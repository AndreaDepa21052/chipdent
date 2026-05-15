using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Seed delle 14 società del gruppo Confident, dati estratti dalle visure
/// camerali in FileRaw/Visure/ (aggiornate gen. 2026). Tutte le società
/// hanno la stessa sede legale fisica (Gallarate, VA).
/// L'IBAN è demo (IBAN test ABI 03069 di Intesa, valido per CRC ma non
/// reale): va sostituito col vero IBAN per ciascuna società.
/// </summary>
public static class SocietaSeeder
{
    private record Seed(
        string Nome,
        string RagioneSociale,
        string CodiceFiscale,
        string PartitaIva,
        string NumeroRea,
        string FormaGiuridica,
        DateTime DataCostituzione,
        decimal CapitaleSociale,
        string CodiceAteco,
        string Pec,
        string DemoIban,
        bool IsHolding = false);

    private static readonly Seed[] Catalogo = new[]
    {
        // L'unica S.p.A. è la holding CCH (sede legale principale del gruppo).
        new Seed("Ident CCH",          "CONFIDENT COMMERCIALE HOLDING S.P.A SOCIETA' BENEFIT", "11659240961", "11659240961", "VA - 384027", "società per azioni",            new DateTime(2021,03,10), 50_000m, "73.11.02", "cchsrl@pec.cgn.it",            "IT60X0306901604100000200001", IsHolding: true),
        new Seed("Ident",              "IDENT SRL",              "03774390128", "03774390128", "VA - 378013", "società a responsabilità limitata", new DateTime(2020,05,28), 30_000m, "86.23.00", "identsrl@pec.cgn.it",          "IT60X0306901604100000200002"),
        new Seed("Ident Cormano",      "IDENT CORMANO SRL",      "03886660129", "03886660129", "VA - 386325", "società a responsabilità limitata", new DateTime(2022,03,11), 20_000m, "86.23.00", "identcormano@pec.cgn.it",      "IT60X0306901604100000200003"),
        new Seed("Ident Giussano",     "IDENT GIUSSANO SRL",     "03846780124", "03846780124", "VA - 383450", "società a responsabilità limitata", new DateTime(2021,08,03), 20_000m, "86.23.00", "identgiussanosrl@pec.cgn.it",  "IT60X0306901604100000200004"),
        new Seed("Ident Milano 3",     "IDENT MILANO3 SRL",      "04044840124", "04044840124", "VA - 397827", "società a responsabilità limitata", new DateTime(2024,10,18), 20_000m, "86.23.00", "identmilano3srl@pec.it",       "IT60X0306901604100000200005"),
        new Seed("Ident Milano 6",     "IDENT MILANO6 SRL",      "04032580120", "04032580120", "VA - 396915", "società a responsabilità limitata", new DateTime(2024,07,12), 20_000m, "86.23.00", "identmilano6srl@pec.it",       "IT60X0306901604100000200006"),
        new Seed("Ident Milano 7",     "IDENT MILANO7 SRL",      "03916500121", "03916500121", "VA - 388438", "società a responsabilità limitata", new DateTime(2022,09,22), 20_000m, "86.23.00", "identmilano7srl@pec.it",       "IT60X0306901604100000200007"),
        new Seed("Ident Milano 9",     "IDENT MILANO9 SRL",      "03932250123", "03932250123", "VA - 389743", "società a responsabilità limitata", new DateTime(2023,01,10), 20_000m, "86.23.00", "identmilano9srl@pec.it",       "IT60X0306901604100000200008"),
        new Seed("Ident SGM",          "IDENT SGM SRL",          "03974920120", "03974920120", "VA - 392779", "società a responsabilità limitata", new DateTime(2023,09,05), 20_000m, "86.23.00", "identsgmsrl@pec.it",           "IT60X0306901604100000200009"),
        new Seed("Ident Varese",       "IDENT VARESE SRL",       "11685190966", "11685190966", "VA - 382574", "società a responsabilità limitata", new DateTime(2021,03,24), 30_000m, "86.23.00", "identvaresesrl@pec.cgn.it",    "IT60X0306901604100000200010"),
        new Seed("Ident Bollate",      "IDENT BOLLATE SRL",      "04001360124", "04001360124", "VA - 394627", "società a responsabilità limitata", new DateTime(2024,01,29), 20_000m, "86.23.00", "identbollatesrl@pec.it",       "IT60X0306901604100000200011"),
        new Seed("Ident Brugherio",    "IDENT BRUGHERIO SRL",    "04052040120", "04052040120", "VA - 398503", "società a responsabilità limitata", new DateTime(2024,12,18), 20_000m, "86.23.00", "identbrugheriosrl@pec.it",     "IT60X0306901604100000200012"),
        new Seed("Ident Busto Arsizio","IDENT BUSTO ARSIZIO SRL","03989870120", "03989870120", "VA - 393979", "società a responsabilità limitata", new DateTime(2023,12,19), 20_000m, "86.23.00", "identbustoarsiziosrl@pec.it",  "IT60X0306901604100000200013"),
        new Seed("Ident Comasina",     "IDENT COMASINA SRL",     "04080280128", "04080280128", "VA - 400403", "società a responsabilità limitata", new DateTime(2025,04,18), 20_000m, "86.23.00", "identcomasinasrl@pec.it",      "IT60X0306901604100000200014"),
        new Seed("Ident Como",         "IDENT COMO SRL",         "03891040127", "03891040127", "VA - 386598", "società a responsabilità limitata", new DateTime(2022,04,01), 20_000m, "86.23.00", "identcomosrl@pec.cgn.it",      "IT60X0306901604100000200015")
    };

    public static async Task SeedAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct = default)
    {
        var existing = await ctx.Societa
            .Find(s => s.TenantId == tenant.Id)
            .ToListAsync(ct);

        // Insert idempotente per CodiceFiscale: aggiunge solo le società mancanti.
        var existingCfs = existing
            .Where(e => !string.IsNullOrEmpty(e.CodiceFiscale))
            .Select(e => e.CodiceFiscale!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nuove = new List<Societa>();
        foreach (var s in Catalogo)
        {
            if (existingCfs.Contains(s.CodiceFiscale)) continue;
            nuove.Add(new Societa
            {
                TenantId = tenant.Id,
                Nome = s.Nome,
                RagioneSociale = s.RagioneSociale,
                CodiceFiscale = s.CodiceFiscale,
                PartitaIva = s.PartitaIva,
                NumeroRea = s.NumeroRea,
                FormaGiuridica = s.FormaGiuridica,
                DataCostituzione = DateTime.SpecifyKind(s.DataCostituzione, DateTimeKind.Utc),
                CapitaleSociale = s.CapitaleSociale,
                CodiceAteco = s.CodiceAteco,
                Pec = s.Pec,
                IndirizzoSedeLegale = "VIA GIACOMO MATTEOTTI 2",
                ComuneSedeLegale = "GALLARATE",
                ProvinciaSedeLegale = "VA",
                CapSedeLegale = "21013",
                Iban = s.DemoIban,
                Bic = "BCITITMM",
                IsHolding = s.IsHolding
            });
        }
        if (nuove.Count > 0)
        {
            await ctx.Societa.InsertManyAsync(nuove, cancellationToken: ct);
            logger.LogInformation("Seeded {Count} società", nuove.Count);
        }

        await BackfillClinicaSocietaAsync(ctx, tenant, logger, ct);
    }

    /// <summary>
    /// Migrazione idempotente: lega ogni clinica priva di SocietaId alla
    /// società corrispondente per nome (matching basato su suffisso comune).
    /// </summary>
    private static async Task BackfillClinicaSocietaAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        var societa = await ctx.Societa
            .Find(s => s.TenantId == tenant.Id)
            .ToListAsync(ct);
        if (societa.Count == 0) return;

        // Mappa: nome breve clinica → ragione sociale società.
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CORMANO"]    = "IDENT CORMANO SRL",
            ["GIUSSANO"]   = "IDENT GIUSSANO SRL",
            ["MILANO3"]    = "IDENT MILANO3 SRL",
            ["MILANO6"]    = "IDENT MILANO6 SRL",
            ["MILANO7"]    = "IDENT MILANO7 SRL",
            ["MILANO9"]    = "IDENT MILANO9 SRL",
            ["SGM"]        = "IDENT SGM SRL",
            ["VARESE"]     = "IDENT VARESE SRL",
            ["BOLLATE"]    = "IDENT BOLLATE SRL",
            ["BRUGHERIO"]  = "IDENT BRUGHERIO SRL",
            ["BUSTO A."]   = "IDENT BUSTO ARSIZIO SRL",
            ["COMASINA"]   = "IDENT COMASINA SRL",
            ["COMO"]       = "IDENT COMO SRL",
            ["CCH"]        = "CONFIDENT COMMERCIALE HOLDING S.P.A SOCIETA' BENEFIT",
            ["DESIO"]      = "IDENT SRL"
        };

        var byRagSoc = societa.ToDictionary(s => s.RagioneSociale, s => s, StringComparer.OrdinalIgnoreCase);
        var cliniche = await ctx.Cliniche
            .Find(c => c.TenantId == tenant.Id && (c.SocietaId == null || c.SocietaId == ""))
            .ToListAsync(ct);

        var legate = 0;
        foreach (var c in cliniche)
        {
            if (!mapping.TryGetValue(c.Nome, out var rs)) continue;
            if (!byRagSoc.TryGetValue(rs, out var soc)) continue;
            await ctx.Cliniche.UpdateOneAsync(
                x => x.Id == c.Id,
                Builders<Clinica>.Update.Set(x => x.SocietaId, soc.Id).Set(x => x.UpdatedAt, DateTime.UtcNow),
                cancellationToken: ct);
            legate++;
        }
        if (legate > 0)
        {
            logger.LogInformation("Backfill SocietaId su {Count} cliniche", legate);
        }
    }
}
