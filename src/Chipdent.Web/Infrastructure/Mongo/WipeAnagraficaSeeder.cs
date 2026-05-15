using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Migrazione one-shot di reset anagrafica (richiesta utente 2026-05-15):
/// cancella fornitori, dottori, corsi-dottori e attestati ECM del tenant, più
/// le dipendenze del modulo Tesoreria collegate ai fornitori e i documenti
/// associati ai dottori. Lasciamo intatti gli utenti del portale fornitore
/// (Role = Fornitore): rimangono orfani fino a riassegnazione manuale.
///
/// Scope cascata (concordato con l'utente):
///   - Fornitori   → fatture, scadenze, distinte SEPA, proposte anagrafica,
///                   batch e righe degli import (storia caricamenti PDF).
///   - Dottori     → collaborazioni-dottore, documenti-dottore.
///   - Corsi       → solo i record con DestinatarioTipo = Dottore (i corsi di
///                   sicurezza/RLS dei dipendenti restano).
///   - AttestatiEcm→ tutti (sono solo dei dottori).
///
/// Trigger one-shot: la migrazione è marcata <c>"wipe-anagrafica-2026-05-15"</c>
/// in <see cref="Tenant.MigrazioniApplicate"/>. Se l'utente vorrà rifarla in
/// futuro, basterà cambiare la chiave (o rimuoverla dalla lista del tenant).
///
/// Ordine in <see cref="MongoSeeder.SeedAsync"/>: deve girare PRIMA di
/// <see cref="ConfidentImportSeeder"/>, così la condizione "nessun fornitore
/// con Codice" diventa vera e il re-seed dall'anagrafica seed-data parte
/// nello stesso startup.
/// </summary>
internal static class WipeAnagraficaSeeder
{
    private const string MigrationKey = "wipe-anagrafica-2026-05-15";

    public static async Task SeedAsync(
        MongoContext ctx,
        Tenant tenant,
        ILogger logger,
        CancellationToken ct)
    {
        if (tenant.MigrazioniApplicate.Contains(MigrationKey)) return;

        var tid = tenant.Id;

        // ── 1. Fornitori + dipendenze Tesoreria ──────────────────────────
        var fornitoriCount = await ctx.Fornitori
            .CountDocumentsAsync(f => f.TenantId == tid, cancellationToken: ct);
        if (fornitoriCount > 0)
        {
            await ctx.Fatture.DeleteManyAsync(f => f.TenantId == tid, ct);
            await ctx.ScadenzePagamento.DeleteManyAsync(s => s.TenantId == tid, ct);
            await ctx.DistinteSepa.DeleteManyAsync(d => d.TenantId == tid, ct);
            await ctx.ProposteAnagraficaFornitori.DeleteManyAsync(p => p.TenantId == tid, ct);
            await ctx.ImportFattureBatches.DeleteManyAsync(b => b.TenantId == tid, ct);
            await ctx.ImportFattureRighe.DeleteManyAsync(r => r.TenantId == tid, ct);
            await ctx.Fornitori.DeleteManyAsync(f => f.TenantId == tid, ct);
            logger.LogWarning(
                "WipeAnagrafica: rimossi {N} fornitori e tutte le dipendenze Tesoreria (fatture, scadenze, distinte, proposte, batch import)",
                fornitoriCount);
        }

        // ── 2. Dottori + dipendenze (collaborazioni, documenti) ──────────
        var dottoriCount = await ctx.Dottori
            .CountDocumentsAsync(d => d.TenantId == tid, cancellationToken: ct);
        if (dottoriCount > 0)
        {
            await ctx.CollaborazioniDottori.DeleteManyAsync(c => c.TenantId == tid, ct);
            await ctx.DocumentiDottore.DeleteManyAsync(d => d.TenantId == tid, ct);
            await ctx.Dottori.DeleteManyAsync(d => d.TenantId == tid, ct);
            logger.LogWarning(
                "WipeAnagrafica: rimossi {N} dottori + collaborazioni e documenti collegati",
                dottoriCount);
        }

        // ── 3. Corsi assegnati a Dottori (i corsi dei dipendenti restano) ─
        var corsiCount = await ctx.Corsi
            .CountDocumentsAsync(c => c.TenantId == tid && c.DestinatarioTipo == DestinatarioCorso.Dottore,
                                 cancellationToken: ct);
        if (corsiCount > 0)
        {
            await ctx.Corsi.DeleteManyAsync(
                c => c.TenantId == tid && c.DestinatarioTipo == DestinatarioCorso.Dottore, ct);
            logger.LogWarning("WipeAnagrafica: rimossi {N} corsi dottori (formazione)", corsiCount);
        }

        // ── 4. Attestati ECM (sono tutti dei dottori) ────────────────────
        var ecmCount = await ctx.AttestatiEcm
            .CountDocumentsAsync(e => e.TenantId == tid, cancellationToken: ct);
        if (ecmCount > 0)
        {
            await ctx.AttestatiEcm.DeleteManyAsync(e => e.TenantId == tid, ct);
            logger.LogWarning("WipeAnagrafica: rimossi {N} attestati ECM", ecmCount);
        }

        // ── 5. Marca migrazione come applicata ───────────────────────────
        await ctx.Tenants.UpdateOneAsync(
            t => t.Id == tid,
            Builders<Tenant>.Update.AddToSet(t => t.MigrazioniApplicate, MigrationKey),
            cancellationToken: ct);
        tenant.MigrazioniApplicate.Add(MigrationKey);

        logger.LogInformation(
            "WipeAnagrafica: completata — fornitori {F}, dottori {D}, corsi-dottori {C}, ECM {E} cancellati. ConfidentImportSeeder ri-seederà l'anagrafica nello stesso startup.",
            fornitoriCount, dottoriCount, corsiCount, ecmCount);
    }
}
