using System.Text;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Domain.Common;

/// <summary>
/// Calcola completezza anagrafica fornitore e disponibilità dati per
/// incasso/pagamento via SEPA. Usato dalla lista fornitori (badge + percentuale)
/// e dalla griglia scadenze (warning quando manca l'IBAN del beneficiario).
/// </summary>
public static class FornitoreCompletezza
{
    /// <summary>Esito della valutazione di completezza per un fornitore.</summary>
    public sealed record Esito(
        int Percentuale,
        bool SepaPronto,
        IReadOnlyList<string> CampiMancantiSepa,
        IReadOnlyList<string> CampiMancantiAltri)
    {
        /// <summary>Lista compatta dei campi mancanti — prima SEPA, poi gli altri.</summary>
        public IReadOnlyList<string> TuttiCampiMancanti =>
            CampiMancantiSepa.Concat(CampiMancantiAltri).ToList();
    }

    /// <summary>
    /// Campi obbligatori per generare una distinta SEPA verso questo fornitore.
    /// Vengono pesati di più nella percentuale di completamento.
    /// </summary>
    private static readonly (string Etichetta, Func<Fornitore, bool> Presente)[] CampiSepa =
    {
        ("IBAN valido",       f => !string.IsNullOrWhiteSpace(f.Iban) && IsValidIban(NormalizeIban(f.Iban!))),
        ("Ragione sociale",   f => !string.IsNullOrWhiteSpace(f.RagioneSociale)),
        ("P.IVA o C.F.",      f => !string.IsNullOrWhiteSpace(f.PartitaIva) || !string.IsNullOrWhiteSpace(f.CodiceFiscale)),
        ("Indirizzo",         f => !string.IsNullOrWhiteSpace(f.Indirizzo)),
        ("Località",          f => !string.IsNullOrWhiteSpace(f.Localita)),
        ("CAP",               f => !string.IsNullOrWhiteSpace(f.CodicePostale)),
    };

    /// <summary>Campi anagrafici utili ma non bloccanti per SEPA.</summary>
    private static readonly (string Etichetta, Func<Fornitore, bool> Presente)[] CampiAltri =
    {
        ("Codice SDI o PEC",  f => !string.IsNullOrWhiteSpace(f.CodiceSdi) || !string.IsNullOrWhiteSpace(f.Pec)),
        ("Email contatto",    f => !string.IsNullOrWhiteSpace(f.EmailContatto)),
        ("Telefono",          f => !string.IsNullOrWhiteSpace(f.Telefono)),
        ("Provincia",         f => !string.IsNullOrWhiteSpace(f.Provincia)),
    };

    public static Esito Valuta(Fornitore f)
    {
        var mancantiSepa = CampiSepa.Where(c => !c.Presente(f)).Select(c => c.Etichetta).ToList();
        var mancantiAltri = CampiAltri.Where(c => !c.Presente(f)).Select(c => c.Etichetta).ToList();

        // I campi SEPA pesano il doppio: sono bloccanti per l'operatività di tesoreria.
        var totaleCheck = CampiSepa.Length * 2 + CampiAltri.Length;
        var puntiOk = (CampiSepa.Length - mancantiSepa.Count) * 2 + (CampiAltri.Length - mancantiAltri.Count);
        var perc = (int)Math.Round((double)puntiOk * 100 / totaleCheck);

        return new Esito(
            Percentuale: Math.Clamp(perc, 0, 100),
            SepaPronto: mancantiSepa.Count == 0,
            CampiMancantiSepa: mancantiSepa,
            CampiMancantiAltri: mancantiAltri);
    }

    /// <summary>True se sull'anagrafica fornitore manca un IBAN valido per generare la distinta SEPA.</summary>
    public static bool MancaIbanSepa(Fornitore? f)
    {
        if (f is null) return true;
        if (string.IsNullOrWhiteSpace(f.Iban)) return true;
        return !IsValidIban(NormalizeIban(f.Iban));
    }

    public static string NormalizeIban(string iban) =>
        new string((iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Validazione IBAN: lunghezza per paese + check digit MOD 97 (ISO 13616).</summary>
    public static bool IsValidIban(string iban)
    {
        if (string.IsNullOrEmpty(iban) || iban.Length < 15 || iban.Length > 34) return false;
        if (!iban.All(char.IsLetterOrDigit)) return false;
        if (iban.StartsWith("IT") && iban.Length != 27) return false;

        var rearranged = iban[4..] + iban[..4];
        var numeric = new StringBuilder();
        foreach (var ch in rearranged)
        {
            numeric.Append(char.IsDigit(ch) ? ch.ToString() : (ch - 'A' + 10).ToString());
        }
        var s = numeric.ToString();
        var remainder = 0;
        foreach (var ch in s)
        {
            remainder = (remainder * 10 + (ch - '0')) % 97;
        }
        return remainder == 1;
    }
}
