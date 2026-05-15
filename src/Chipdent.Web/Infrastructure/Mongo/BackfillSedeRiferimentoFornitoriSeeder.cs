using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Backfill one-shot: per ogni fornitore senza <see cref="Fornitore.SedeRiferimentoId"/>
/// imposta la sede DESIO come default richiesto dal cliente. Resta possibile cambiarla
/// dalla griglia Fornitori o dalla scheda.
/// </summary>
internal static class BackfillSedeRiferimentoFornitoriSeeder
{
    public static async Task SeedAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        var desio = await ctx.Cliniche
            .Find(c => c.TenantId == tenant.Id && c.Nome == "DESIO")
            .FirstOrDefaultAsync(ct);
        if (desio is null) return;

        var res = await ctx.Fornitori.UpdateManyAsync(
            f => f.TenantId == tenant.Id
                && !f.IsDeleted
                && (f.SedeRiferimentoId == null || f.SedeRiferimentoId == ""),
            Builders<Fornitore>.Update
                .Set(x => x.SedeRiferimentoId, desio.Id)
                .Set(x => x.UpdatedAt, DateTime.UtcNow),
            cancellationToken: ct);

        if (res.ModifiedCount > 0)
        {
            logger.LogInformation(
                "BackfillSedeRiferimentoFornitori: assegnata sede DESIO a {Count} fornitori",
                res.ModifiedCount);
        }
    }
}
