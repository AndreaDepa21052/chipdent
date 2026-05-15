using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Popola l'anagrafica fornitori leggendo dai dati estratti dalle fatture passive
/// PDF (vedi <see cref="FattureFornitoriAnagraficaData"/>, rigenerato da
/// <c>tools/import-fatture-anagrafica.py</c>).
///
/// Comportamento idempotente: gira SOLO se l'anagrafica fornitori del tenant è
/// completamente vuota. Quindi:
///   - subito dopo il primo run di <see cref="WipeAnagraficaSeeder"/> popola i
///     ~57 cedenti estratti dal PDF;
///   - se l'utente aggiunge/modifica fornitori dal portale, al deploy successivo
///     questo seeder è un no-op (l'anagrafica non è più vuota).
///
/// Campi popolati per ogni fornitore:
///   - Codice progressivo F0001, F0002, …
///   - RagioneSociale, RagioneSocialePagamento (= RagioneSociale)
///   - PartitaIva, CodiceFiscale
///   - Indirizzo, CodicePostale, Localita, Provincia
///   - Telefono, EmailContatto, Pec
///   - Iban (quando univoco; in caso di conflitto multi-IBAN il tool genera
///     null e segnala il conflitto come commento nel file di dati)
///   - CategoriaDefault default = AltreSpeseFisse, Stato = Attivo,
///     TerminiPagamentoGiorni = 30, BasePagamento = DataFattura.
/// </summary>
internal static class FattureFornitoriAnagraficaSeeder
{
    public static async Task SeedAsync(
        MongoContext ctx,
        Tenant tenant,
        ILogger logger,
        CancellationToken ct)
    {
        if (FattureFornitoriAnagraficaData.Righe.Count == 0) return;

        var existing = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id)
            .AnyAsync(ct);
        if (existing) return;

        var nuovi = new List<Fornitore>(FattureFornitoriAnagraficaData.Righe.Count);
        int seq = 0;
        foreach (var r in FattureFornitoriAnagraficaData.Righe)
        {
            seq++;
            nuovi.Add(new Fornitore
            {
                TenantId = tenant.Id,
                Codice = $"F{seq:D4}",
                RagioneSociale = r.RagioneSociale,
                RagioneSocialePagamento = r.RagioneSociale,
                PartitaIva = r.PartitaIva,
                CodiceFiscale = r.CodiceFiscale,
                Indirizzo = r.Indirizzo,
                CodicePostale = r.CodicePostale,
                Localita = r.Localita,
                Provincia = r.Provincia,
                Telefono = r.Telefono,
                EmailContatto = r.Email,
                Pec = r.Pec,
                Iban = r.Iban,
                CategoriaDefault = CategoriaSpesa.AltreSpeseFisse,
                Stato = StatoFornitore.Attivo,
                TerminiPagamentoGiorni = 30,
                BasePagamento = BasePagamento.DataFattura,
                Note = r.IsPersonaFisica
                    ? "Importato da fatture passive PDF — persona fisica"
                    : "Importato da fatture passive PDF",
            });
        }

        await ctx.Fornitori.InsertManyAsync(nuovi, cancellationToken: ct);

        var conIban = nuovi.Count(f => !string.IsNullOrWhiteSpace(f.Iban));
        var pf = nuovi.Count(f => f.Note != null && f.Note.Contains("persona fisica"));
        logger.LogInformation(
            "FattureFornitoriAnagrafica: inseriti {N} fornitori dal PDF (PF: {Pf}, società: {Sc}; con IBAN: {Iban}).",
            nuovi.Count, pf, nuovi.Count - pf, conIban);
    }
}
