using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Migrazione one-shot: rimuove il fornitore fantasma con ragione sociale
/// "FORNITORI" (l'intestazione della colonna A del file
/// "FORNITORI Confident.xlsx" che a un certo punto è finita nel DB come
/// se fosse una riga dati).
///
/// Match case-insensitive, dopo Trim, contro le tre intestazioni note
/// dei fogli del file ("FORNITORI", "MAIL", "MAIL2"). In pratica solo
/// "FORNITORI" si è mai materializzato come anagrafica.
///
/// Soft-delete (IsDeleted=true + Stato=Dismesso): le fatture/scadenze
/// storiche eventualmente collegate continuano a referenziare correttamente.
/// </summary>
internal static class PurgeHeaderFornitoreSeeder
{
    private static readonly string[] NomiHeaderInvalidi = { "FORNITORI", "MAIL", "MAIL2" };

    public static async Task SeedAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        var candidati = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id && !f.IsDeleted)
            .ToListAsync(ct);

        var daRimuovere = candidati
            .Where(f => NomiHeaderInvalidi.Any(h =>
                string.Equals((f.RagioneSociale ?? "").Trim(), h, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (daRimuovere.Count == 0) return;

        var ids = daRimuovere.Select(f => f.Id).ToList();
        await ctx.Fornitori.UpdateManyAsync(
            f => ids.Contains(f.Id),
            Builders<Fornitore>.Update
                .Set(f => f.IsDeleted, true)
                .Set(f => f.Stato, StatoFornitore.Dismesso)
                .Set(f => f.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);

        logger.LogInformation(
            "PurgeHeaderFornitore: soft-deleted {Count} fornitor{Plural} fantasma (intestazioni colonna importate per errore: {Nomi})",
            daRimuovere.Count,
            daRimuovere.Count == 1 ? "e" : "i",
            string.Join(", ", daRimuovere.Select(f => $"'{f.RagioneSociale}' (codice {f.Codice ?? "—"})")));
    }
}
