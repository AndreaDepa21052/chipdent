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
            var tenant = await ctx.Tenants
                .Find(t => t.Slug == "confident")
                .FirstOrDefaultAsync(ct);

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

            var ownerEmail = "owner@chipdent.it";
            var owner = await ctx.Users
                .Find(u => u.Email == ownerEmail)
                .FirstOrDefaultAsync(ct);

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

            var anyClinica = await ctx.Cliniche
                .Find(c => c.TenantId == tenant.Id)
                .AnyAsync(ct);

            if (!anyClinica)
            {
                var seed = new[]
                {
                    new Clinica { TenantId = tenant.Id, Nome = "Confident Milano Centro", Citta = "Milano", Indirizzo = "Via Dante 12", Telefono = "+39 02 1234567", Email = "milano@confident.it", NumeroRiuniti = 6, Stato = ClinicaStato.Operativa },
                    new Clinica { TenantId = tenant.Id, Nome = "Confident Roma EUR", Citta = "Roma", Indirizzo = "Viale Europa 88", Telefono = "+39 06 9876543", Email = "roma@confident.it", NumeroRiuniti = 8, Stato = ClinicaStato.Operativa },
                    new Clinica { TenantId = tenant.Id, Nome = "Confident Torino", Citta = "Torino", Indirizzo = "Corso Vittorio 5", Telefono = "+39 011 5556677", Email = "torino@confident.it", NumeroRiuniti = 4, Stato = ClinicaStato.InApertura }
                };
                await ctx.Cliniche.InsertManyAsync(seed, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} cliniche", seed.Length);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mongo seed skipped: {Message}", ex.Message);
        }
    }
}
