using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Infrastructure.Sepa;

/// <summary>
/// Helper di calcolo dei termini di pagamento e selezione dell'ordinante.
/// Tenuti qui per essere riusati tra TesoreriaController e FornitoriPortalController.
/// </summary>
public static class PagamentiHelper
{
    /// <summary>
    /// Calcola la data di scadenza attesa secondo i termini commerciali del fornitore.
    /// Usata per evidenziare mismatch nella griglia scadenziario.
    /// </summary>
    public static DateTime CalcolaScadenzaAttesa(DateTime dataFattura, int giorni, BasePagamento basePagamento)
    {
        var giorniSafe = Math.Max(0, giorni);
        return basePagamento switch
        {
            BasePagamento.DataFattura => dataFattura.Date.AddDays(giorniSafe),
            BasePagamento.FineMeseFattura => UltimoGiornoMese(dataFattura).AddDays(giorniSafe),
            BasePagamento.FineMeseSuccessivo => UltimoGiornoMese(dataFattura.AddMonths(1)).AddDays(giorniSafe),
            _ => dataFattura.Date.AddDays(giorniSafe)
        };
    }

    private static DateTime UltimoGiornoMese(DateTime d) =>
        new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    /// <summary>
    /// Ritorna i dati ordinante per la clinica indicata, con fallback al tenant.
    /// </summary>
    public static OrdinanteSnapshot Risolvi(Clinica? clinica, Tenant tenant)
    {
        var iban = !string.IsNullOrWhiteSpace(clinica?.IbanOrdinante)
            ? clinica!.IbanOrdinante!
            : tenant.PagatoreIban ?? string.Empty;

        var bic = !string.IsNullOrWhiteSpace(clinica?.IbanOrdinante)
            ? clinica!.BicOrdinante                         // se la clinica ha proprio IBAN, usa il suo BIC
            : tenant.PagatoreBic;

        var ragione = !string.IsNullOrWhiteSpace(clinica?.IbanOrdinante)
            ? (clinica!.RagioneSocialeOrdinante ?? clinica.Nome ?? tenant.PagatoreRagioneSociale ?? tenant.DisplayName)
            : (tenant.PagatoreRagioneSociale ?? tenant.RagioneSociale ?? tenant.DisplayName);

        return new OrdinanteSnapshot(iban, bic, ragione, tenant.PagatoreCodiceFiscale);
    }
}

/// <summary>
/// Snapshot dei dati ordinante usati per una scadenza/distinta.
/// </summary>
public record OrdinanteSnapshot(string Iban, string? Bic, string RagioneSociale, string? CodiceFiscale);
