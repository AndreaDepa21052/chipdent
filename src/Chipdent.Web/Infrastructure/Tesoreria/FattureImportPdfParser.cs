using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Chipdent.Web.Infrastructure.Tesoreria;

/// <summary>
/// Parser dei PDF "fattura passiva concatenata" prodotti dal portale Confident
/// (rendering AssoSoftware di FatturaPA / SDI).
/// Una fattura per pagina (alcune occupano più pagine consecutive); ogni nuova
/// fattura inizia con la stringa <c>"Cedente/prestatore (fornitore)"</c> in cima
/// alla pagina. I campi vengono estratti via regex tollerante a wrap di testo.
///
/// Per ogni fattura vengono restituiti:
///  - identificativi documento (tipo, numero, data, codice destinatario SDI)
///  - dati anagrafici del fornitore (P.IVA, CF, indirizzo, recapiti, IBAN)
///  - modalità di pagamento + data scadenza
///  - competenza (mese/anno) ricavata da "mese di &lt;mese&gt;" o "Periodo da DD-MM-YYYY a DD-MM-YYYY"
///
/// Gli importi NON vengono estratti dal PDF — il CSV resta la verità per i totali.
/// </summary>
public static class FattureImportPdfParser
{
    /// <summary>Marker che apre ogni nuova fattura nel PDF.</summary>
    public const string HeaderFattura = "Cedente/prestatore (fornitore)";

    public sealed record PdfFattura(
        int PaginaStart,
        int PaginaEnd,
        string? TipoDoc,
        string? DescrTipo,
        string? NumeroDoc,
        DateTime? DataDoc,
        string? CodiceDestinatario,
        string? RagioneSociale,
        bool RagioneSocialeIsPersonaFisica,
        string? PartitaIva,
        string? CodiceFiscale,
        string? Indirizzo,
        string? Cap,
        string? Localita,
        string? Provincia,
        string? Paese,
        string? Telefono,
        string? Email,
        string? Pec,
        string? Iban,
        string? CodiceModalitaPagamento,
        string? DescrModalitaPagamento,
        DateTime? DataScadenza,
        int? MeseCompetenza,
        int? AnnoCompetenza,
        string? PeriodoRiferimentoRaw,
        // ── Cessionario/committente (la NOSTRA Società che ha ricevuto fattura) ──
        string? CessionarioRagioneSociale,
        string? CessionarioPartitaIva,
        string? CessionarioCodiceFiscale,
        string? CessionarioIndirizzo,
        string? CessionarioCap,
        string? CessionarioLocalita,
        string? CessionarioProvincia,
        // ── LOC eventualmente menzionata nelle parti descrittive (causale,
        //     descrizioni di riga). Es. "Royalties MI7", "Canone — DESIO". ──
        string? LocRilevataDaTesto,
        string TestoCompleto);

    public sealed record ParsedPdf(
        string NomeFile,
        long DimensioneByte,
        string ChecksumSha256,
        int Pagine,
        IReadOnlyList<PdfFattura> Fatture);

    public static ParsedPdf Parse(string nomeFile, byte[] contenuto)
    {
        var checksum = Sha256(contenuto);
        var pagine = new List<string>();
        using (var pdf = PdfDocument.Open(contenuto))
        {
            foreach (var page in pdf.GetPages())
            {
                // ContentOrderTextExtractor: layout analysis più stabile su PDF
                // AssoSoftware del semplice page.Text. Fallback al testo grezzo se
                // l'analisi fallisce su pagine particolari.
                string txt;
                try { txt = ContentOrderTextExtractor.GetText(page) ?? string.Empty; }
                catch { txt = page.Text ?? string.Empty; }
                pagine.Add(txt);
            }
        }

        // Split: una nuova fattura inizia su una pagina che parte con HeaderFattura
        var fatture = new List<PdfFattura>();
        int? startPage = null;
        var buffer = new List<string>();
        for (int i = 0; i < pagine.Count; i++)
        {
            var t = pagine[i];
            var trimmed = (t ?? string.Empty).TrimStart();
            if (trimmed.StartsWith(HeaderFattura, StringComparison.OrdinalIgnoreCase))
            {
                if (startPage.HasValue)
                {
                    fatture.Add(EstraiCampi(startPage.Value, i, buffer));
                }
                startPage = i + 1; // pagina 1-based
                buffer = new List<string> { t };
            }
            else if (startPage.HasValue)
            {
                buffer.Add(t);
            }
            // Se non abbiamo ancora visto un header, ignoriamo (es. copertine)
        }
        if (startPage.HasValue)
        {
            fatture.Add(EstraiCampi(startPage.Value, pagine.Count, buffer));
        }

        return new ParsedPdf(nomeFile, contenuto.LongLength, checksum, pagine.Count, fatture);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Estrazione campi da una singola fattura (testo concatenato di N pagine)
    // ─────────────────────────────────────────────────────────────────────

    private static PdfFattura EstraiCampi(int pStart, int pEndExclusive, List<string> pagine)
    {
        var text = string.Join("\n", pagine);

        // ── Anagrafica fornitore (cedente/prestatore) ────────────────────
        // Il blocco fornitore è dall'inizio fino a "Cessionario/committente".
        // Il blocco cessionario è da "Cessionario/committente" fino a
        // "Tipologia documento" (header della sezione documento).
        var cessIdx = text.IndexOf("Cessionario/committente", StringComparison.OrdinalIgnoreCase);
        var tipoDocIdx = text.IndexOf("Tipologia documento", StringComparison.OrdinalIgnoreCase);
        var blockForn = cessIdx > 0 ? text.Substring(0, cessIdx) : text;
        string blockCess = string.Empty;
        if (cessIdx > 0)
        {
            var endCess = tipoDocIdx > cessIdx ? tipoDocIdx : Math.Min(text.Length, cessIdx + 2000);
            blockCess = text.Substring(cessIdx, endCess - cessIdx);
        }

        var piva = ExtractField(blockForn, @"Identificativo fiscale ai fini IVA:\s*(\S+)");
        var cf   = ExtractField(blockForn, @"Codice fiscale:\s*(\S+)");

        // "Denominazione" per società, "Cognome nome" per persone fisiche.
        var denom = ExtractField(blockForn, @"Denominazione:\s*(.+?)\s*\r?\n");
        var isPF = false;
        if (string.IsNullOrEmpty(denom))
        {
            denom = ExtractField(blockForn, @"Cognome nome:\s*(.+?)\s*\r?\n");
            isPF = !string.IsNullOrEmpty(denom);
        }

        var indir = ExtractField(blockForn, @"Indirizzo:\s*(.+?)\s*\r?\n");
        var comuneM = Regex.Match(blockForn, @"Comune:\s*(.+?)\s+Provincia:\s*([A-Z]{2})");
        string? localita = comuneM.Success ? comuneM.Groups[1].Value.Trim() : null;
        string? prov     = comuneM.Success ? comuneM.Groups[2].Value.Trim() : null;
        var capM = Regex.Match(blockForn, @"Cap:\s*(\d{5})\s+Nazione:\s*([A-Z]{2})");
        string? cap   = capM.Success ? capM.Groups[1].Value : null;
        string? paese = capM.Success ? capM.Groups[2].Value : null;

        var telefono = ExtractField(blockForn, @"Telefono:\s*(.+?)\s*\r?\n");
        var email    = ExtractField(blockForn, @"Email:\s*(.+?)\s*\r?\n");
        var pec      = ExtractField(blockForn, @"PEC:\s*(.+?)\s*\r?\n",
                                    RegexOptions.IgnoreCase);

        // ── Header documento ─────────────────────────────────────────────
        // "Tipologia documento Art. 73 Numero documento Data documento Codice destinatario"
        // riga successiva: "TDxx <descrizione tipo> <numero> dd-mm-yyyy <SDI>"
        string? tipoDoc = null, descrTipo = null, numDoc = null, sdi = null;
        DateTime? dataDoc = null;

        var hdrM = Regex.Match(text,
            @"Tipologia documento[\s\S]*?Codice destinatario\s*\r?\n([\s\S]+?)(?:\r?\nCausale\b|\r?\nCod\. articolo\b|\r?\nDescrizione\b|\r?\nC\b)");
        if (hdrM.Success)
        {
            var raw = hdrM.Groups[1].Value.Trim().Replace("\r", "");
            // Compatta multi-riga in una singola
            var line = Regex.Replace(raw, @"\s*\n\s*", " ").Trim();
            // Estrai: TDxx ... numero ... dd-mm-yyyy ... SDI
            var m = Regex.Match(line, @"^(TD\d+)\s+(.+?)\s+(\S+)\s+(\d{2}-\d{2}-\d{4})\s+(\S+)\s*$");
            if (m.Success)
            {
                tipoDoc   = m.Groups[1].Value;
                descrTipo = m.Groups[2].Value;
                numDoc    = m.Groups[3].Value;
                if (DateTime.TryParseExact(m.Groups[4].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd))
                    dataDoc = DateTime.SpecifyKind(dd, DateTimeKind.Utc);
                sdi       = m.Groups[5].Value;
            }
        }

        // ── IBAN ─────────────────────────────────────────────────────────
        var iban = ExtractIbanItaliano(text);

        // ── Modalità pagamento ───────────────────────────────────────────
        string? mpCode = null, mpDescr = null;
        var mpM = Regex.Match(text, @"\b(MP\d{2})\b\s+(\w+)");
        if (mpM.Success)
        {
            mpCode  = mpM.Groups[1].Value;
            mpDescr = mpM.Groups[2].Value;
        }

        // ── Data scadenza ────────────────────────────────────────────────
        DateTime? scadenza = null;
        var scM = Regex.Match(text, @"Data scadenza\s+(\d{2}-\d{2}-\d{4})");
        if (scM.Success && DateTime.TryParseExact(scM.Groups[1].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sc))
            scadenza = DateTime.SpecifyKind(sc, DateTimeKind.Utc);

        // ── Competenza ───────────────────────────────────────────────────
        // 1) "Periodo da DD-MM-YYYY a DD-MM-YYYY"
        // 2) "mese di <nome mese> [anno]"
        // 3) "<nome mese> <anno>"
        int? meseComp = null, annoComp = null;
        string? periodoRaw = null;
        var periodoM = Regex.Match(text, @"Periodo da\s+(\d{2}-\d{2}-\d{4})\s+a\s+(\d{2}-\d{2}-\d{4})");
        if (periodoM.Success)
        {
            periodoRaw = periodoM.Value;
            if (DateTime.TryParseExact(periodoM.Groups[1].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var pd))
            {
                meseComp = pd.Month;
                annoComp = pd.Year;
            }
        }
        if (meseComp == null)
        {
            var mcM = Regex.Match(text, @"mese di\s+(gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre)(?:\s+(\d{4}))?", RegexOptions.IgnoreCase);
            if (mcM.Success)
            {
                meseComp = MeseFromNome(mcM.Groups[1].Value);
                if (mcM.Groups[2].Success && int.TryParse(mcM.Groups[2].Value, out var anno))
                    annoComp = anno;
                else if (dataDoc.HasValue)
                    annoComp = dataDoc.Value.Year; // se l'anno non è esplicito, prendi quello della fattura
            }
        }
        if (meseComp == null)
        {
            // pattern "Royalties mese dicembre 2025" (senza "di")
            var mc2 = Regex.Match(text, @"\bmese\s+(gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre)\s+(\d{4})", RegexOptions.IgnoreCase);
            if (mc2.Success)
            {
                meseComp = MeseFromNome(mc2.Groups[1].Value);
                annoComp = int.Parse(mc2.Groups[2].Value);
            }
        }

        // ── Cessionario/committente (chi riceve la fattura — la NOSTRA Società) ──
        string? cessPiva = null, cessCf = null, cessDenom = null,
                cessIndir = null, cessCap = null, cessLocalita = null, cessProv = null;
        if (!string.IsNullOrEmpty(blockCess))
        {
            cessPiva = ExtractField(blockCess, @"Identificativo fiscale ai fini IVA:\s*(\S+)");
            cessCf   = ExtractField(blockCess, @"Codice fiscale:\s*(\S+)");
            cessDenom = ExtractField(blockCess, @"Denominazione:\s*(.+?)\s*\r?\n");
            if (string.IsNullOrEmpty(cessDenom))
                cessDenom = ExtractField(blockCess, @"Cognome nome:\s*(.+?)\s*\r?\n");
            cessIndir = ExtractField(blockCess, @"Indirizzo:\s*(.+?)\s*\r?\n");
            var cComM = Regex.Match(blockCess, @"Comune:\s*(.+?)\s+Provincia:\s*([A-Z]{2})");
            if (cComM.Success)
            {
                cessLocalita = cComM.Groups[1].Value.Trim();
                cessProv = cComM.Groups[2].Value.Trim();
            }
            var cCapM = Regex.Match(blockCess, @"Cap:\s*(\d{5})");
            if (cCapM.Success) cessCap = cCapM.Groups[1].Value;
        }

        // ── LOC nel testo descrittivo ────────────────────────────────────
        // Le fatture B2B verso il gruppo riportano spesso la sigla della sede
        // di destinazione nelle righe descrittive ("Royalties MI7 dic 25",
        // "Canone affitto STUDIO DENTISTICO DESIO", "Manutenzione — CCH").
        // Isoliamo la sezione "descrittiva" (dopo Causale / Descrizione) per
        // non confondere sigle che compaiono in indirizzi del cessionario.
        string? locTesto = RilevaLocDaTesto(text);

        return new PdfFattura(
            PaginaStart: pStart,
            PaginaEnd: pEndExclusive,
            TipoDoc: tipoDoc,
            DescrTipo: descrTipo,
            NumeroDoc: numDoc,
            DataDoc: dataDoc,
            CodiceDestinatario: sdi,
            RagioneSociale: denom,
            RagioneSocialeIsPersonaFisica: isPF,
            PartitaIva: piva,
            CodiceFiscale: cf,
            Indirizzo: indir,
            Cap: cap,
            Localita: localita,
            Provincia: prov,
            Paese: paese,
            Telefono: telefono,
            Email: email,
            Pec: pec,
            Iban: iban,
            CodiceModalitaPagamento: mpCode,
            DescrModalitaPagamento: mpDescr,
            DataScadenza: scadenza,
            MeseCompetenza: meseComp,
            AnnoCompetenza: annoComp,
            PeriodoRiferimentoRaw: periodoRaw,
            CessionarioRagioneSociale: cessDenom,
            CessionarioPartitaIva: cessPiva,
            CessionarioCodiceFiscale: cessCf,
            CessionarioIndirizzo: cessIndir,
            CessionarioCap: cessCap,
            CessionarioLocalita: cessLocalita,
            CessionarioProvincia: cessProv,
            LocRilevataDaTesto: locTesto,
            TestoCompleto: text);
    }

    /// <summary>
    /// Sigle LOC del network Confident usate nelle descrizioni di fattura.
    /// Il match è "word-boundary": evita falsi positivi su sottostringhe
    /// (es. "MI7" non matcha "MI78", "COMO" non matcha "COMODA"). Vince la
    /// prima occorrenza nella sezione descrittiva. Se nessuna sigla matcha
    /// si ritorna null e ScadenziarioGenerator userà gli altri segnali.
    /// </summary>
    private static readonly string[] LocSigleNote = new[]
    {
        "CCH", "MI3", "MI6", "MI7", "MI9",
        "DESIO", "VARESE", "GIUSSANO", "CORMANO", "COMO",
        "BUSTO", "BOLLATE", "BRUGHERIO", "COMASINA", "SGM",
        "MILANO3", "MILANO6", "MILANO7", "MILANO9"
    };

    private static string? RilevaLocDaTesto(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // Prendiamo la "parte bassa" della fattura: descrizioni, causale,
        // periodo di riferimento — escludiamo l'header anagrafico per evitare
        // confusione con indirizzi di sede legale di cedente/cessionario.
        var startDescr = -1;
        foreach (var anchor in new[] { "Cod. articolo", "Descrizione", "Causale" })
        {
            var idx = text.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (startDescr < 0 || idx < startDescr)) startDescr = idx;
        }
        var slice = startDescr > 0 ? text.Substring(startDescr) : text;
        var upper = slice.ToUpperInvariant();

        foreach (var sigla in LocSigleNote)
        {
            // \b non funziona bene con cifre adiacenti; usiamo un look-around manuale.
            var pattern = $@"(?<![A-Z0-9]){Regex.Escape(sigla)}(?![A-Z0-9])";
            if (Regex.IsMatch(upper, pattern)) return sigla;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static string? ExtractField(string text, string pattern, RegexOptions opts = RegexOptions.None)
    {
        var m = Regex.Match(text, pattern, opts);
        if (!m.Success) return null;
        var v = m.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>
    /// L'IBAN nel PDF rendering può essere spezzato su 2 righe (es. "IT68I050343352200000000\n3711").
    /// Compatto i caratteri dopo "IBAN " e taglio a 27 caratteri se inizia con IT.
    /// </summary>
    private static string? ExtractIbanItaliano(string text)
    {
        var idx = text.IndexOf("IBAN", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var slice = text.Substring(idx + 4, Math.Min(160, text.Length - idx - 4));
        var compact = Regex.Replace(slice, @"\s", "");
        var m = Regex.Match(compact, @"^([A-Z]{2}\d{2}[A-Z0-9]{1,30})");
        if (!m.Success) return null;
        var iban = m.Groups[1].Value;
        if (iban.StartsWith("IT", StringComparison.OrdinalIgnoreCase) && iban.Length >= 27)
            return iban.Substring(0, 27).ToUpperInvariant();
        return iban.ToUpperInvariant();
    }

    private static readonly Dictionary<string, int> MesiIt = new(StringComparer.OrdinalIgnoreCase)
    {
        { "gennaio", 1 }, { "febbraio", 2 }, { "marzo", 3 }, { "aprile", 4 },
        { "maggio", 5 }, { "giugno", 6 }, { "luglio", 7 }, { "agosto", 8 },
        { "settembre", 9 }, { "ottobre", 10 }, { "novembre", 11 }, { "dicembre", 12 }
    };

    private static int? MeseFromNome(string nome) =>
        MesiIt.TryGetValue(nome ?? "", out var n) ? n : null;

    private static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
