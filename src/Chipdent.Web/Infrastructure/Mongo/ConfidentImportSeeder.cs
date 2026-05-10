using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Sepa;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Importa l'anagrafica reale Confident (fornitori + dottori) dal file
/// "FORNITORI Confident.xlsx" tradotto in <see cref="ConfidentImportSeedData"/>.
///
/// Migrazione one-shot:
///   - se NESSUN fornitore del tenant ha un <see cref="Fornitore.Codice"/> impostato
///     (segno del vecchio seed demo) cancelliamo fornitori, dottori e i dati di
///     tesoreria da loro dipendenti (fatture/scadenze) e ricarichiamo l'anagrafica
///     reale, assegnando un codice progressivo F#### / D####.
///   - altrimenti l'importer è no-op.
///
/// Per ogni dottore importato viene anche creato il Fornitore-ombra collegato
/// (riusa <see cref="FornitoreOmbraService"/>): così l'anagrafica fornitori
/// contiene sia le aziende che i dottori, con il loro codice.
/// </summary>
internal static class ConfidentImportSeeder
{
    public static async Task SeedAsync(
        MongoContext ctx,
        Tenant tenant,
        FornitoreOmbraService ombraService,
        ILogger logger,
        CancellationToken ct)
    {
        var hasCodice = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id && f.Codice != null && f.Codice != "")
            .AnyAsync(ct);
        if (hasCodice) return; // import già eseguito

        // Migrazione: vecchi seed demo presenti senza Codice → li ripuliamo per
        // ricaricare l'anagrafica reale. Anche fatture/scadenze e gli utenti
        // portale collegati ai fornitori demo.
        var oldFornitori = await ctx.Fornitori.Find(f => f.TenantId == tenant.Id).ToListAsync(ct);
        if (oldFornitori.Count > 0)
        {
            var oldIds = oldFornitori.Select(f => f.Id).ToList();
            await ctx.Fatture.DeleteManyAsync(
                f => f.TenantId == tenant.Id && oldIds.Contains(f.FornitoreId), ct);
            await ctx.ScadenzePagamento.DeleteManyAsync(
                s => s.TenantId == tenant.Id && oldIds.Contains(s.FornitoreId), ct);
            await ctx.Users.DeleteManyAsync(
                u => u.TenantId == tenant.Id
                  && u.Role == UserRole.Fornitore
                  && u.LinkedPersonType == LinkedPersonType.Fornitore
                  && u.LinkedPersonId != null
                  && oldIds.Contains(u.LinkedPersonId), ct);
            await ctx.Fornitori.DeleteManyAsync(f => f.TenantId == tenant.Id, ct);
            logger.LogInformation(
                "ConfidentImport: rimossi {Count} fornitori demo (+ fatture/scadenze/utenti collegati)",
                oldFornitori.Count);
        }

        var oldDottoriCount = await ctx.Dottori.CountDocumentsAsync(d => d.TenantId == tenant.Id, cancellationToken: ct);
        if (oldDottoriCount > 0)
        {
            await ctx.Dottori.DeleteManyAsync(d => d.TenantId == tenant.Id, ct);
            logger.LogInformation("ConfidentImport: rimossi {Count} dottori demo", oldDottoriCount);
        }

        // Import fornitori
        var fornitori = ConfidentImportSeedData.BuildFornitori(tenant.Id);
        if (fornitori.Count > 0)
        {
            await ctx.Fornitori.InsertManyAsync(fornitori, cancellationToken: ct);
            logger.LogInformation("ConfidentImport: importati {Count} fornitori", fornitori.Count);
        }

        // Import dottori (+ fornitori-ombra)
        var dottori = ConfidentImportSeedData.BuildDottori(tenant.Id);
        if (dottori.Count > 0)
        {
            await ctx.Dottori.InsertManyAsync(dottori, cancellationToken: ct);
            logger.LogInformation("ConfidentImport: importati {Count} dottori", dottori.Count);

            var ombra = 0;
            foreach (var d in dottori)
            {
                var f = await ombraService.EnsureForDottoreAsync(d, ct);
                if (f != null) ombra++;
            }
            logger.LogInformation("ConfidentImport: creati {Count} fornitori-ombra dottori", ombra);
        }
    }
}
