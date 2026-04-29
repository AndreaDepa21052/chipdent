using System.Globalization;
using System.Text.RegularExpressions;

namespace Chipdent.Web.Infrastructure.Insights;

/// <summary>
/// Estrattore "OCR-like" basato su regex sul testo del documento (incollato dall'utente o estratto da
/// un PDF parser). Riconosce date italiane comuni (gg/mm/aaaa, gg-mm-aa, "31 dicembre 2026") e prova
/// ad associarle a parole chiave di contesto ("scadenza", "valida fino al", "rinnovo", "idoneità").
///
/// In produzione lo step naturale è collegare Azure Document Intelligence: l'interfaccia IOcrExtractor
/// resta stabile, cambia solo l'implementazione. Per ora questo è sufficiente per:
///  - quando il Backoffice carica una visita medica, suggerire data idoneità rilevata
///  - quando si carica DURC, agibilità, polizze, certificazioni dottori → date di scadenza pre-popolate
/// </summary>
public class OcrScadenzeExtractor
{
    private static readonly string[] MesiItaliani =
    {
        "gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno",
        "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"
    };

    private static readonly string[] KeywordsScadenza =
    {
        "scadenza", "scade", "valida fino", "valido fino", "rinnovo entro", "idoneità fino",
        "data fine", "data scadenza", "scadrà", "expires", "expiry"
    };

    private static readonly Regex DateNumeric = new(
        @"\b(\d{1,2})[\/\-\.](\d{1,2})[\/\-\.](\d{2}|\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DateLetter = new(
        @"\b(\d{1,2})\s+(gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre)\s+(\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public OcrEstrazione Estrai(string testo)
    {
        if (string.IsNullOrWhiteSpace(testo)) return new OcrEstrazione(Array.Empty<DataRilevata>(), null);

        var trovate = new List<DataRilevata>();

        foreach (Match m in DateNumeric.Matches(testo))
        {
            if (TryParseNumeric(m, out var dt))
            {
                var contesto = EstraiContesto(testo, m.Index, m.Length);
                trovate.Add(new DataRilevata(dt, contesto, ContieneScadenzaKeyword(contesto)));
            }
        }
        foreach (Match m in DateLetter.Matches(testo))
        {
            var giorno = int.Parse(m.Groups[1].Value);
            var mese = Array.IndexOf(MesiItaliani, m.Groups[2].Value.ToLowerInvariant()) + 1;
            var anno = int.Parse(m.Groups[3].Value);
            if (TryDate(anno, mese, giorno, out var dt))
            {
                var contesto = EstraiContesto(testo, m.Index, m.Length);
                trovate.Add(new DataRilevata(dt, contesto, ContieneScadenzaKeyword(contesto)));
            }
        }

        // La "data scadenza candidata" è la più tardiva fra quelle col contesto di scadenza, oppure la più tardiva tout-court.
        var conScad = trovate.Where(t => t.SuggerisceScadenza).ToList();
        var candidata = (conScad.Count > 0 ? conScad : trovate)
            .OrderByDescending(t => t.Data).FirstOrDefault();

        return new OcrEstrazione(trovate.OrderBy(t => t.Data).ToList(), candidata?.Data);
    }

    private static bool TryParseNumeric(Match m, out DateTime dt)
    {
        var g = int.Parse(m.Groups[1].Value);
        var mm = int.Parse(m.Groups[2].Value);
        var a = int.Parse(m.Groups[3].Value);
        if (a < 100) a += 2000;
        return TryDate(a, mm, g, out dt);
    }

    private static bool TryDate(int year, int month, int day, out DateTime dt)
    {
        try { dt = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc); return true; }
        catch { dt = default; return false; }
    }

    private static string EstraiContesto(string testo, int index, int len)
    {
        var start = Math.Max(0, index - 40);
        var end = Math.Min(testo.Length, index + len + 30);
        return testo[start..end].Replace('\n', ' ').Trim();
    }

    private static bool ContieneScadenzaKeyword(string contesto)
    {
        var lower = contesto.ToLowerInvariant();
        return KeywordsScadenza.Any(k => lower.Contains(k));
    }
}

public record OcrEstrazione(IReadOnlyList<DataRilevata> Date, DateTime? ScadenzaCandidata);

public record DataRilevata(DateTime Data, string Contesto, bool SuggerisceScadenza);
