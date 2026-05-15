using System.Globalization;
using System.Text.RegularExpressions;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Sepa;

namespace Chipdent.Web.Infrastructure.Tesoreria;

/// <summary>
/// Trasforma le righe importate (CCH/Ident) in Fatture + Scadenze applicando le regole
/// del file <c>Regole scadenzario.xlsx</c>. Documento canonico delle regole:
/// <c>docs/REGOLE_SCADENZIARIO.md</c> — tenere allineato a ogni modifica.
///
/// <para>
/// <b>Pipeline (per ogni riga importata)</b>:
/// <list type="number">
///   <item>Classificazione fornitore (Medico / Laboratorio / Invisalign / Locazione / Riba / CC / …)</item>
///   <item>Match anagrafica fornitore per P.IVA → CF → ragione sociale (autocreazione se assente)</item>
///   <item>Risoluzione clinica destinataria (LOC) — vedi regole sotto</item>
///   <item>Costruzione fattura</item>
///   <item><b>Short-circuit</b> per i fornitori a «pagamenti manuali» (vedi sotto)</item>
///   <item>Calcolo scadenze per tipologia (incl. snap bonifici e ritenute)</item>
///   <item>Arricchimento note con «nota secondaria automatica» della clinica (vedi sotto)</item>
///   <item>Regole di confronto su storico (duplicati / scostamento importo)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>REGOLA «Pagamenti manuali» (Fornitore.PagamentiManuali = true)</b><br/>
/// La fattura viene registrata, ma <b>non</b> viene emessa alcuna scadenza. La posizione
/// confluisce in <see cref="Output.FatturePagamentoManuale"/> e in un alert dedicato.
/// La regola vince su tutte le altre (NC, CC, ritenute incluse): l'operatore calcola e
/// dispone il pagamento manualmente.
/// </para>
///
/// <para>
/// <b>REGOLA «LOC della scadenza» (Clinica.NomeAbbreviato)</b><br/>
/// Il campo LOC mostrato nello scadenziario è derivato dalla clinica destinataria:
/// se <c>Clinica.NomeAbbreviato</c> è valorizzato è usato tale e quale, altrimenti
/// fallback alla tabella statica per nome. Il <see cref="ResolveLoc"/> usa lo stesso
/// campo anche in lettura: l'abbreviazione presente nella sezione/nome fornitore
/// dell'import viene matchata contro <c>Clinica.NomeAbbreviato</c> (priorità) prima
/// che con le sigle storiche hardcoded.
/// </para>
///
/// <para>
/// <b>REGOLA «Modalità DopoIlPagamento → scadenza già pagata»</b><br/>
/// Se l'anagrafica fornitore ha
/// <c>EmissioneFattura = DopoIlPagamento</c> (in UI: «prima pagamento,
/// poi emissione fattura»), tutte le scadenze derivate dalla fattura
/// vengono marcate come <c>StatoScadenza.Pagato</c> con
/// <c>DataPagamento = data fattura</c>. Il razionale è che la fattura
/// arriva nel sistema dopo che il bonifico è già stato disposto, quindi
/// la scadenza è "nata pagata". Viene emesso un alert
/// <b>«Pagamento già effettuato»</b> (Warn) per ricordare di riconciliare
/// con l'estratto conto.
/// </para>
///
/// <para>
/// <b>REGOLA «Nota secondaria automatica» (Clinica.AggiungiNotaSecondariaAutomaticamente)</b><br/>
/// Quando il flag della clinica destinataria è true, il testo di
/// <c>Clinica.NotaSecondariaAutomatica</c> viene appeso (separatore " · ") al campo
/// <c>Scadenza.Note</c> di tutte le scadenze derivate dalla fattura (compresa la rata
/// ritenuta). Pensato per istruzioni operative ricorrenti per sede.
/// </para>
///
/// <para>
/// <b>REGOLA «Note del fornitore»</b><br/>
/// La nota primaria <c>Fornitore.Note</c> viene <b>sempre</b> appesa al campo
/// <c>Scadenza.Note</c> (separatore " · "). La nota secondaria
/// <c>Fornitore.NotaSecondaria</c> viene appesa <b>solo se</b> il flag
/// <c>Fornitore.AggiungiNotaSecondariaAutomaticamente</c> è true; in tal caso
/// le due note vengono concatenate (primaria · secondaria). Le regole clinica e
/// fornitore sono indipendenti e cumulative: se entrambi i blocchi producono
/// testo, vengono appesi in sequenza (prima clinica, poi fornitore).
/// </para>
///
/// <para>
/// <b>Calcolo del MESE DI COMPETENZA</b> (mostrato in UI come "apr 26",
/// formato <c>MMM yy</c> in it-IT)<br/>
/// In ordine di priorità:
/// <list type="number">
///   <item>Estratto dal PDF della fattura (<c>ImportFatturaRiga.MeseCompetenza</c> +
///         <c>AnnoCompetenza</c>) — più affidabile</item>
///   <item>Parsing della causale via regex su nomi mese italiani (es. "Compenso ott 2025")
///         in <c>MeseDaCausale</c></item>
///   <item>Fallback al mese della data fattura</item>
/// </list>
/// La competenza è usata sia per il campo "MeseCompetenza" della fattura sia per
/// calcolare la scadenza dei fornitori medici/laboratorio/direzione sanitaria
/// (60 gg fine mese rispetto al mese di competenza).
/// </para>
/// </summary>
public static class ScadenziarioGenerator
{
    private static readonly CultureInfo It = new("it-IT");

    public sealed class Input
    {
        public required string TenantId { get; init; }
        public required IReadOnlyList<ImportFatturaRiga> Righe { get; init; }
        public required IReadOnlyList<Fornitore> Fornitori { get; init; }
        public required IReadOnlyList<Clinica> Cliniche { get; init; }
        public required IReadOnlyList<Dottore> Dottori { get; init; }
        /// <summary>Anagrafica delle Società del gruppo. Usata da
        /// <see cref="ResolveLoc"/> per matchare il cessionario letto dal PDF
        /// alla Società destinataria e risalire alla clinica (LOC).</summary>
        public IReadOnlyList<Societa> Societa { get; init; } = Array.Empty<Societa>();
        /// <summary>Userid che firma le fatture generate (creator/approver).</summary>
        public string? UserId { get; init; }
    }

    public sealed class Output
    {
        public List<FatturaFornitore> Fatture { get; } = new();
        public List<ScadenzaPagamento> Scadenze { get; } = new();
        public List<Fornitore> FornitoriNuovi { get; } = new();
        public List<AlertScadenziario> Alerts { get; } = new();

        /// <summary>
        /// Fatture che NON hanno prodotto scadenza perché il fornitore ha il flag
        /// <see cref="Fornitore.PagamentiManuali"/> attivo. Tabella mostrata
        /// nell'anteprima di generazione scadenziario come alert dedicato: per
        /// queste fatture il pagamento va calcolato e disposto manualmente
        /// (l'operatore decide importo, data, metodo).
        /// </summary>
        public List<FatturaPagamentoManuale> FatturePagamentoManuale { get; } = new();

        public int RigheElaborate { get; set; }
        public int RigheSaltate { get; set; }
    }

    /// <summary>
    /// Record di una fattura per la quale la generazione di scadenza è stata
    /// saltata per via del flag «pagamenti manuali» sul fornitore.
    /// </summary>
    public sealed record FatturaPagamentoManuale(
        string FornitoreId,
        string FornitoreNome,
        string? NumeroDoc,
        DateTime DataDocumento,
        decimal Totale,
        string? ClinicaId,
        int RigaSorgente);

    /// <summary>Tipologia operativa derivata dal nome fornitore (governa la regola di scadenza).</summary>
    public enum TipoFornitoreOp
    {
        Generico,
        Medico,
        DirezioneSanitaria,
        Laboratorio,
        Invisalign,
        CartaCredito,
        Compass,
        DeutscheBank,
        ImportoFisso,    // Cristal, Infinity, Locazioni — alert su scostamento importo
        Locazione,
        Riba             // pagamento con RIBA: regola "verifica scadenze multiple"
    }

    public sealed record AlertScadenziario(
        AlertSeverita Severita,
        string Regola,
        string Messaggio,
        string? FornitoreNome,
        string? NumeroDoc,
        DateTime? DataDocumento,
        int RigaSorgente);

    public enum AlertSeverita { Info, Warn, Err }

    public static Output Genera(Input input)
    {
        var output = new Output();
        // Indici di lookup multipli: P.IVA e CF (più stabili del nome), poi RagioneSociale
        // come fallback. Le fatture importate possono arrivare con P.IVA e CF dal PDF: se
        // matchano un fornitore esistente, evitiamo di crearne uno duplicato per varianti
        // del nome (es. "AGESP S.P.A." vs "AGESP SPA").
        var fornitoriByPiva = new Dictionary<string, Fornitore>(StringComparer.OrdinalIgnoreCase);
        var fornitoriByCf = new Dictionary<string, Fornitore>(StringComparer.OrdinalIgnoreCase);
        var fornitoriByKey = new Dictionary<string, Fornitore>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in input.Fornitori)
        {
            fornitoriByKey[NormalizeNome(f.RagioneSociale)] = f;
            var piva = NormalizeIdFiscale(f.PartitaIva);
            if (!string.IsNullOrEmpty(piva)) fornitoriByPiva[piva] = f;
            var cf = NormalizeIdFiscale(f.CodiceFiscale);
            if (!string.IsNullOrEmpty(cf)) fornitoriByCf[cf] = f;
        }

        var medici = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in input.Dottori) medici.Add(NormalizeNome($"{d.Cognome} {d.Nome}"));

        var cliniche = input.Cliniche.ToDictionary(c => c.Nome ?? "", c => c, StringComparer.OrdinalIgnoreCase);
        var clinicheById = input.Cliniche.ToDictionary(c => c.Id, c => c);
        var cch = input.Cliniche.FirstOrDefault(c => c.IsHolding) ??
                  input.Cliniche.FirstOrDefault(c => string.Equals(c.Nome, "CCH", StringComparison.OrdinalIgnoreCase));

        // ── Indici Società per match cessionario → Società → Clinica ─────
        // P.IVA e CF sono identificatori legali univoci. La RagioneSociale
        // è l'ultimo fallback e meno robusta (varianti tipografiche).
        var societaByPiva = new Dictionary<string, Societa>(StringComparer.OrdinalIgnoreCase);
        var societaByCf = new Dictionary<string, Societa>(StringComparer.OrdinalIgnoreCase);
        var societaByNome = new Dictionary<string, Societa>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in input.Societa)
        {
            var sp = NormalizeIdFiscale(s.PartitaIva);
            if (!string.IsNullOrEmpty(sp)) societaByPiva[sp] = s;
            var sc = NormalizeIdFiscale(s.CodiceFiscale);
            if (!string.IsNullOrEmpty(sc)) societaByCf[sc] = s;
            if (!string.IsNullOrWhiteSpace(s.RagioneSociale))
                societaByNome[NormalizeNome(s.RagioneSociale)] = s;
            if (!string.IsNullOrWhiteSpace(s.Nome))
                societaByNome.TryAdd(NormalizeNome(s.Nome), s);
        }
        // Una Società ha (al più) una clinica associata (Clinica.SocietaId).
        // Costruiamo l'indice inverso per risolvere LOC da Società.
        var clinicaBySocietaId = input.Cliniche
            .Where(c => !string.IsNullOrEmpty(c.SocietaId))
            .GroupBy(c => c.SocietaId!)
            .ToDictionary(g => g.Key, g => g.First());

        // Storico per fornitore costruito incrementalmente, usato per duplicate-detection,
        // copia IBAN precedente e confronto importo (Cristal/Infinity/Locazioni).
        var ibanCacheByForn = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var importiByForn = new Dictionary<string, List<(DateTime data, decimal importo)>>(StringComparer.OrdinalIgnoreCase);

        // Ordine cronologico per applicare le regole di "fattura precedente"
        // in modo deterministico (più vecchia → più recente).
        var righeOrdinate = input.Righe
            .Where(r => !r.HaErrori && r.TotaleDocumento.HasValue && !string.IsNullOrWhiteSpace(r.Fornitore))
            .OrderBy(r => r.DataDocumento ?? r.DataRegistrazione ?? DateTime.MinValue)
            .ThenBy(r => r.NumeroRiga)
            .ToList();

        output.RigheSaltate = input.Righe.Count - righeOrdinate.Count;

        foreach (var riga in righeOrdinate)
        {
            output.RigheElaborate++;

            var nomeForn = (riga.Fornitore ?? "").Trim();
            var key = NormalizeNome(nomeForn);
            var dataDoc = riga.DataDocumento ?? riga.DataRegistrazione ?? DateTime.UtcNow.Date;
            dataDoc = DateTime.SpecifyKind(dataDoc.Date, DateTimeKind.Utc);

            // ── Classificazione fornitore ────────────────────────────────
            var (tipo, ambiguita) = ClassificaConAmbiguita(nomeForn, medici, riga.Causale);
            if (ambiguita is not null)
            {
                output.Alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Catalogazione ambigua",
                    $"Fornitore «{nomeForn}» riconducibile a più tipi ({ambiguita}); applicato «{tipo}». Verificare manualmente.",
                    nomeForn, riga.Numero, dataDoc, riga.NumeroRiga));
            }

            // ── Match / autocreazione fornitore ──────────────────────────
            // Priorità: 1) Partita IVA da PDF, 2) Codice Fiscale da PDF, 3) RagioneSociale.
            // P.IVA/CF sono identificatori legali univoci e resistono alle varianti tipografiche
            // del nome ("S.p.A." vs "SPA", maiuscole/minuscole, abbreviazioni).
            var piva = NormalizeIdFiscale(riga.PartitaIvaFornitore);
            var cf = NormalizeIdFiscale(riga.CodiceFiscaleFornitore);
            Fornitore? fornitore = null;
            if (!string.IsNullOrEmpty(piva)) fornitoriByPiva.TryGetValue(piva, out fornitore);
            if (fornitore is null && !string.IsNullOrEmpty(cf)) fornitoriByCf.TryGetValue(cf, out fornitore);
            if (fornitore is null) fornitoriByKey.TryGetValue(key, out fornitore);

            if (fornitore is null)
            {
                fornitore = new Fornitore
                {
                    TenantId = input.TenantId,
                    RagioneSociale = nomeForn,
                    RagioneSocialePagamento = nomeForn,
                    PartitaIva = string.IsNullOrEmpty(piva) ? null : riga.PartitaIvaFornitore,
                    CodiceFiscale = string.IsNullOrEmpty(cf) ? null : riga.CodiceFiscaleFornitore,
                    CategoriaDefault = MappaCategoriaDefault(tipo),
                    TerminiPagamentoGiorni = TipiATerminiGiorni(tipo),
                    BasePagamento = TipiABasePagamento(tipo),
                    Stato = StatoFornitore.Attivo
                };
                fornitoriByKey[key] = fornitore;
                if (!string.IsNullOrEmpty(piva)) fornitoriByPiva[piva] = fornitore;
                if (!string.IsNullOrEmpty(cf)) fornitoriByCf[cf] = fornitore;
                output.FornitoriNuovi.Add(fornitore);
                output.Alerts.Add(new AlertScadenziario(AlertSeverita.Info, "Catalogazione fornitore",
                    $"Creato nuovo fornitore «{nomeForn}» (tipo: {tipo}). Verifica IBAN e termini.",
                    nomeForn, riga.Numero, dataDoc, riga.NumeroRiga));
            }

            // ── Clinica destinataria (LOC) ───────────────────────────────
            var clinicaId = ResolveLoc(riga, nomeForn, cch, cliniche, input.Cliniche,
                societaByPiva, societaByCf, societaByNome, clinicaBySocietaId);
            if (string.IsNullOrEmpty(clinicaId))
            {
                output.Alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "LOC mancante",
                    $"Impossibile determinare la sede destinataria da «{nomeForn}» / sezione «{riga.Sezione}». Assegnare manualmente.",
                    nomeForn, riga.Numero, dataDoc, riga.NumeroRiga));
            }

            // ── Importi (controllata vs Confident) ───────────────────────
            // Confident (holding CCH) → netto + iva + lordo distinti
            // Controllate → solo lordo (iva = 0)
            var totale = riga.TotaleDocumento ?? 0m;
            var ritenuta = riga.Ritenuta ?? 0m;
            var netto = riga.NettoAPagare ?? (totale - ritenuta);
            var isHoldingCch = string.Equals(riga.Sezione, "CCH", StringComparison.OrdinalIgnoreCase);
            decimal imponibile, ivaAmount;
            if (isHoldingCch && riga.Iva.HasValue)
            {
                ivaAmount = riga.Iva.Value;
                imponibile = totale - ivaAmount;
            }
            else
            {
                ivaAmount = 0m;
                imponibile = totale;
            }

            // ── Nota di credito (totale negativo o tipo doc NC) ──────────
            var isNotaCredito = totale < 0
                || (riga.TipoDocumento ?? "").IndexOf("NC", StringComparison.OrdinalIgnoreCase) >= 0
                || (riga.TipoDocumento ?? "").Contains("credito", StringComparison.OrdinalIgnoreCase);
            if (isNotaCredito && totale > 0) totale = -Math.Abs(totale); // forziamo segno

            // ── Parcella: alert anti-doppione ────────────────────────────
            var causaleLower = (riga.Causale ?? "").ToLowerInvariant();
            if (causaleLower.Contains("parcella"))
            {
                output.Alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Parcella",
                    "Riferimento a parcella in causale: verificare possibile doppione con il compenso del professionista.",
                    nomeForn, riga.Numero, dataDoc, riga.NumeroRiga));
            }

            // ── Costruzione Fattura ──────────────────────────────────────
            var fattura = new FatturaFornitore
            {
                TenantId = input.TenantId,
                FornitoreId = fornitore.Id,                   // collegamento posticipato (vedi sotto)
                ClinicaId = clinicaId ?? string.Empty,
                Numero = string.IsNullOrWhiteSpace(riga.Numero) ? $"AUTO-{riga.NumeroRiga}" : riga.Numero.Trim(),
                DataEmissione = dataDoc,
                MeseCompetenza = MeseCompetenza(dataDoc, riga.Causale, riga),
                Categoria = MappaCategoriaDefault(tipo),
                Imponibile = imponibile,
                Iva = ivaAmount,
                Totale = totale,
                TipoEmissione = TipoEmissioneFattura.Elettronica, // import zucchetti = SDI per definizione
                FlagEM = "E",
                Note = isNotaCredito ? $"NC importata · {riga.Causale}" : riga.Causale,
                Stato = StatoFattura.Approvata,
                Origine = OrigineFattura.ImportExcel,
                CaricataDaUserId = input.UserId,
                ApprovataIl = DateTime.UtcNow,
                ApprovataDaUserId = input.UserId
            };
            output.Fatture.Add(fattura);

            // ── REGOLA «Pagamenti manuali» ───────────────────────────────
            // Se il fornitore ha il flag attivo, la fattura è già stata
            // registrata sopra ma NON generiamo nessuna scadenza: la posizione
            // entra nella tabella alert dedicata. La regola vince su tutte le
            // altre (NC, CC, ritenute incluse).
            if (fornitore.PagamentiManuali)
            {
                output.FatturePagamentoManuale.Add(new FatturaPagamentoManuale(
                    fornitore.Id, fornitore.RagioneSociale, riga.Numero, dataDoc,
                    totale, fattura.ClinicaId, riga.NumeroRiga));
                output.Alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Pagamento manuale",
                    $"Fornitore «{fornitore.RagioneSociale}» è marcato come «pagamenti manuali»: fattura registrata senza scadenza. Calcolare e disporre il pagamento a mano.",
                    fornitore.RagioneSociale, riga.Numero, dataDoc, riga.NumeroRiga));

                // Importi storici comunque aggiornati per duplicate-detection.
                if (!importiByForn.TryGetValue(key, out var listaMan))
                {
                    listaMan = new List<(DateTime, decimal)>();
                    importiByForn[key] = listaMan;
                }
                listaMan.Add((dataDoc, totale));
                continue;
            }

            // ── IBAN: priorità a quello letto dal PDF della singola fattura
            //    (più affidabile e specifico), poi anagrafica fornitore, poi
            //    cache della fattura precedente dello stesso fornitore.
            var iban = !string.IsNullOrWhiteSpace(riga.IbanFornitore)
                ? riga.IbanFornitore
                : fornitore.Iban;
            if (string.IsNullOrWhiteSpace(iban) && ibanCacheByForn.TryGetValue(key, out var prevIban))
                iban = prevIban;
            if (!string.IsNullOrWhiteSpace(iban)) ibanCacheByForn[key] = iban;

            var scadenze = BuildScadenze(
                tipo, fattura, fornitore, dataDoc, totale, netto, ritenuta,
                isNotaCredito, iban, output.Alerts, riga);

            // ── REGOLA «Modalità DopoIlPagamento → scadenza già pagata» ──
            // Per i fornitori che emettono la fattura DOPO aver ricevuto il
            // pagamento, quando l'import arriva il bonifico è già stato
            // disposto: marchiamo TUTTE le scadenze derivate come Pagato
            // alla data fattura. Alert dedicato per segnalarlo all'operatore.
            if (fornitore.EmissioneFattura == EmissioneFattura.DopoIlPagamento && scadenze.Count > 0)
            {
                foreach (var s in scadenze)
                {
                    s.Stato = StatoScadenza.Pagato;
                    s.DataPagamento = DateTime.SpecifyKind(dataDoc.Date, DateTimeKind.Utc);
                    s.Note = string.IsNullOrWhiteSpace(s.Note)
                        ? "Pagamento già effettuato (fattura emessa post-pagamento)"
                        : $"{s.Note} · Pagamento già effettuato (fattura emessa post-pagamento)";
                }
                output.Alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Pagamento già effettuato",
                    $"Fornitore «{fornitore.RagioneSociale}» configurato come «prima pagamento, poi emissione fattura»: scadenza marcata come PAGATA al {dataDoc:dd/MM/yyyy}. Verificare riconciliazione con l'estratto conto.",
                    fornitore.RagioneSociale, riga.Numero, dataDoc, riga.NumeroRiga));
            }

            // ── REGOLA «Nota secondaria automatica» (CLINICA) ───────────
            // Se la clinica destinataria della fattura ha il flag attivo,
            // appendiamo la NotaSecondariaAutomatica a Scadenza.Note. Si
            // applica a TUTTE le scadenze derivate (compresa la rata ritenuta).
            if (!string.IsNullOrEmpty(clinicaId)
                && clinicheById.TryGetValue(clinicaId, out var clinicaDest)
                && clinicaDest.AggiungiNotaSecondariaAutomaticamente
                && !string.IsNullOrWhiteSpace(clinicaDest.NotaSecondariaAutomatica))
            {
                var nota = clinicaDest.NotaSecondariaAutomatica.Trim();
                foreach (var s in scadenze)
                {
                    s.Note = string.IsNullOrWhiteSpace(s.Note) ? nota : $"{s.Note} · {nota}";
                }
            }

            // ── REGOLA «Note del fornitore» ──────────────────────────────
            // - Note primarie (Fornitore.Note): SEMPRE appese alla scadenza.
            // - Nota secondaria (Fornitore.NotaSecondaria): appesa SOLO se il
            //   flag <c>AggiungiNotaSecondariaAutomaticamente</c> è true.
            // Quando entrambe sono presenti vengono concatenate in sequenza
            // (separatore " · "). Se la nota primaria è vuota e il flag della
            // secondaria è attivo, viene appesa solo la secondaria.
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(fornitore.Note))
                    parts.Add(fornitore.Note.Trim());
                if (fornitore.AggiungiNotaSecondariaAutomaticamente
                    && !string.IsNullOrWhiteSpace(fornitore.NotaSecondaria))
                    parts.Add(fornitore.NotaSecondaria.Trim());
                if (parts.Count > 0)
                {
                    var notaForn = string.Join(" · ", parts);
                    foreach (var s in scadenze)
                    {
                        s.Note = string.IsNullOrWhiteSpace(s.Note) ? notaForn : $"{s.Note} · {notaForn}";
                    }
                }
            }

            output.Scadenze.AddRange(scadenze);

            // ── Duplicate / scostamento importo ─────────────────────────
            if (!importiByForn.TryGetValue(key, out var lista))
            {
                lista = new List<(DateTime, decimal)>();
                importiByForn[key] = lista;
            }
            ApplicaRegoleConfronto(tipo, totale, dataDoc, nomeForn, riga, lista, output.Alerts);
            lista.Add((dataDoc, totale));
        }

        return output;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Classificazione & mappature
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifica il fornitore raccogliendo TUTTI i match e segnalando ambiguità.
    /// Ordine di priorità (più specifico prima): Invisalign → Compass → DeutscheBank →
    /// CartaCredito → RIBA → ImportoFisso → Locazione → DirezioneSanitaria → Laboratorio → Medico → Generico.
    /// </summary>
    private static (TipoFornitoreOp Tipo, string? Ambiguita) ClassificaConAmbiguita(
        string nome, HashSet<string> medici, string? causale)
    {
        var n = nome.ToLowerInvariant();
        var c = (causale ?? "").ToLowerInvariant();
        var hits = new List<TipoFornitoreOp>();

        if (n.Contains("invisalign") || n.Contains("align technology")) hits.Add(TipoFornitoreOp.Invisalign);
        if (n.Contains("compass")) hits.Add(TipoFornitoreOp.Compass);
        if (n.Contains("deutsche bank")) hits.Add(TipoFornitoreOp.DeutscheBank);
        if (n.Contains("amex") || n.Contains("american express") || (n.Contains("carta") && n.Contains("credito")))
            hits.Add(TipoFornitoreOp.CartaCredito);
        if (n.Contains("riba") || c.Contains("riba")) hits.Add(TipoFornitoreOp.Riba);
        if (n.Contains("cristal") || n.Contains("infinity")) hits.Add(TipoFornitoreOp.ImportoFisso);
        if (n.Contains("locazione") || n.Contains("immobiliare") || n.Contains("affitto"))
            hits.Add(TipoFornitoreOp.Locazione);
        if (n.Contains("direzione sanitaria") || n.Contains("dir. sanitaria") || n.Contains("dir san"))
            hits.Add(TipoFornitoreOp.DirezioneSanitaria);
        if (n.Contains("laboratorio") || n.Contains("odontotecnic") || n.Contains("lab.") || n.Contains("lab ortodont"))
            hits.Add(TipoFornitoreOp.Laboratorio);

        // Medici: match per cognome+nome (dottori a libera professione fatturano a nome proprio)
        var isMedico = medici.Contains(NormalizeNome(nome));
        if (!isMedico
            && !nome.Contains("S.r.l", StringComparison.OrdinalIgnoreCase)
            && !nome.Contains("SRL", StringComparison.OrdinalIgnoreCase)
            && !nome.Contains("SPA", StringComparison.OrdinalIgnoreCase)
            && !nome.Contains("S.p.A", StringComparison.OrdinalIgnoreCase)
            && !nome.Contains("S.A.S", StringComparison.OrdinalIgnoreCase)
            && !nome.Contains("SNC", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(nome, @"^[A-Za-zÀ-ÿ'\s]+$")
            && nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is >= 2 and <= 4
            && medici.Any(m => m.Split(' ')[0].Equals(NormalizeNome(nome).Split(' ')[0], StringComparison.OrdinalIgnoreCase)))
        {
            isMedico = true;
        }
        if (isMedico) hits.Add(TipoFornitoreOp.Medico);

        if (hits.Count == 0) return (TipoFornitoreOp.Generico, null);
        if (hits.Count == 1) return (hits[0], null);

        // Ambiguità: applichiamo la prima (priorità lista) ma riportiamo tutte le candidate.
        return (hits[0], string.Join(" / ", hits));
    }

    private static CategoriaSpesa MappaCategoriaDefault(TipoFornitoreOp t) => t switch
    {
        TipoFornitoreOp.Medico => CategoriaSpesa.Medici,
        TipoFornitoreOp.DirezioneSanitaria => CategoriaSpesa.DirezioneSanitaria,
        TipoFornitoreOp.Laboratorio => CategoriaSpesa.Laboratorio,
        TipoFornitoreOp.Invisalign => CategoriaSpesa.Laboratorio,
        TipoFornitoreOp.CartaCredito => CategoriaSpesa.AltreSpeseFisse,
        TipoFornitoreOp.Compass => CategoriaSpesa.FinanziamentiPassivi,
        TipoFornitoreOp.DeutscheBank => CategoriaSpesa.FinanziamentiPassivi,
        TipoFornitoreOp.Locazione => CategoriaSpesa.Locazione,
        TipoFornitoreOp.ImportoFisso => CategoriaSpesa.AltreSpeseFisse,
        TipoFornitoreOp.Riba => CategoriaSpesa.AltreSpeseFisse,
        _ => CategoriaSpesa.AltreSpeseFisse
    };

    private static int TipiATerminiGiorni(TipoFornitoreOp t) => t switch
    {
        TipoFornitoreOp.Invisalign => 150,
        TipoFornitoreOp.Medico or TipoFornitoreOp.DirezioneSanitaria or TipoFornitoreOp.Laboratorio => 60,
        _ => 30
    };

    private static BasePagamento TipiABasePagamento(TipoFornitoreOp t) => t switch
    {
        TipoFornitoreOp.Invisalign => BasePagamento.DataFattura,
        TipoFornitoreOp.Medico or TipoFornitoreOp.DirezioneSanitaria or TipoFornitoreOp.Laboratorio
            => BasePagamento.FineMeseFattura,
        _ => BasePagamento.FineMeseFattura
    };

    // ─────────────────────────────────────────────────────────────────
    //  Generazione scadenze per regola
    // ─────────────────────────────────────────────────────────────────

    private static List<ScadenzaPagamento> BuildScadenze(
        TipoFornitoreOp tipo,
        FatturaFornitore fattura,
        Fornitore fornitore,
        DateTime dataDoc,
        decimal totale,
        decimal netto,
        decimal ritenuta,
        bool isNotaCredito,
        string? iban,
        List<AlertScadenziario> alerts,
        ImportFatturaRiga riga)
    {
        var oggi = DateTime.UtcNow.Date;
        var result = new List<ScadenzaPagamento>();

        // ── Note di credito Compass/Deutsche Bank: importo negativo, RID, "pagata" alla data fattura ──
        if (isNotaCredito && (tipo == TipoFornitoreOp.Compass || tipo == TipoFornitoreOp.DeutscheBank))
        {
            result.Add(NuovaScadenza(fattura, fornitore, dataDoc, totale, MetodoPagamento.Rid, iban,
                stato: StatoScadenza.Pagato, dataPagamento: dataDoc,
                note: "NC Compass/DB · RID compensativa"));
            return result;
        }

        // ── Note di credito generiche: importo negativo, bonifico fine mese fattura, alert ──
        if (isNotaCredito)
        {
            var fineMese = UltimoGiorno(dataDoc);
            result.Add(NuovaScadenza(fattura, fornitore, fineMese, totale, MetodoPagamento.Bonifico, iban,
                note: "Nota di credito · verificare compensazione"));
            alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Nota di credito",
                "Nota di credito: importo registrato in negativo, scadenza al 30/31 del mese fattura. Verifica abbinamento alla fattura originale.",
                fornitore.RagioneSociale, riga.Numero, dataDoc, riga.NumeroRiga));
            return result;
        }

        // ── Carta di credito: registrata come Pagato alla data fattura ──
        if (tipo == TipoFornitoreOp.CartaCredito)
        {
            result.Add(NuovaScadenza(fattura, fornitore, dataDoc, totale, MetodoPagamento.CartaCredito, iban,
                stato: StatoScadenza.Pagato, dataPagamento: dataDoc,
                note: "CC · estratto conto"));
            return result;
        }

        // ── Bonifico/RID/RIBA secondo termini fornitore ──
        var metodo = MetodoDefault(fornitore, tipo);
        var importoBonifico = totale;

        // Ritenuta: separa netto (bonifico) e ritenuta (16 mese successivo)
        if (ritenuta > 0 && netto > 0)
        {
            importoBonifico = netto;
        }

        var scadenza = CalcolaScadenzaPrincipale(tipo, dataDoc, riga, fornitore, alerts);

        // Snap dei bonifici al 10 o 30/31 del mese.
        // Per RID e RIBA si "copia la data scadenza" (regola Excel) — niente snap.
        if (metodo == MetodoPagamento.Bonifico)
        {
            var snapped = SnapBonifico(scadenza, anticipa: tipo == TipoFornitoreOp.Invisalign);
            if (snapped != scadenza)
            {
                alerts.Add(new AlertScadenziario(AlertSeverita.Info, "Snap bonifico",
                    $"Scadenza calcolata {scadenza:dd/MM/yyyy} riallineata a {snapped:dd/MM/yyyy} (regola: bonifici solo al 10 o al 30/31).",
                    fornitore.RagioneSociale, riga.Numero, dataDoc, riga.NumeroRiga));
            }
            scadenza = snapped;
        }

        // RID / RIBA: "copiare data scadenza" dal documento.
        // Se la fattura è arricchita dal PDF e contiene DataScadenzaPdf, la
        // copiamo letteralmente (regola Excel D7/D8). Altrimenti rimaniamo
        // sulla scadenza calcolata dai termini e generiamo un alert informativo.
        if ((metodo == MetodoPagamento.Rid || metodo == MetodoPagamento.Riba) && !isNotaCredito)
        {
            if (riga.DataScadenzaPdf.HasValue)
            {
                scadenza = DateTime.SpecifyKind(riga.DataScadenzaPdf.Value.Date, DateTimeKind.Utc);
            }
            else
            {
                alerts.Add(new AlertScadenziario(AlertSeverita.Info,
                    metodo == MetodoPagamento.Rid ? "RID da verificare" : "RIBA da verificare",
                    $"Pagamento {metodo} per «{fornitore.RagioneSociale}»: data scadenza calcolata dai termini contrattuali (il PDF non era disponibile o non l'ha esposta — copiarla manualmente se diversa).",
                    fornitore.RagioneSociale, riga.Numero, dataDoc, riga.NumeroRiga));
            }
        }

        if (string.IsNullOrWhiteSpace(iban) && metodo == MetodoPagamento.Bonifico)
        {
            alerts.Add(new AlertScadenziario(AlertSeverita.Err, "IBAN mancante",
                $"Bonifico senza IBAN: aggiornare l'anagrafica fornitore «{fornitore.RagioneSociale}» o usare l'IBAN della fattura precedente.",
                fornitore.RagioneSociale, riga.Numero, dataDoc, riga.NumeroRiga));
        }

        var principale = NuovaScadenza(fattura, fornitore, scadenza, importoBonifico, metodo, iban,
            stato: scadenza < oggi ? StatoScadenza.Insoluto : StatoScadenza.DaPagare);
        result.Add(principale);

        // Ritenuta professionisti → al 16 del mese successivo al bonifico
        if (ritenuta > 0)
        {
            var meseRitenuta = scadenza.AddMonths(1);
            var dataRit = new DateTime(meseRitenuta.Year, meseRitenuta.Month, 16);
            var ritScad = NuovaScadenza(fattura, fornitore, dataRit, ritenuta, MetodoPagamento.Bonifico, iban,
                note: "Ritenuta d'acconto · F24 16 mese successivo");
            ritScad.ScadenzaPadreId = principale.Id; // valorizzato lato controller dopo l'insert? no: Id già generato qui
            result.Add(ritScad);
        }

        return result;
    }

    private static MetodoPagamento MetodoDefault(Fornitore f, TipoFornitoreOp tipo)
    {
        // I metodi reali in produzione li sceglierà l'utente: qui usiamo il default più comune
        // per tipologia, lasciando comunque modificabile la scadenza generata.
        return tipo switch
        {
            TipoFornitoreOp.Compass => MetodoPagamento.Rid,
            TipoFornitoreOp.DeutscheBank => MetodoPagamento.Rid,
            TipoFornitoreOp.Riba => MetodoPagamento.Riba,
            _ => MetodoPagamento.Bonifico
        };
    }

    private static DateTime CalcolaScadenzaPrincipale(
        TipoFornitoreOp tipo, DateTime dataDoc, ImportFatturaRiga riga, Fornitore f,
        List<AlertScadenziario> alerts)
    {
        switch (tipo)
        {
            case TipoFornitoreOp.Invisalign:
                return dataDoc.AddDays(150);

            case TipoFornitoreOp.Medico:
            case TipoFornitoreOp.DirezioneSanitaria:
            case TipoFornitoreOp.Laboratorio:
                {
                    // 60 gg fine-mese rispetto al mese di competenza.
                    // Priorità: 1) competenza estratta dal PDF (più affidabile),
                    //          2) parsing della causale CSV,
                    //          3) fallback alla data fattura (con alert).
                    DateTime? meseOggetto = null;

                    if (riga.MeseCompetenza is int m && riga.AnnoCompetenza is int a)
                    {
                        meseOggetto = new DateTime(a, m, 1);
                    }
                    if (meseOggetto == null)
                    {
                        meseOggetto = MeseDaCausale(riga.Causale);
                    }
                    if (meseOggetto.HasValue)
                    {
                        return UltimoGiorno(meseOggetto.Value).AddDays(60);
                    }
                    alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Mese di competenza assente",
                        "Mese di competenza non disponibile né dal PDF né dalla causale: scadenza calcolata sul mese fattura. Verifica manualmente.",
                        f.RagioneSociale, riga.Numero, dataDoc, riga.NumeroRiga));
                    return UltimoGiorno(dataDoc).AddDays(60);
                }

            default:
                // Bonifici generici: 30 gg fine-mese data fattura
                return PagamentiHelper.CalcolaScadenzaAttesa(dataDoc,
                    f.TerminiPagamentoGiorni > 0 ? f.TerminiPagamentoGiorni : 30,
                    f.BasePagamento);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Regole di confronto (duplicati / scostamento importo)
    // ─────────────────────────────────────────────────────────────────

    private static void ApplicaRegoleConfronto(
        TipoFornitoreOp tipo, decimal totale, DateTime dataDoc, string nomeForn,
        ImportFatturaRiga riga, List<(DateTime data, decimal importo)> storico,
        List<AlertScadenziario> alerts)
    {
        if (storico.Count == 0) return;

        // Cristal / Infinity / Locazioni: alert se importo DIVERSO dal precedente
        if (tipo == TipoFornitoreOp.ImportoFisso || tipo == TipoFornitoreOp.Locazione)
        {
            var ultimo = storico[^1].importo;
            if (Math.Abs(ultimo - totale) > 0.01m)
            {
                alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Scostamento importo",
                    $"Importo {totale:N2} € differisce dal precedente ({ultimo:N2} €) per fornitore «{nomeForn}» (canone/fisso atteso).",
                    nomeForn, riga.Numero, dataDoc, riga.NumeroRiga));
            }
            return;
        }

        // Altri fornitori: alert se importo UGUALE a una fattura recente (≤60gg) → possibile doppione
        var duplicato = storico.LastOrDefault(s =>
            Math.Abs(s.importo - totale) <= 0.01m && (dataDoc - s.data).TotalDays <= 60);
        if (duplicato != default)
        {
            alerts.Add(new AlertScadenziario(AlertSeverita.Warn, "Possibile doppione",
                $"Stesso importo {totale:N2} € già registrato il {duplicato.data:dd/MM/yyyy} per «{nomeForn}». Verifica.",
                nomeForn, riga.Numero, dataDoc, riga.NumeroRiga));
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────

    private static ScadenzaPagamento NuovaScadenza(
        FatturaFornitore fattura, Fornitore fornitore, DateTime data, decimal importo,
        MetodoPagamento metodo, string? iban,
        StatoScadenza stato = StatoScadenza.DaPagare,
        DateTime? dataPagamento = null,
        string? note = null)
    {
        return new ScadenzaPagamento
        {
            TenantId = fattura.TenantId,
            FatturaId = fattura.Id,
            FornitoreId = fornitore.Id,
            ClinicaId = fattura.ClinicaId,
            Categoria = fattura.Categoria,
            DataScadenza = DateTime.SpecifyKind(data.Date, DateTimeKind.Utc),
            DataScadenzaAttesa = DateTime.SpecifyKind(data.Date, DateTimeKind.Utc),
            Importo = importo,
            Metodo = metodo,
            Iban = iban,
            Stato = stato,
            DataPagamento = dataPagamento.HasValue
                ? DateTime.SpecifyKind(dataPagamento.Value.Date, DateTimeKind.Utc)
                : null,
            Note = note
        };
    }

    /// <summary>
    /// Risolve la clinica destinataria della fattura (campo LOC dello scadenziario =
    /// <see cref="Clinica.NomeAbbreviato"/>). Ordine di priorità — dal segnale più
    /// affidabile al più euristico:
    /// <list type="number">
    ///   <item>Cessionario letto dal PDF (P.IVA → CF → Ragione sociale) → Società →
    ///         Clinica con quella <c>SocietaId</c>. È il segnale più affidabile
    ///         perché la fatturazione elettronica vincola P.IVA/CF al destinatario.</item>
    ///   <item>Sezione CSV "CCH" → holding</item>
    ///   <item>LOC rilevata dal testo descrittivo del PDF (causale, descrizioni di
    ///         riga) — match contro <c>Clinica.NomeAbbreviato</c></item>
    ///   <item>Match della <c>Sezione</c> CSV o del nome fornitore con
    ///         <c>Clinica.NomeAbbreviato</c></item>
    ///   <item>Tabella statica di fallback (sigle storiche)</item>
    /// </list>
    /// </summary>
    private static string? ResolveLoc(
        ImportFatturaRiga riga, string nomeForn, Clinica? cch,
        Dictionary<string, Clinica> cliniche,
        IReadOnlyList<Clinica> clinicheAll,
        Dictionary<string, Societa> societaByPiva,
        Dictionary<string, Societa> societaByCf,
        Dictionary<string, Societa> societaByNome,
        Dictionary<string, Clinica> clinicaBySocietaId)
    {
        // 1) Cessionario PDF → Società → Clinica (segnale più affidabile)
        Societa? societa = null;
        var cessP = NormalizeIdFiscale(riga.PartitaIvaCessionario);
        if (!string.IsNullOrEmpty(cessP)) societaByPiva.TryGetValue(cessP, out societa);
        if (societa is null)
        {
            var cessC = NormalizeIdFiscale(riga.CodiceFiscaleCessionario);
            if (!string.IsNullOrEmpty(cessC)) societaByCf.TryGetValue(cessC, out societa);
        }
        if (societa is null && !string.IsNullOrWhiteSpace(riga.RagioneSocialeCessionario))
        {
            societaByNome.TryGetValue(NormalizeNome(riga.RagioneSocialeCessionario), out societa);
        }
        if (societa is not null && clinicaBySocietaId.TryGetValue(societa.Id, out var cliFromSoc))
            return cliFromSoc.Id;

        // 2) Sezione CCH → holding
        if (string.Equals(riga.Sezione, "CCH", StringComparison.OrdinalIgnoreCase) && cch != null)
            return cch.Id;

        // 3) LOC rilevata dal testo descrittivo del PDF
        if (!string.IsNullOrWhiteSpace(riga.LocRilevataDaTesto))
        {
            var locUp = riga.LocRilevataDaTesto.Trim().ToUpperInvariant();
            foreach (var cli in clinicheAll)
            {
                var abbr = (cli.NomeAbbreviato ?? "").Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(abbr) && string.Equals(abbr, locUp, StringComparison.Ordinal))
                    return cli.Id;
            }
            // Anche match sul Nome (es. "DESIO", "VARESE")
            if (cliniche.TryGetValue(locUp, out var byNome)) return byNome.Id;
        }

        var n = nomeForn.ToUpperInvariant();

        // 4) Match per NomeAbbreviato sulla sezione/nome fornitore.
        foreach (var cli in clinicheAll)
        {
            var abbr = (cli.NomeAbbreviato ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(abbr)) continue;
            if (string.Equals(riga.Sezione, abbr, StringComparison.OrdinalIgnoreCase)) return cli.Id;
            if (n.Contains(" " + abbr) || n.EndsWith(" " + abbr) || n.EndsWith("-" + abbr))
                return cli.Id;
        }

        // 5) Tabella statica di fallback (varianti storiche del nome fornitore).
        foreach (var (sigla, nome) in SiglaToNome)
        {
            if (n.Contains(" " + sigla) || n.EndsWith(sigla) || n.Contains(nome))
            {
                if (cliniche.TryGetValue(nome, out var c)) return c.Id;
            }
        }
        return null;
    }

    private static readonly (string Sigla, string Nome)[] SiglaToNome = new[]
    {
        ("DESIO", "DESIO"), ("VARESE", "VARESE"), ("GIUSSANO", "GIUSSANO"),
        ("CORMANO", "CORMANO"), ("COMO", "COMO"), ("MILANO7", "MILANO7"),
        ("MILANO9", "MILANO9"), ("MILANO6", "MILANO6"), ("MILANO3", "MILANO3"),
        ("SGM", "SGM"), ("BUSTO", "BUSTO A."), ("BOLLATE", "BOLLATE"),
        ("BRUGHERIO", "BRUGHERIO"), ("COMASINA", "COMASINA"), ("CCH", "CCH"),
        ("DES", "DESIO"), ("VAR", "VARESE"), ("GIU", "GIUSSANO"),
        ("COR", "CORMANO"), ("COM", "COMO"), ("MI7", "MILANO7"),
        ("MI9", "MILANO9"), ("MI6", "MILANO6"), ("MI3", "MILANO3"),
        ("BUS", "BUSTO A."), ("BOL", "BOLLATE"), ("BRU", "BRUGHERIO"),
        ("CMS", "COMASINA")
    };

    private static DateTime MeseCompetenza(DateTime dataDoc, string? causale, ImportFatturaRiga? riga = null)
    {
        // Priorità: 1) competenza estratta dal PDF, 2) parsing causale, 3) data fattura
        if (riga?.MeseCompetenza is int m && riga.AnnoCompetenza is int a)
            return new DateTime(a, m, 1);
        var fromCausale = MeseDaCausale(causale);
        if (fromCausale.HasValue) return new DateTime(fromCausale.Value.Year, fromCausale.Value.Month, 1);
        return new DateTime(dataDoc.Year, dataDoc.Month, 1);
    }

    private static readonly Dictionary<string, int> MesiIt = new(StringComparer.OrdinalIgnoreCase)
    {
        { "gennaio", 1 }, { "gen", 1 }, { "genn", 1 },
        { "febbraio", 2 }, { "feb", 2 }, { "febb", 2 },
        { "marzo", 3 }, { "mar", 3 },
        { "aprile", 4 }, { "apr", 4 },
        { "maggio", 5 }, { "mag", 5 },
        { "giugno", 6 }, { "giu", 6 },
        { "luglio", 7 }, { "lug", 7 },
        { "agosto", 8 }, { "ago", 8 },
        { "settembre", 9 }, { "set", 9 }, { "sett", 9 },
        { "ottobre", 10 }, { "ott", 10 },
        { "novembre", 11 }, { "nov", 11 },
        { "dicembre", 12 }, { "dic", 12 }
    };

    /// <summary>Estrae mese/anno dall'oggetto fattura (es. "Compenso ott 2025" → ott 2025).</summary>
    private static DateTime? MeseDaCausale(string? causale)
    {
        if (string.IsNullOrWhiteSpace(causale)) return null;
        var m = Regex.Match(causale, @"(?<mese>gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre|gen(?:n)?|feb(?:b)?|mar|apr|mag|giu|lug|ago|set(?:t)?|ott|nov|dic)\s*[\-/']?\s*(?<anno>\d{2,4})?",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        if (!MesiIt.TryGetValue(m.Groups["mese"].Value.ToLowerInvariant(), out var mese)) return null;
        var annoStr = m.Groups["anno"].Value;
        int anno;
        if (string.IsNullOrEmpty(annoStr)) anno = DateTime.UtcNow.Year;
        else if (annoStr.Length == 2) anno = 2000 + int.Parse(annoStr);
        else anno = int.Parse(annoStr);
        return new DateTime(anno, mese, 1);
    }

    private static DateTime UltimoGiorno(DateTime d) =>
        new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    /// <summary>
    /// I bonifici sono programmati solo al giorno 10 o al 30/31 del mese.
    /// Per default scelgo il giorno più vicino; per Invisalign anticipo "per difetto" (floor).
    /// </summary>
    public static DateTime SnapBonifico(DateTime data, bool anticipa = false)
    {
        var lastDay = DateTime.DaysInMonth(data.Year, data.Month);
        var d10 = new DateTime(data.Year, data.Month, 10);
        var dLast = new DateTime(data.Year, data.Month, lastDay);
        var prevLast = new DateTime(data.Year, data.Month, 1).AddDays(-1);
        var nextMonth10 = new DateTime(data.Year, data.Month, 1).AddMonths(1).AddDays(9);

        if (anticipa)
        {
            // Floor: arrotonda al precedente fra (prevLast | d10 | dLast)
            if (data.Day < 10) return prevLast;
            if (data.Day < lastDay) return d10;
            return dLast;
        }
        // Closest
        var candidates = new[] { prevLast, d10, dLast, nextMonth10 };
        return candidates.OrderBy(c => Math.Abs((c - data).TotalDays)).First();
    }

    private static string NormalizeNome(string s) =>
        Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    /// <summary>
    /// Normalizza un identificativo fiscale (P.IVA o CF) per il match: rimuove spazi,
    /// trattini, prefisso paese ("IT") e mette in maiuscolo. Stringa vuota se non valida.
    /// </summary>
    private static string NormalizeIdFiscale(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var clean = Regex.Replace(s.Trim().ToUpperInvariant(), @"[\s\-\.]", "");
        if (clean.StartsWith("IT") && clean.Length > 11) clean = clean[2..];
        return clean;
    }
}
