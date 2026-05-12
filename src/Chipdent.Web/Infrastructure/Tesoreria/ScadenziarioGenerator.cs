using System.Globalization;
using System.Text.RegularExpressions;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Sepa;

namespace Chipdent.Web.Infrastructure.Tesoreria;

/// <summary>
/// Trasforma le righe importate (CCH/Ident) in Fatture + Scadenze applicando le regole
/// del file <c>Regole scadenzario.xlsx</c>: classificazione fornitore, calcolo scadenze
/// per tipologia (medici/laboratori/Invisalign/generici), gestione ritenute, note di
/// credito, carta di credito, snap dei bonifici al 10 / fine mese, e una lista di
/// <see cref="AlertScadenziario"/> per i casi che richiedono revisione manuale.
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
        /// <summary>Userid che firma le fatture generate (creator/approver).</summary>
        public string? UserId { get; init; }
    }

    public sealed class Output
    {
        public List<FatturaFornitore> Fatture { get; } = new();
        public List<ScadenzaPagamento> Scadenze { get; } = new();
        public List<Fornitore> FornitoriNuovi { get; } = new();
        public List<AlertScadenziario> Alerts { get; } = new();
        public int RigheElaborate { get; set; }
        public int RigheSaltate { get; set; }
    }

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
        var fornitoriByKey = new Dictionary<string, Fornitore>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in input.Fornitori) fornitoriByKey[NormalizeNome(f.RagioneSociale)] = f;

        var medici = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in input.Dottori) medici.Add(NormalizeNome($"{d.Cognome} {d.Nome}"));

        var cliniche = input.Cliniche.ToDictionary(c => c.Nome ?? "", c => c, StringComparer.OrdinalIgnoreCase);
        var cch = input.Cliniche.FirstOrDefault(c => c.IsHolding) ??
                  input.Cliniche.FirstOrDefault(c => string.Equals(c.Nome, "CCH", StringComparison.OrdinalIgnoreCase));

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
            if (!fornitoriByKey.TryGetValue(key, out var fornitore))
            {
                fornitore = new Fornitore
                {
                    TenantId = input.TenantId,
                    RagioneSociale = nomeForn,
                    CategoriaDefault = MappaCategoriaDefault(tipo),
                    TerminiPagamentoGiorni = TipiATerminiGiorni(tipo),
                    BasePagamento = TipiABasePagamento(tipo),
                    Stato = StatoFornitore.Attivo
                };
                fornitoriByKey[key] = fornitore;
                output.FornitoriNuovi.Add(fornitore);
                output.Alerts.Add(new AlertScadenziario(AlertSeverita.Info, "Catalogazione fornitore",
                    $"Creato nuovo fornitore «{nomeForn}» (tipo: {tipo}). Verifica IBAN e termini.",
                    nomeForn, riga.Numero, dataDoc, riga.NumeroRiga));
            }

            // ── Clinica destinataria (LOC) ───────────────────────────────
            var clinicaId = ResolveLoc(riga, nomeForn, cch, cliniche);
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

    private static string? ResolveLoc(
        ImportFatturaRiga riga, string nomeForn, Clinica? cch, Dictionary<string, Clinica> cliniche)
    {
        // 1) Sezione CCH → holding
        if (string.Equals(riga.Sezione, "CCH", StringComparison.OrdinalIgnoreCase) && cch != null)
            return cch.Id;

        // 2) Pattern nel nome fornitore (es "VODAFONE DESIO", "AFFITTO VAR")
        var n = nomeForn.ToUpperInvariant();
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
}
