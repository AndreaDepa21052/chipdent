using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Backfill difensivo: assicura che TUTTE le <see cref="ScadenzaPagamento"/> del
/// tenant abbiano un <see cref="ScadenzaPagamento.Codice"/> valorizzato.
///
/// Il codice "umano" della scadenza ha formato <c>SC-YYYYMM-NNNN</c> dove
/// <c>YYYYMM</c> è l'anno+mese della data scadenza e <c>NNNN</c> è un
/// progressivo nel mese.
///
/// Strategia:
///   1. Carica TUTTE le scadenze del tenant (anche quelle già codificate)
///      per ricostruire la sequenza per mese (così non riassegniamo numeri
///      già in uso e non creiamo collisioni).
///   2. Per ogni scadenza senza codice, ordinate per (mese, CreatedAt, Id),
///      assegna il prossimo progressivo libero nel mese.
///   3. Aggiorna solo le scadenze toccate (le altre restano intatte).
/// </summary>
internal static class BackfillCodiceScadenzeSeeder
{
    public static async Task SeedAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        var tutte = await ctx.ScadenzePagamento
            .Find(s => s.TenantId == tenant.Id)
            .ToListAsync(ct);
        var senza = tutte.Where(s => string.IsNullOrEmpty(s.Codice)).ToList();
        if (senza.Count == 0) return;

        // Ricostruisco il max progressivo per mese leggendo i codici esistenti.
        var maxByMese = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in tutte)
        {
            if (string.IsNullOrEmpty(s.Codice)) continue;
            // Formato atteso: SC-YYYYMM-NNNN
            var parts = s.Codice.Split('-');
            if (parts.Length != 3 || parts[0] != "SC") continue;
            if (!int.TryParse(parts[2], out var n)) continue;
            if (!maxByMese.TryGetValue(parts[1], out var cur) || n > cur)
                maxByMese[parts[1]] = n;
        }

        var ordinate = senza
            .OrderBy(s => s.DataScadenza)
            .ThenBy(s => s.CreatedAt)
            .ThenBy(s => s.Id);

        var assegnati = 0;
        foreach (var s in ordinate)
        {
            var key = s.DataScadenza.ToString("yyyyMM");
            maxByMese.TryGetValue(key, out var n);
            n++;
            maxByMese[key] = n;
            var codice = $"SC-{key}-{n:D4}";

            await ctx.ScadenzePagamento.UpdateOneAsync(
                x => x.Id == s.Id,
                Builders<ScadenzaPagamento>.Update
                    .Set(x => x.Codice, codice)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow),
                cancellationToken: ct);
            assegnati++;
        }

        logger.LogInformation(
            "BackfillCodiceScadenze: assegnati {N} codici su {Tot} scadenze del tenant {Tid}",
            assegnati, tutte.Count, tenant.Id);
    }
}
