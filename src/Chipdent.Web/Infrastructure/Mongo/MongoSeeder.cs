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
            await MigrateLegacyRolesAsync(ctx, logger, ct);

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

            var milanoIdSeed = cliniche.FirstOrDefault(c => c.Citta == "Milano")?.Id;
            if (!string.IsNullOrEmpty(milanoIdSeed))
            {
                const string direttoreEmail = "direttore.milano@chipdent.it";
                if (!await ctx.Users.Find(u => u.Email == direttoreEmail).AnyAsync(ct))
                {
                    await ctx.Users.InsertOneAsync(new User
                    {
                        TenantId = tenant.Id,
                        Email = direttoreEmail,
                        PasswordHash = hasher.Hash("chipdent"),
                        FullName = "Anna Bianchi",
                        Role = UserRole.Direttore,
                        ClinicaIds = new List<string> { milanoIdSeed },
                        IsActive = true
                    }, cancellationToken: ct);
                    logger.LogInformation("Seeded direttore user {Email}", direttoreEmail);
                }

                const string backofficeEmail = "backoffice@chipdent.it";
                if (!await ctx.Users.Find(u => u.Email == backofficeEmail).AnyAsync(ct))
                {
                    await ctx.Users.InsertOneAsync(new User
                    {
                        TenantId = tenant.Id,
                        Email = backofficeEmail,
                        PasswordHash = hasher.Hash("chipdent"),
                        FullName = "Elena Rossi",
                        Role = UserRole.Backoffice,
                        IsActive = true
                    }, cancellationToken: ct);
                    logger.LogInformation("Seeded backoffice user {Email}", backofficeEmail);
                }
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

            if (!await ctx.DocumentiClinica.Find(d => d.TenantId == tenant.Id).AnyAsync(ct))
            {
                var docs = new List<DocumentoClinica>();
                foreach (var c in cliniche)
                {
                    docs.Add(new DocumentoClinica { TenantId = tenant.Id, ClinicaId = c.Id, Tipo = TipoDocumento.AutorizzazioneSanitaria, Titolo = $"Autorizzazione sanitaria {c.Citta}", Numero = $"AS-{c.Citta[..3].ToUpper()}-2024", DataEmissione = DateTime.UtcNow.AddYears(-2), DataScadenza = DateTime.UtcNow.AddMonths(8), EnteEmittente = $"ATS {c.Citta}" });
                    docs.Add(new DocumentoClinica { TenantId = tenant.Id, ClinicaId = c.Id, Tipo = TipoDocumento.CPI, Titolo = "Certificato Prevenzione Incendi", Numero = $"CPI-{c.Citta[..3].ToUpper()}-2023", DataEmissione = DateTime.UtcNow.AddYears(-1), DataScadenza = DateTime.UtcNow.AddMonths(2), EnteEmittente = "VV.F." });
                    docs.Add(new DocumentoClinica { TenantId = tenant.Id, ClinicaId = c.Id, Tipo = TipoDocumento.ContrattoAffitto, Titolo = $"Contratto locazione {c.Indirizzo}", DataEmissione = DateTime.UtcNow.AddYears(-3), DataScadenza = DateTime.UtcNow.AddYears(3) });
                }
                await ctx.DocumentiClinica.InsertManyAsync(docs, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} documenti", docs.Count);
            }

            if (!await ctx.Corsi.Find(c => c.TenantId == tenant.Id).AnyAsync(ct))
            {
                var dipsList = await ctx.Dipendenti.Find(d => d.TenantId == tenant.Id).ToListAsync(ct);
                var corsi = new List<Corso>();
                foreach (var d in dipsList)
                {
                    corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = d.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.Antincendio, DataConseguimento = DateTime.UtcNow.AddYears(-2), Scadenza = DateTime.UtcNow.AddMonths(4) });
                    corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = d.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.PrimoSoccorso, DataConseguimento = DateTime.UtcNow.AddYears(-1), Scadenza = DateTime.UtcNow.AddMonths(15) });
                }
                await ctx.Corsi.InsertManyAsync(corsi, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} corsi", corsi.Count);
            }

            if (!await ctx.VisiteMediche.Find(v => v.TenantId == tenant.Id).AnyAsync(ct))
            {
                var dipsList = await ctx.Dipendenti.Find(d => d.TenantId == tenant.Id).ToListAsync(ct);
                var visite = dipsList.Select(d => new VisitaMedica
                {
                    TenantId = tenant.Id,
                    DipendenteId = d.Id,
                    Data = DateTime.UtcNow.AddMonths(-6),
                    Esito = EsitoVisita.Idoneo,
                    ScadenzaIdoneita = DateTime.UtcNow.AddMonths(6)
                }).ToList();
                await ctx.VisiteMediche.InsertManyAsync(visite, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} visite mediche", visite.Count);
            }

            if (!await ctx.DVRs.Find(d => d.TenantId == tenant.Id).AnyAsync(ct))
            {
                var dvrs = cliniche.Select(c => new DVR
                {
                    TenantId = tenant.Id,
                    ClinicaId = c.Id,
                    Versione = "3.2",
                    DataApprovazione = DateTime.UtcNow.AddMonths(-4),
                    ProssimaRevisione = DateTime.UtcNow.AddMonths(8),
                    Stato = StatoDVR.Approvato
                }).ToList();
                await ctx.DVRs.InsertManyAsync(dvrs, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} DVR", dvrs.Count);
            }

            if (!await ctx.Comunicazioni.Find(c => c.TenantId == tenant.Id).AnyAsync(ct))
            {
                var comm = new[]
                {
                    new Comunicazione { TenantId = tenant.Id, MittenteUserId = owner.Id, MittenteNome = owner.FullName, Categoria = CategoriaComunicazione.Annuncio, Oggetto = "Benvenuti su Chipdent!", Corpo = "Da oggi tutta la catena gestisce turni, RLS e documenti da un'unica piattaforma. Buon lavoro!", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                    new Comunicazione { TenantId = tenant.Id, MittenteUserId = owner.Id, MittenteNome = owner.FullName, Categoria = CategoriaComunicazione.UrgenzaOperativa, Oggetto = "Manutenzione riunito 3 — Milano", Corpo = "Domani mattina dalle 7 alle 9 il riunito 3 sarà fuori uso per manutenzione programmata.", CreatedAt = DateTime.UtcNow.AddHours(-5) }
                };
                await ctx.Comunicazioni.InsertManyAsync(comm, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} comunicazioni", comm.Length);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mongo seed skipped: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Migra dati pre-refactor ruoli (Operatore/HR/Manager/Admin) al nuovo
    /// modello (Staff/Backoffice/Direttore/Management). Idempotente.
    /// </summary>
    private static async Task MigrateLegacyRolesAsync(MongoContext ctx, ILogger logger, CancellationToken ct)
    {
        try
        {
            // I valori numerici stabili — lo stesso enum coincide con la persistenza:
            //   Staff=0   (era Operatore=0)
            //   Backoffice=10 (era HR=10)
            //   Direttore=20  (era Manager=20)
            //   Management=30 (era Admin=30)
            //   Owner=99
            // Non c'è quindi rinumerazione: i record persistiti restano validi.
            // Garantiamo solo che ClinicaIds esista come array (mongo legacy può non averla).
            var users = await ctx.Users.Find(_ => true).ToListAsync(ct);
            var fixedCount = 0;
            foreach (var u in users)
            {
                if (u.ClinicaIds is null)
                {
                    await ctx.Users.UpdateOneAsync(
                        x => x.Id == u.Id,
                        Builders<User>.Update.Set(x => x.ClinicaIds, new List<string>()),
                        cancellationToken: ct);
                    fixedCount++;
                }
            }
            if (fixedCount > 0) logger.LogInformation("Migrated {Count} users (ClinicaIds backfill)", fixedCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Role migration skipped: {Message}", ex.Message);
        }
    }
}
