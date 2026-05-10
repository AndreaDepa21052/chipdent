using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Backfill difensivo: assicura che TUTTI i fornitori del tenant abbiano un
/// <see cref="Fornitore.Codice"/> valorizzato.
///
/// Casi gestiti:
///   - Fornitori-ombra creati prima che i dottori avessero un codice;
///   - Fornitori creati da <see cref="Tesoreria.ScadenziarioGenerator"/> in
///     versioni precedenti del codice (regressione storica);
///   - Qualsiasi altra fonte di Fornitori "anonimi".
///
/// Strategia di assegnazione:
///   - se il Fornitore è ombra di un Dottore con codice "D####", riceve lo
///     stesso codice del dottore (resta in sync 1:1 col modello);
///   - altrimenti gli viene assegnato il prossimo F#### libero, proseguendo
///     la sequenza esistente.
/// </summary>
internal static class BackfillCodiceFornitoriSeeder
{
    public static async Task SeedAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        var senzaCodice = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id
                    && (f.Codice == null || f.Codice == ""))
            .ToListAsync(ct);
        if (senzaCodice.Count == 0) return;

        // Sequenza F#### corrente
        var maxF = 0;
        var conCodice = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id && f.Codice != null && f.Codice != "")
            .Project(f => f.Codice)
            .ToListAsync(ct);
        foreach (var c in conCodice)
        {
            if (string.IsNullOrEmpty(c) || c[0] != 'F') continue;
            if (int.TryParse(c.AsSpan(1), out var n) && n > maxF) maxF = n;
        }

        var ombraCorrette = 0;
        var nuoveAssegnazioni = 0;

        foreach (var f in senzaCodice)
        {
            string codice;
            if (!string.IsNullOrEmpty(f.DottoreId))
            {
                // Prova ad ereditare il codice dal dottore collegato
                var dott = await ctx.Dottori
                    .Find(d => d.Id == f.DottoreId)
                    .Project(d => d.Codice)
                    .FirstOrDefaultAsync(ct);
                if (!string.IsNullOrEmpty(dott))
                {
                    codice = dott;
                    ombraCorrette++;
                }
                else
                {
                    maxF++;
                    codice = $"F{maxF:D4}";
                    nuoveAssegnazioni++;
                }
            }
            else
            {
                maxF++;
                codice = $"F{maxF:D4}";
                nuoveAssegnazioni++;
            }

            await ctx.Fornitori.UpdateOneAsync(
                x => x.Id == f.Id,
                Builders<Fornitore>.Update
                    .Set(x => x.Codice, codice)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow),
                cancellationToken: ct);
        }

        logger.LogInformation(
            "BackfillCodiceFornitori: assegnati {Totale} codici ({Ombra} ereditati dal dottore, {Nuovi} progressivi)",
            senzaCodice.Count, ombraCorrette, nuoveAssegnazioni);
    }
}
