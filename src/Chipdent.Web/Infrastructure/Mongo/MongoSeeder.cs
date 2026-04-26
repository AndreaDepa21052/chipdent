using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

public static class MongoSeeder
{
    public static async Task SeedAsync(MongoContext ctx, IPasswordHasher hasher, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var tenant = await ctx.Tenants.Find(t => t.Slug == "confident").FirstOrDefaultAsync(ct);
            if (tenant is null)
            {
                tenant = new Tenant
                {
                    Slug = "confident",
                    DisplayName = "Confident Dental",
                    PrimaryColor = "#c47830",
                    IsActive = true
                };
                await ctx.Tenants.InsertOneAsync(tenant, cancellationToken: ct);
                logger.LogInformation("Seeded tenant {Slug}", tenant.Slug);
            }

            const string ownerEmail = "owner@chipdent.it";
            var owner = await ctx.Users.Find(u => u.Email == ownerEmail).FirstOrDefaultAsync(ct);
            if (owner is null)
            {
                owner = new User
                {
                    TenantId = tenant.Id,
                    Email = ownerEmail,
                    PasswordHash = hasher.Hash("chipdent"),
                    FullName = "Andrea De Pa",
                    Role = UserRole.Owner,
                    IsActive = true
                };
                await ctx.Users.InsertOneAsync(owner, cancellationToken: ct);
                logger.LogInformation("Seeded owner user {Email}", ownerEmail);
            }

            var cliniche = await ctx.Cliniche.Find(c => c.TenantId == tenant.Id).ToListAsync(ct);
            if (cliniche.Count == 0)
            {
                cliniche = new()
                {
                    new Clinica { TenantId = tenant.Id, Nome = "Confident Milano Centro", Citta = "Milano", Indirizzo = "Via Dante 12", Telefono = "+39 02 1234567", Email = "milano@confident.it", NumeroRiuniti = 6, Stato = ClinicaStato.Operativa },
                    new Clinica { TenantId = tenant.Id, Nome = "Confident Roma EUR", Citta = "Roma", Indirizzo = "Viale Europa 88", Telefono = "+39 06 9876543", Email = "roma@confident.it", NumeroRiuniti = 8, Stato = ClinicaStato.Operativa },
                    new Clinica { TenantId = tenant.Id, Nome = "Confident Torino", Citta = "Torino", Indirizzo = "Corso Vittorio 5", Telefono = "+39 011 5556677", Email = "torino@confident.it", NumeroRiuniti = 4, Stato = ClinicaStato.InApertura }
                };
                await ctx.Cliniche.InsertManyAsync(cliniche, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} cliniche", cliniche.Count);
            }

            if (!await ctx.Dottori.Find(d => d.TenantId == tenant.Id).AnyAsync(ct))
            {
                var milano = cliniche.FirstOrDefault(c => c.Citta == "Milano")?.Id;
                var roma = cliniche.FirstOrDefault(c => c.Citta == "Roma")?.Id;
                var dottori = new[]
                {
                    new Dottore { TenantId = tenant.Id, Nome = "Marco", Cognome = "Bianchi", Email = "m.bianchi@confident.it", Specializzazione = "Implantologia", NumeroAlbo = "MI-1234", ScadenzaAlbo = DateTime.UtcNow.AddMonths(8), TipoContratto = TipoContratto.Collaborazione, ClinicaPrincipaleId = milano },
                    new Dottore { TenantId = tenant.Id, Nome = "Laura", Cognome = "Ferri", Email = "l.ferri@confident.it", Specializzazione = "Ortodonzia", NumeroAlbo = "MI-2210", ScadenzaAlbo = DateTime.UtcNow.AddMonths(2), TipoContratto = TipoContratto.LiberoProfessionista, ClinicaPrincipaleId = milano },
                    new Dottore { TenantId = tenant.Id, Nome = "Paolo", Cognome = "Rizzo", Email = "p.rizzo@confident.it", Specializzazione = "Endodonzia", NumeroAlbo = "RM-887", ScadenzaAlbo = DateTime.UtcNow.AddYears(2), TipoContratto = TipoContratto.Collaborazione, ClinicaPrincipaleId = roma }
                };
                await ctx.Dottori.InsertManyAsync(dottori, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} dottori", dottori.Length);
            }

            if (!await ctx.Dipendenti.Find(d => d.TenantId == tenant.Id).AnyAsync(ct))
            {
                var milano = cliniche.FirstOrDefault(c => c.Citta == "Milano")?.Id ?? string.Empty;
                var roma = cliniche.FirstOrDefault(c => c.Citta == "Roma")?.Id ?? string.Empty;
                var dipendenti = new[]
                {
                    new Dipendente { TenantId = tenant.Id, Nome = "Sara", Cognome = "Conti", Email = "s.conti@confident.it", Ruolo = RuoloDipendente.ASO, ClinicaId = roma, DataAssunzione = DateTime.UtcNow.AddYears(-3), GiorniFerieResidui = 12, Stato = StatoDipendente.Attivo },
                    new Dipendente { TenantId = tenant.Id, Nome = "Giulia", Cognome = "Moretti", Email = "g.moretti@confident.it", Ruolo = RuoloDipendente.Igienista, ClinicaId = milano, DataAssunzione = DateTime.UtcNow.AddYears(-1), GiorniFerieResidui = 22, Stato = StatoDipendente.Attivo },
                    new Dipendente { TenantId = tenant.Id, Nome = "Federica", Cognome = "Marini", Email = "f.marini@confident.it", Ruolo = RuoloDipendente.Segreteria, ClinicaId = milano, DataAssunzione = DateTime.UtcNow.AddMonths(-2), GiorniFerieResidui = 24, Stato = StatoDipendente.Onboarding }
                };
                await ctx.Dipendenti.InsertManyAsync(dipendenti, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} dipendenti", dipendenti.Length);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mongo seed skipped: {Message}", ex.Message);
        }
    }
}
