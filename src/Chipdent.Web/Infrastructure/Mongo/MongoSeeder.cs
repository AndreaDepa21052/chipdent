using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Sepa;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

public static class MongoSeeder
{
    public static async Task SeedAsync(MongoContext ctx, IPasswordHasher hasher, FornitoreOmbraService ombraService, ILogger logger, CancellationToken ct = default)
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
                    DisplayName = "Confident",
                    PrimaryColor = "#c47830",
                    IsActive = true,
                    // Dati ordinante demo per le distinte SEPA (IBAN test BIC valido).
                    PagatoreRagioneSociale = "Confident S.p.A.",
                    PagatoreIban = "IT60X0542811101000000123456",
                    PagatoreBic = "BPMOIT22XXX"
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

            // Migrazione one-shot: rimuove le 3 cliniche demo storiche (Confident Milano Centro / Roma EUR / Torino)
            // così che la rete reale Confident (14 sedi Lombardia) venga seedata al primo avvio successivo.
            var legacyNames = new[] { "Confident Milano Centro", "Confident Roma EUR", "Confident Torino" };
            var legacyCliniche = await ctx.Cliniche
                .Find(c => c.TenantId == tenant.Id && legacyNames.Contains(c.Nome))
                .ToListAsync(ct);
            if (legacyCliniche.Count > 0)
            {
                var legacyIds = legacyCliniche.Select(c => c.Id).ToList();
                await ctx.Cliniche.DeleteManyAsync(c => legacyIds.Contains(c.Id), ct);
                await ctx.DocumentiClinica.DeleteManyAsync(d => d.TenantId == tenant.Id && legacyIds.Contains(d.ClinicaId), ct);
                await ctx.DVRs.DeleteManyAsync(d => d.TenantId == tenant.Id && legacyIds.Contains(d.ClinicaId), ct);
                await ctx.ProtocolliClinica.DeleteManyAsync(p => p.TenantId == tenant.Id && legacyIds.Contains(p.ClinicaId), ct);
                await ctx.Rentri.DeleteManyAsync(r => r.TenantId == tenant.Id && legacyIds.Contains(r.ClinicaId), ct);
                await ctx.InterventiClinica.DeleteManyAsync(i => i.TenantId == tenant.Id && legacyIds.Contains(i.ClinicaId), ct);
                logger.LogInformation("Migrated out {Count} legacy demo cliniche", legacyCliniche.Count);
            }

            var cliniche = await ctx.Cliniche.Find(c => c.TenantId == tenant.Id).ToListAsync(ct);
            if (cliniche.Count == 0)
            {
                // Rete reale Confident (Lombardia). Indirizzi/telefono/email sono dati demo verosimili.
                cliniche = new()
                {
                    new Clinica { TenantId = tenant.Id, Nome = "DESIO",      Citta = "Desio (MB)",          Indirizzo = "Via Garibaldi 24",      Telefono = "+39 0362 123450", Email = "desio@confident.it",      NumeroRiuniti = 5, Stato = ClinicaStato.Operativa, Latitudine = 45.6207, Longitudine = 9.2096, IbanOrdinante = "IT60X0306901604100000123450", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Desio S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "VARESE",     Citta = "Varese",              Indirizzo = "Corso Matteotti 18",    Telefono = "+39 0332 234561", Email = "varese@confident.it",     NumeroRiuniti = 5, Stato = ClinicaStato.Operativa, Latitudine = 45.8205, Longitudine = 8.8251, IbanOrdinante = "IT60X0306901604100000123451", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Varese S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "GIUSSANO",   Citta = "Giussano (MB)",       Indirizzo = "Via Italia 9",          Telefono = "+39 0362 345672", Email = "giussano@confident.it",   NumeroRiuniti = 4, Stato = ClinicaStato.Operativa, Latitudine = 45.6979, Longitudine = 9.2047, IbanOrdinante = "IT60X0306901604100000123452", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Giussano S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "CORMANO",    Citta = "Cormano (MI)",        Indirizzo = "Via Roma 33",           Telefono = "+39 02 615783",   Email = "cormano@confident.it",    NumeroRiuniti = 4, Stato = ClinicaStato.Operativa, Latitudine = 45.5470, Longitudine = 9.1641, IbanOrdinante = "IT60X0306901604100000123453", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Cormano S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "COMO",       Citta = "Como",                Indirizzo = "Viale Lecco 41",        Telefono = "+39 031 456784",  Email = "como@confident.it",       NumeroRiuniti = 3, Stato = ClinicaStato.Operativa, Latitudine = 45.8081, Longitudine = 9.0852, IbanOrdinante = "IT60X0306901604100000123454", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Como S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "MILANO7",    Citta = "Milano",              Indirizzo = "Via Settembrini 7",     Telefono = "+39 02 567895",   Email = "milano7@confident.it",    NumeroRiuniti = 6, Stato = ClinicaStato.Operativa, Latitudine = 45.4806, Longitudine = 9.2024, IbanOrdinante = "IT60X0306901604100000123455", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Milano 7 S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "MILANO9",    Citta = "Milano",              Indirizzo = "Via Padova 99",         Telefono = "+39 02 678906",   Email = "milano9@confident.it",    NumeroRiuniti = 5, Stato = ClinicaStato.Operativa, Latitudine = 45.4983, Longitudine = 9.2305, IbanOrdinante = "IT60X0306901604100000123456", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Milano 9 S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "SGM",        Citta = "San Giuliano Milanese", Indirizzo = "Via Della Repubblica 12", Telefono = "+39 02 789017", Email = "sgm@confident.it",       NumeroRiuniti = 4, Stato = ClinicaStato.Operativa, Latitudine = 45.3997, Longitudine = 9.2862, IbanOrdinante = "IT60X0306901604100000123457", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident SGM S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "BUSTO A.",   Citta = "Busto Arsizio (VA)",  Indirizzo = "Via Volta 14",          Telefono = "+39 0331 890128", Email = "bustoa@confident.it",     NumeroRiuniti = 4, Stato = ClinicaStato.Operativa, Latitudine = 45.6111, Longitudine = 8.8497, IbanOrdinante = "IT60X0306901604100000123458", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Busto Arsizio S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "BOLLATE",    Citta = "Bollate (MI)",        Indirizzo = "Via Magenta 22",        Telefono = "+39 02 901239",   Email = "bollate@confident.it",    NumeroRiuniti = 4, Stato = ClinicaStato.Operativa, Latitudine = 45.5481, Longitudine = 9.1167, IbanOrdinante = "IT60X0306901604100000123459", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Bollate S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "MILANO6",    Citta = "Milano",              Indirizzo = "Via Lorenteggio 60",    Telefono = "+39 02 012340",   Email = "milano6@confident.it",    NumeroRiuniti = 5, Stato = ClinicaStato.Operativa, Latitudine = 45.4500, Longitudine = 9.1430, IbanOrdinante = "IT60X0306901604100000123460", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Milano 6 S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "MILANO3",    Citta = "Milano",              Indirizzo = "Via Tre Castelli 3",    Telefono = "+39 02 123451",   Email = "milano3@confident.it",    NumeroRiuniti = 5, Stato = ClinicaStato.Operativa, Latitudine = 45.4615, Longitudine = 9.1900, IbanOrdinante = "IT60X0306901604100000123461", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Milano 3 S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "BRUGHERIO",  Citta = "Brugherio (MB)",      Indirizzo = "Via San Maurizio 4",    Telefono = "+39 039 234562",  Email = "brugherio@confident.it",  NumeroRiuniti = 3, Stato = ClinicaStato.Operativa, Latitudine = 45.5524, Longitudine = 9.2980, IbanOrdinante = "IT60X0306901604100000123462", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Brugherio S.r.l." },
                    new Clinica { TenantId = tenant.Id, Nome = "COMASINA",   Citta = "Milano",              Indirizzo = "Viale Comasina 200",    Telefono = "+39 02 345673",   Email = "comasina@confident.it",   NumeroRiuniti = 3, Stato = ClinicaStato.InApertura, Latitudine = 45.5290, Longitudine = 9.1690, IbanOrdinante = "IT60X0306901604100000123463", BicOrdinante = "BCITITMM", RagioneSocialeOrdinante = "Confident Comasina S.r.l." }
                };
                await ctx.Cliniche.InsertManyAsync(cliniche, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} cliniche", cliniche.Count);
            }

            var milanoIdSeed = cliniche.FirstOrDefault(c => c.Nome == "MILANO7")?.Id;
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
                var milano = cliniche.FirstOrDefault(c => c.Nome == "MILANO7")?.Id;
                var altraSede = cliniche.FirstOrDefault(c => c.Nome == "BUSTO A.")?.Id;
                var dottori = new[]
                {
                    new Dottore { TenantId = tenant.Id, Nome = "Marco", Cognome = "Bianchi", Email = "m.bianchi@confident.it", Specializzazione = "Implantologia", NumeroAlbo = "MI-1234", ScadenzaAlbo = DateTime.UtcNow.AddMonths(8), TipoContratto = TipoContratto.Collaborazione, ClinicaPrincipaleId = milano },
                    new Dottore { TenantId = tenant.Id, Nome = "Laura", Cognome = "Ferri", Email = "l.ferri@confident.it", Specializzazione = "Ortodonzia", NumeroAlbo = "MI-2210", ScadenzaAlbo = DateTime.UtcNow.AddMonths(2), TipoContratto = TipoContratto.LiberoProfessionista, ClinicaPrincipaleId = milano },
                    new Dottore { TenantId = tenant.Id, Nome = "Paolo", Cognome = "Rizzo", Email = "p.rizzo@confident.it", Specializzazione = "Endodonzia", NumeroAlbo = "VA-887", ScadenzaAlbo = DateTime.UtcNow.AddYears(2), TipoContratto = TipoContratto.Collaborazione, ClinicaPrincipaleId = altraSede }
                };
                await ctx.Dottori.InsertManyAsync(dottori, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} dottori", dottori.Length);
            }

            if (!await ctx.Dipendenti.Find(d => d.TenantId == tenant.Id).AnyAsync(ct))
            {
                var milano = cliniche.FirstOrDefault(c => c.Nome == "MILANO7")?.Id ?? string.Empty;
                var altraSede = cliniche.FirstOrDefault(c => c.Nome == "BUSTO A.")?.Id ?? string.Empty;
                var dipendenti = new[]
                {
                    new Dipendente { TenantId = tenant.Id, Nome = "Sara", Cognome = "Conti", Sesso = Sesso.F, Email = "s.conti@confident.it", Telefono = "+39 333 1112233", Nazionalita = "Italiana", IndirizzoResidenza = "Via Volta 14", CittaResidenza = "Busto Arsizio", CapResidenza = "21052", Ruolo = RuoloDipendente.ASO, ClinicaId = altraSede, DataPrimoRapporto = DateTime.UtcNow.AddYears(-5), DataAssunzione = DateTime.UtcNow.AddYears(-3), Ccnl = "Studi professionali", LivelloContratto = "4° livello", MeseAnnoCcnl = "Mar 2022", MonteOreSettimanale = 38, BeneficioTicket = true, ExTirocinante = true, TitoloStudio = "Diploma operatore servizi sociali", AutocertificazioneTitolo = true, ScadenzaCartaIdentita = DateTime.UtcNow.AddYears(2), GiorniFerieResidui = 12, Stato = StatoDipendente.Attivo },
                    new Dipendente { TenantId = tenant.Id, Nome = "Giulia", Cognome = "Moretti", Sesso = Sesso.F, Email = "g.moretti@confident.it", Telefono = "+39 333 4445566", Nazionalita = "Italiana", IndirizzoResidenza = "Via Milano 5", CittaResidenza = "Milano", CapResidenza = "20100", Ruolo = RuoloDipendente.Igienista, ClinicaId = milano, DataAssunzione = DateTime.UtcNow.AddYears(-1), Ccnl = "Studi professionali", LivelloContratto = "3° livello", MonteOreSettimanale = 38, BeneficioTicket = true, TitoloStudio = "Laurea in Igiene Dentale", ScadenzaCartaIdentita = DateTime.UtcNow.AddYears(4), GiorniFerieResidui = 22, Stato = StatoDipendente.Attivo },
                    new Dipendente { TenantId = tenant.Id, Nome = "Federica", Cognome = "Marini", Sesso = Sesso.F, Email = "f.marini@confident.it", Cellulare = "+39 333 7778899", Nazionalita = "Italiana", IndirizzoResidenza = "Corso Buenos Aires 99", CittaResidenza = "Milano", CapResidenza = "20124", Ruolo = RuoloDipendente.Segreteria, ClinicaId = milano, DataAssunzione = DateTime.UtcNow.AddMonths(-2), Ccnl = "Studi professionali", LivelloContratto = "5° livello", MonteOreSettimanale = 30, DataScadenzaContratto = DateTime.UtcNow.AddMonths(10), TitoloStudio = "Diploma maturità classica", ScadenzaCartaIdentita = DateTime.UtcNow.AddYears(3), ScadenzaPermessoSoggiorno = DateTime.UtcNow.AddDays(45), GiorniFerieResidui = 24, Stato = StatoDipendente.Onboarding }
                };
                await ctx.Dipendenti.InsertManyAsync(dipendenti, cancellationToken: ct);
                logger.LogInformation("Seeded {Count} dipendenti", dipendenti.Length);
            }

            // Utente Staff demo, linkato al dipendente Giulia Moretti (Igienista, Milano)
            const string staffEmail = "staff@chipdent.it";
            if (!await ctx.Users.Find(u => u.Email == staffEmail).AnyAsync(ct))
            {
                var giulia = await ctx.Dipendenti
                    .Find(d => d.TenantId == tenant.Id && d.Email == "g.moretti@confident.it")
                    .FirstOrDefaultAsync(ct);
                if (giulia is not null)
                {
                    await ctx.Users.InsertOneAsync(new User
                    {
                        TenantId = tenant.Id,
                        Email = staffEmail,
                        PasswordHash = hasher.Hash("chipdent"),
                        FullName = giulia.NomeCompleto,
                        Role = UserRole.Staff,
                        ClinicaIds = new List<string> { giulia.ClinicaId },
                        LinkedPersonType = LinkedPersonType.Dipendente,
                        LinkedPersonId = giulia.Id,
                        IsActive = true
                    }, cancellationToken: ct);
                    logger.LogInformation("Seeded staff user {Email} linked to {Dip}", staffEmail, giulia.NomeCompleto);
                }
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

            await SeedHistoricalAiDataAsync(ctx, tenant, logger, ct);
            await SeedTesoreriaAsync(ctx, hasher, tenant, cliniche, logger, ct);
            await SeedCashflowAsync(ctx, tenant, cliniche, logger, ct);
            await SeedChecklistDipendenteAsync(ctx, tenant, cliniche, logger, ct);
            await InterventiSeed.SeedAsync(ctx, tenant, cliniche, logger, ct);

            // Crea/aggiorna fornitori-ombra per i dottori (collaborazione/libero professionista)
            var ombraCreati = await ombraService.SyncTenantAsync(tenant.Id, ct);
            if (ombraCreati > 0)
            {
                logger.LogInformation("Sincronizzati {Count} fornitori-ombra dei dottori", ombraCreati);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mongo seed skipped: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Seed demo del modulo Tesoreria: fornitori, fatture, scadenze (passato + presente + futuro).
    /// Crea anche un utente di portale per il primo fornitore (lereti@demo.it / chipdent).
    /// Idempotente: salta se ci sono già fornitori per il tenant.
    /// </summary>
    private static async Task SeedTesoreriaAsync(MongoContext ctx, IPasswordHasher hasher, Tenant tenant, List<Clinica> cliniche, ILogger logger, CancellationToken ct)
    {
        if (await ctx.Fornitori.Find(f => f.TenantId == tenant.Id).AnyAsync(ct)) return;
        if (cliniche.Count == 0) return;

        var oggi = DateTime.UtcNow.Date;

        var fornitori = new[]
        {
            new Fornitore { TenantId = tenant.Id, RagioneSociale = "LERETI spa",                    PartitaIva = "01234567890", EmailContatto = "lereti@demo.it",  Iban = "IT42G0569610901000009101X54", CategoriaDefault = CategoriaSpesa.Acqua,            Stato = StatoFornitore.Attivo, TerminiPagamentoGiorni = 30, BasePagamento = BasePagamento.FineMeseFattura },
            new Fornitore { TenantId = tenant.Id, RagioneSociale = "EniMoov S.p.A.",                PartitaIva = "09876543210", EmailContatto = "fatt@enimoov.it", Iban = "IT11A0123412345000000099999",  CategoriaDefault = CategoriaSpesa.Trasporti,        Stato = StatoFornitore.Attivo, TerminiPagamentoGiorni = 30, BasePagamento = BasePagamento.DataFattura },
            new Fornitore { TenantId = tenant.Id, RagioneSociale = "Lyreco Italia srl",             PartitaIva = "11122233344", EmailContatto = "ufficio@lyreco.it",Iban = "IT88B0306904012000000123456",  CategoriaDefault = CategoriaSpesa.Cancelleria,      Stato = StatoFornitore.Attivo, TerminiPagamentoGiorni = 60, BasePagamento = BasePagamento.FineMeseSuccessivo },
            new Fornitore { TenantId = tenant.Id, RagioneSociale = "Q-Print srl",                   PartitaIva = "44455566677", EmailContatto = "info@qprint.it",  Iban = "IT98Y0503450112000000001360",  CategoriaDefault = CategoriaSpesa.AltreSpeseFisse, Stato = StatoFornitore.Attivo, TerminiPagamentoGiorni = 30, BasePagamento = BasePagamento.DataFattura },
            new Fornitore { TenantId = tenant.Id, RagioneSociale = "CVZ Antincendi S.A.S.",         PartitaIva = "55566677788", EmailContatto = "info@cvzantinc.it",Iban = "IT54S0306922800100000069338",  CategoriaDefault = CategoriaSpesa.Manutenzione,    Stato = StatoFornitore.Attivo, TerminiPagamentoGiorni = 30, BasePagamento = BasePagamento.FineMeseFattura },
            new Fornitore { TenantId = tenant.Id, RagioneSociale = "Sapia Pratesi & Partners srl",  PartitaIva = "22233344455", EmailContatto = "studio@sapia.it", Iban = "IT22T0103412345000000088888",  CategoriaDefault = CategoriaSpesa.Consulenze,      Stato = StatoFornitore.Attivo, TerminiPagamentoGiorni = 30, BasePagamento = BasePagamento.FineMeseFattura },
            new Fornitore { TenantId = tenant.Id, RagioneSociale = "Plastigomma s.r.l.",            PartitaIva = "33344455566", EmailContatto = "info@plastigomma.it", Iban = "IT44U0306904012000000099111", CategoriaDefault = CategoriaSpesa.MaterialiClinici, Stato = StatoFornitore.Attivo, TerminiPagamentoGiorni = 30, BasePagamento = BasePagamento.DataFattura }
        };
        await ctx.Fornitori.InsertManyAsync(fornitori, cancellationToken: ct);
        logger.LogInformation("Seeded {Count} fornitori", fornitori.Length);

        // Account portale per LERETI (demo del portale fornitore)
        var lereti = fornitori[0];
        const string portaleEmail = "lereti@demo.it";
        if (!await ctx.Users.Find(u => u.Email == portaleEmail).AnyAsync(ct))
        {
            await ctx.Users.InsertOneAsync(new User
            {
                TenantId = tenant.Id,
                Email = portaleEmail,
                PasswordHash = hasher.Hash("chipdent"),
                FullName = lereti.RagioneSociale,
                Role = UserRole.Fornitore,
                LinkedPersonType = LinkedPersonType.Fornitore,
                LinkedPersonId = lereti.Id,
                IsActive = true
            }, cancellationToken: ct);
            logger.LogInformation("Seeded fornitore portal user {Email}", portaleEmail);
        }

        // Mix di fatture/scadenze: ~40 totali, distribuite tra passato/presente/futuro,
        // con metodi e stati vari, per dare la dashboard piena di dati al primo avvio.
        var rng = new Random(7);
        var fatture = new List<FatturaFornitore>();
        var scadenze = new List<ScadenzaPagamento>();
        var metodi = new[] { MetodoPagamento.Bonifico, MetodoPagamento.Rid, MetodoPagamento.Riba, MetodoPagamento.CartaCredito };

        var counter = 0;
        for (var i = 0; i < 40; i++)
        {
            var f = fornitori[rng.Next(fornitori.Length)];
            var c = cliniche[rng.Next(cliniche.Count)];
            // Distribuzione: 60% nei 12 mesi passati, 40% prossimi 90gg
            var giornoOffset = rng.NextDouble() < 0.6 ? -rng.Next(0, 365) : rng.Next(1, 90);
            var dataEm = oggi.AddDays(giornoOffset - rng.Next(15, 35));
            var dataScad = oggi.AddDays(giornoOffset);
            var imponibile = (decimal)Math.Round(rng.NextDouble() * 2000 + 30, 2);
            var iva = Math.Round(imponibile * 0.22m, 2);
            var totale = imponibile + iva;

            counter++;
            var fattura = new FatturaFornitore
            {
                TenantId = tenant.Id,
                FornitoreId = f.Id,
                ClinicaId = c.Id,
                Numero = $"{dataEm.Year}-FE{1000 + counter}",
                DataEmissione = DateTime.SpecifyKind(dataEm, DateTimeKind.Utc),
                MeseCompetenza = DateTime.SpecifyKind(new DateTime(dataEm.Year, dataEm.Month, 1), DateTimeKind.Utc),
                Categoria = f.CategoriaDefault,
                Imponibile = imponibile,
                Iva = iva,
                Totale = totale,
                Stato = StatoFattura.Approvata,
                ApprovataIl = dataEm.AddDays(2),
                Origine = OrigineFattura.ImportExcel,
                Note = $"Fornitura {f.CategoriaDefault.ToString().ToLowerInvariant()} {dataEm:MMM yy}"
            };
            fatture.Add(fattura);

            var metodo = metodi[rng.Next(metodi.Length)];
            var stato = giornoOffset < -10 ? StatoScadenza.Pagato
                      : giornoOffset < 0 ? (rng.NextDouble() < 0.3 ? StatoScadenza.DaPagare : StatoScadenza.Pagato)
                      : giornoOffset <= 7 ? (rng.NextDouble() < 0.5 ? StatoScadenza.Programmato : StatoScadenza.DaPagare)
                      : StatoScadenza.DaPagare;

            // Calcolo scadenza attesa dai termini del fornitore. Per il 25% delle righe
            // applichiamo una divergenza voluta (±5..15 gg) per dimostrare il warning.
            var attesa = Chipdent.Web.Infrastructure.Sepa.PagamentiHelper
                .CalcolaScadenzaAttesa(dataEm, f.TerminiPagamentoGiorni, f.BasePagamento);
            DateTime dataScadFinale = dataScad;
            if (rng.NextDouble() < 0.25)
            {
                dataScadFinale = attesa.AddDays(rng.Next(-15, -5));   // dichiarata "anticipata" rispetto all'atteso
            }

            scadenze.Add(new ScadenzaPagamento
            {
                TenantId = tenant.Id,
                FatturaId = fattura.Id,
                FornitoreId = f.Id,
                ClinicaId = c.Id,
                Categoria = fattura.Categoria,
                DataScadenza = DateTime.SpecifyKind(dataScadFinale, DateTimeKind.Utc),
                DataScadenzaAttesa = DateTime.SpecifyKind(attesa, DateTimeKind.Utc),
                Importo = totale,
                Metodo = metodo,
                Iban = f.Iban,
                Stato = stato,
                DataPagamento = stato == StatoScadenza.Pagato ? DateTime.SpecifyKind(dataScadFinale.AddDays(rng.Next(-2, 3)), DateTimeKind.Utc) : null,
                DataProgrammata = stato == StatoScadenza.Programmato ? DateTime.SpecifyKind(dataScadFinale, DateTimeKind.Utc) : null,
                RiferimentoPagamento = stato == StatoScadenza.Pagato ? $"CRO-{rng.Next(1000000, 9999999)}" : null
            });
        }

        // Una fattura del portale fornitore "in attesa di approvazione" per mostrare il flusso
        var pending = new FatturaFornitore
        {
            TenantId = tenant.Id,
            FornitoreId = lereti.Id,
            ClinicaId = cliniche[0].Id,
            Numero = $"{oggi.Year}-FE-PORT-001",
            DataEmissione = DateTime.SpecifyKind(oggi.AddDays(-2), DateTimeKind.Utc),
            MeseCompetenza = DateTime.SpecifyKind(new DateTime(oggi.Year, oggi.Month, 1), DateTimeKind.Utc),
            Categoria = CategoriaSpesa.Acqua,
            Imponibile = 84.50m, Iva = 18.59m, Totale = 103.09m,
            Stato = StatoFattura.Caricata,
            Origine = OrigineFattura.PortaleFornitore,
            Note = "Caricata dal portale — bolletta acqua aprile"
        };
        fatture.Add(pending);
        scadenze.Add(new ScadenzaPagamento
        {
            TenantId = tenant.Id,
            FatturaId = pending.Id,
            FornitoreId = lereti.Id,
            ClinicaId = pending.ClinicaId,
            Categoria = pending.Categoria,
            DataScadenza = DateTime.SpecifyKind(oggi.AddDays(28), DateTimeKind.Utc),
            DataScadenzaAttesa = DateTime.SpecifyKind(
                Chipdent.Web.Infrastructure.Sepa.PagamentiHelper.CalcolaScadenzaAttesa(
                    pending.DataEmissione, lereti.TerminiPagamentoGiorni, lereti.BasePagamento),
                DateTimeKind.Utc),
            Importo = pending.Totale,
            Metodo = MetodoPagamento.Bonifico,
            Iban = lereti.Iban,
            Stato = StatoScadenza.DaPagare,
            Note = "Proposta dal fornitore — in attesa di approvazione fattura."
        });

        await ctx.Fatture.InsertManyAsync(fatture, cancellationToken: ct);
        await ctx.ScadenzePagamento.InsertManyAsync(scadenze, cancellationToken: ct);
        logger.LogInformation("Seeded tesoreria: {F} fatture, {S} scadenze", fatture.Count, scadenze.Count);
    }

    /// <summary>
    /// Genera dati storici (12 mesi) per popolare le card AI Insights al primo avvio:
    /// turni passati, timbrature con qualche ritardo anomalo, ferie usate, cambi turno,
    /// un dipendente cessato 6 mesi fa per il forecast organico.
    /// Idempotente: salta se trova già turni storici.
    /// </summary>
    /// <summary>
    /// Seed demo della checklist dipendente: distacchi, RENTRI, protocolli sicurezza,
    /// nuovi corsi formazione (generale + specifica rischio basso/alto + aggiornamento ASO).
    /// Idempotente.
    /// </summary>
    private static async Task SeedChecklistDipendenteAsync(MongoContext ctx, Tenant tenant, List<Clinica> cliniche, ILogger logger, CancellationToken ct)
    {
        if (cliniche.Count < 2) return;

        var oggi = DateTime.UtcNow.Date;
        var milano = cliniche.FirstOrDefault(c => c.Nome == "MILANO7");
        var roma = cliniche.FirstOrDefault(c => c.Nome == "BUSTO A.");
        var torino = cliniche.FirstOrDefault(c => c.Nome == "BRUGHERIO");

        var dipendenti = await ctx.Dipendenti.Find(d => d.TenantId == tenant.Id).ToListAsync(ct);

        // ── Distacchi demo ────────────────────────────────────────
        if (!await ctx.Distacchi.Find(d => d.TenantId == tenant.Id).AnyAsync(ct))
        {
            var giulia = dipendenti.FirstOrDefault(x => x.Email == "g.moretti@confident.it");
            if (giulia is not null && roma is not null)
            {
                await ctx.Distacchi.InsertOneAsync(new DistaccoDipendente
                {
                    TenantId = tenant.Id,
                    DipendenteId = giulia.Id,
                    ClinicaDistaccoId = roma.Id,
                    DataInizio = DateTime.SpecifyKind(oggi.AddDays(-30), DateTimeKind.Utc),
                    DataFine = DateTime.SpecifyKind(oggi.AddDays(30), DateTimeKind.Utc),
                    Motivo = "Copertura ferie collega"
                }, cancellationToken: ct);
            }
            var sara = dipendenti.FirstOrDefault(x => x.Email == "s.conti@confident.it");
            if (sara is not null && milano is not null)
            {
                await ctx.Distacchi.InsertOneAsync(new DistaccoDipendente
                {
                    TenantId = tenant.Id,
                    DipendenteId = sara.Id,
                    ClinicaDistaccoId = milano.Id,
                    DataInizio = DateTime.SpecifyKind(oggi.AddMonths(-9), DateTimeKind.Utc),
                    DataFine = DateTime.SpecifyKind(oggi.AddMonths(-6), DateTimeKind.Utc),
                    Motivo = "Apertura nuova sala"
                }, cancellationToken: ct);
            }
            logger.LogInformation("Seeded distacchi demo");
        }

        // ── RENTRI per Milano e Roma ──────────────────────────────
        if (!await ctx.Rentri.Find(r => r.TenantId == tenant.Id).AnyAsync(ct))
        {
            var rentri = new List<IscrizioneRentri>();
            if (milano is not null)
            {
                rentri.Add(new IscrizioneRentri
                {
                    TenantId = tenant.Id, ClinicaId = milano.Id,
                    DataAttivazione = DateTime.SpecifyKind(oggi.AddMonths(-8), DateTimeKind.Utc),
                    NumeroIscrizione = "RNT-MIL-2024-0142",
                    Username = "milano.confident",
                    Password = "*****"
                });
            }
            if (roma is not null)
            {
                rentri.Add(new IscrizioneRentri
                {
                    TenantId = tenant.Id, ClinicaId = roma.Id,
                    DataAttivazione = DateTime.SpecifyKind(oggi.AddMonths(-3), DateTimeKind.Utc),
                    NumeroIscrizione = "RNT-ROM-2024-0287",
                    Username = "roma.confident",
                    Password = "*****"
                });
            }
            if (rentri.Count > 0) await ctx.Rentri.InsertManyAsync(rentri, cancellationToken: ct);
            logger.LogInformation("Seeded {Count} iscrizioni RENTRI", rentri.Count);
        }

        // ── Protocolli ────────────────────────────────────────────
        if (!await ctx.ProtocolliClinica.Find(p => p.TenantId == tenant.Id).AnyAsync(ct))
        {
            var prot = new List<ProtocolloClinica>();
            foreach (var c in cliniche.Where(x => x.Stato == ClinicaStato.Operativa))
            {
                prot.Add(new ProtocolloClinica
                {
                    TenantId = tenant.Id, ClinicaId = c.Id,
                    Tipo = TipoProtocollo.Sicurezza, Versione = "2.1", Attivo = true,
                    DataAdozione = DateTime.SpecifyKind(oggi.AddMonths(-14), DateTimeKind.Utc),
                    ProssimaRevisione = DateTime.SpecifyKind(oggi.AddMonths(10), DateTimeKind.Utc)
                });
                prot.Add(new ProtocolloClinica
                {
                    TenantId = tenant.Id, ClinicaId = c.Id,
                    Tipo = TipoProtocollo.Legionella, Versione = "1.0", Attivo = true,
                    DataAdozione = DateTime.SpecifyKind(oggi.AddMonths(-6), DateTimeKind.Utc),
                    ProssimaRevisione = DateTime.SpecifyKind(oggi.AddMonths(18), DateTimeKind.Utc)
                });
                prot.Add(new ProtocolloClinica
                {
                    TenantId = tenant.Id, ClinicaId = c.Id,
                    Tipo = TipoProtocollo.SterilizzazioneStrumenti, Versione = "3.0", Attivo = true,
                    DataAdozione = DateTime.SpecifyKind(oggi.AddMonths(-4), DateTimeKind.Utc)
                });
            }
            if (prot.Count > 0) await ctx.ProtocolliClinica.InsertManyAsync(prot, cancellationToken: ct);
            logger.LogInformation("Seeded {Count} protocolli clinica", prot.Count);
        }

        // ── Nuovi tipi di corsi (formazione generale + rischio basso/alto + aggiornamento ASO) ──
        var existingNewCourses = await ctx.Corsi.Find(c => c.TenantId == tenant.Id
            && (c.Tipo == TipoCorso.FormazioneGeneraleSicurezza
                || c.Tipo == TipoCorso.FormazioneSpecificaRischioBasso
                || c.Tipo == TipoCorso.FormazioneSpecificaRischioAltoASO
                || c.Tipo == TipoCorso.AggiornamentoASO10H)).AnyAsync(ct);
        if (!existingNewCourses)
        {
            var corsi = new List<Corso>();
            foreach (var d in dipendenti.Where(x => x.Stato != StatoDipendente.Cessato))
            {
                corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = d.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.FormazioneGeneraleSicurezza, DataConseguimento = DateTime.SpecifyKind(oggi.AddYears(-2), DateTimeKind.Utc) });
                corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = d.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.FormazioneSpecificaRischioBasso, DataConseguimento = DateTime.SpecifyKind(oggi.AddYears(-2), DateTimeKind.Utc), Scadenza = DateTime.SpecifyKind(oggi.AddYears(3), DateTimeKind.Utc) });
                if (d.Ruolo == RuoloDipendente.ASO)
                {
                    corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = d.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.FormazioneSpecificaRischioAltoASO, DataConseguimento = DateTime.SpecifyKind(oggi.AddYears(-2), DateTimeKind.Utc), Scadenza = DateTime.SpecifyKind(oggi.AddYears(3), DateTimeKind.Utc) });
                    corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = d.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.AggiornamentoASO10H, DataConseguimento = DateTime.SpecifyKind(oggi.AddMonths(-7), DateTimeKind.Utc), Scadenza = DateTime.SpecifyKind(oggi.AddMonths(5), DateTimeKind.Utc) });
                }
                corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = d.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.Radioprotezione, DataConseguimento = DateTime.SpecifyKind(oggi.AddYears(-2), DateTimeKind.Utc), Scadenza = DateTime.SpecifyKind(oggi.AddYears(3), DateTimeKind.Utc) });
            }
            // Un RLS con verbale di nomina su Sara
            var saraR = dipendenti.FirstOrDefault(x => x.Email == "s.conti@confident.it");
            if (saraR is not null)
            {
                corsi.Add(new Corso { TenantId = tenant.Id, DestinatarioId = saraR.Id, DestinatarioTipo = DestinatarioCorso.Dipendente, Tipo = TipoCorso.RLS, DataConseguimento = DateTime.SpecifyKind(oggi.AddMonths(-10), DateTimeKind.Utc), Scadenza = DateTime.SpecifyKind(oggi.AddMonths(2), DateTimeKind.Utc), VerbaleNomina = "Verbale 2024/04 del 15/03/2024" });
            }
            if (corsi.Count > 0) await ctx.Corsi.InsertManyAsync(corsi, cancellationToken: ct);
            logger.LogInformation("Seeded {Count} corsi formazione estesi", corsi.Count);
        }
    }

    /// <summary>
    /// Seed demo del modulo Cashflow: saldo cassa, soglia rischio, e 3 entrate attese
    /// (incassi mensili stimati per le sedi). Idempotente.
    /// </summary>
    private static async Task SeedCashflowAsync(MongoContext ctx, Tenant tenant, List<Clinica> cliniche, ILogger logger, CancellationToken ct)
    {
        var existingSettings = await ctx.CashflowSettings.Find(s => s.TenantId == tenant.Id).AnyAsync(ct);
        if (!existingSettings)
        {
            await ctx.CashflowSettings.InsertOneAsync(new CashflowSettings
            {
                TenantId = tenant.Id,
                SaldoCassa = 85_000m,
                SaldoAggiornatoIl = DateTime.UtcNow,
                SogliaRischio = 15_000m,
                Note = "Demo: aggiornato dal seed."
            }, cancellationToken: ct);
            logger.LogInformation("Seeded cashflow settings");
        }

        var existingEntrate = await ctx.EntrateAttese.Find(e => e.TenantId == tenant.Id).AnyAsync(ct);
        if (!existingEntrate && cliniche.Count > 0)
        {
            var oggi = DateTime.UtcNow.Date;
            var primoMese = new DateTime(oggi.Year, oggi.Month, 1);
            var entrate = new List<EntrataAttesa>();
            for (var i = 0; i < 3; i++)
            {
                var mese = DateTime.SpecifyKind(primoMese.AddMonths(i + 1), DateTimeKind.Utc);
                foreach (var c in cliniche)
                {
                    entrate.Add(new EntrataAttesa
                    {
                        TenantId = tenant.Id,
                        DataAttesa = mese,
                        ClinicaId = c.Id,
                        Importo = c.NumeroRiuniti * 4_500m,    // stima grezza per riunito
                        Descrizione = $"Incasso prestazioni {mese:MMM yyyy} · {c.Nome}"
                    });
                }
            }
            await ctx.EntrateAttese.InsertManyAsync(entrate, cancellationToken: ct);
            logger.LogInformation("Seeded {Count} entrate attese", entrate.Count);
        }
    }

    private static async Task SeedHistoricalAiDataAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        var oggi = DateTime.UtcNow.Date;
        var dodiciMesiFa = oggi.AddMonths(-12);

        // Skip se già esistono turni storici (oltre 30 giorni indietro)
        var hasHistory = await ctx.Turni
            .Find(t => t.TenantId == tenant.Id && t.Data < oggi.AddDays(-30))
            .AnyAsync(ct);
        if (hasHistory) return;

        var dipendenti = await ctx.Dipendenti.Find(d => d.TenantId == tenant.Id).ToListAsync(ct);
        if (dipendenti.Count == 0) return;

        var rng = new Random(42); // seed fisso per stabilità

        // ── Turni storici: ~3 turni/settimana per dipendente, mattino/pomeriggio alternati ──
        var turniStorici = new List<Turno>();
        var timbrature = new List<Timbratura>();
        foreach (var d in dipendenti.Where(x => x.Stato != StatoDipendente.Cessato))
        {
            for (var giorno = dodiciMesiFa; giorno < oggi; giorno = giorno.AddDays(1))
            {
                if (giorno.DayOfWeek == DayOfWeek.Saturday || giorno.DayOfWeek == DayOfWeek.Sunday) continue;
                if (rng.NextDouble() > 0.6) continue; // ~3/5 giorni feriali

                var mattina = rng.NextDouble() < 0.6;
                var inizio = mattina ? new TimeSpan(8, 30, 0) : new TimeSpan(14, 0, 0);
                var fine = mattina ? new TimeSpan(13, 0, 0) : new TimeSpan(19, 0, 0);

                var turno = new Turno
                {
                    TenantId = tenant.Id,
                    Data = DateTime.SpecifyKind(giorno, DateTimeKind.Utc),
                    OraInizio = inizio,
                    OraFine = fine,
                    ClinicaId = d.ClinicaId,
                    PersonaId = d.Id,
                    TipoPersona = TipoPersona.Dipendente,
                    CreatedAt = DateTime.SpecifyKind(giorno.AddDays(-7), DateTimeKind.Utc)
                };
                turniStorici.Add(turno);

                // Timbratura: check-in qualche minuto dopo l'inizio (con ritardi mirati per Sara Conti per anomalie)
                var ritardoBase = rng.Next(-2, 5);
                if (d.Email == "s.conti@confident.it" && giorno > oggi.AddDays(-21) && rng.NextDouble() < 0.35)
                {
                    ritardoBase = rng.Next(20, 45); // ritardi anomali recenti
                }
                var inizioTs = giorno.Add(inizio).AddMinutes(ritardoBase);
                var fineTs = giorno.Add(fine).AddMinutes(rng.Next(-5, 8));

                timbrature.Add(new Timbratura
                {
                    TenantId = tenant.Id,
                    DipendenteId = d.Id,
                    ClinicaId = d.ClinicaId,
                    Tipo = TipoTimbratura.CheckIn,
                    Timestamp = DateTime.SpecifyKind(inizioTs, DateTimeKind.Utc),
                    Metodo = MetodoTimbratura.Pin
                });
                timbrature.Add(new Timbratura
                {
                    TenantId = tenant.Id,
                    DipendenteId = d.Id,
                    ClinicaId = d.ClinicaId,
                    Tipo = TipoTimbratura.CheckOut,
                    Timestamp = DateTime.SpecifyKind(fineTs, DateTimeKind.Utc),
                    Metodo = MetodoTimbratura.Pin
                });
            }
        }
        if (turniStorici.Count > 0) await ctx.Turni.InsertManyAsync(turniStorici, cancellationToken: ct);
        if (timbrature.Count > 0) await ctx.Timbrature.InsertManyAsync(timbrature, cancellationToken: ct);
        logger.LogInformation("Seeded {T} turni storici e {Tm} timbrature", turniStorici.Count, timbrature.Count);

        // ── Ferie usate (alcune approvate per generare saldo basso) ──
        var sara = dipendenti.FirstOrDefault(x => x.Email == "s.conti@confident.it");
        if (sara is not null)
        {
            var ferie = new[]
            {
                new RichiestaFerie {
                    TenantId = tenant.Id, DipendenteId = sara.Id, RichiedenteUserId = "system",
                    ClinicaId = sara.ClinicaId, Tipo = TipoAssenza.Ferie,
                    DataInizio = DateTime.SpecifyKind(oggi.AddMonths(-4), DateTimeKind.Utc),
                    DataFine = DateTime.SpecifyKind(oggi.AddMonths(-4).AddDays(7), DateTimeKind.Utc),
                    GiorniRichiesti = 5, Stato = StatoRichiestaFerie.Approvata, SaldoApplicato = true,
                    DecisoreIl = oggi.AddMonths(-4).AddDays(-3),
                    CreatedAt = DateTime.SpecifyKind(oggi.AddMonths(-4).AddDays(-7), DateTimeKind.Utc)
                },
                new RichiestaFerie {
                    TenantId = tenant.Id, DipendenteId = sara.Id, RichiedenteUserId = "system",
                    ClinicaId = sara.ClinicaId, Tipo = TipoAssenza.Ferie,
                    DataInizio = DateTime.SpecifyKind(oggi.AddMonths(-2), DateTimeKind.Utc),
                    DataFine = DateTime.SpecifyKind(oggi.AddMonths(-2).AddDays(10), DateTimeKind.Utc),
                    GiorniRichiesti = 8, Stato = StatoRichiestaFerie.Approvata, SaldoApplicato = true,
                    DecisoreIl = oggi.AddMonths(-2).AddDays(-2),
                    CreatedAt = DateTime.SpecifyKind(oggi.AddMonths(-2).AddDays(-5), DateTimeKind.Utc)
                }
            };
            await ctx.RichiesteFerie.InsertManyAsync(ferie, cancellationToken: ct);

            // Riduco il saldo ferie di Sara per generare segnale di rischio
            await ctx.Dipendenti.UpdateOneAsync(
                d => d.Id == sara.Id,
                Builders<Dipendente>.Update.Set(d => d.GiorniFerieResidui, 2));

            // Cambi turno frequenti per Sara (ultimi 90g) → segnale di rischio
            var cambi = new List<RichiestaCambioTurno>();
            for (var i = 0; i < 4; i++)
            {
                var d = oggi.AddDays(-rng.Next(10, 80));
                cambi.Add(new RichiestaCambioTurno
                {
                    TenantId = tenant.Id,
                    TurnoId = "historical",
                    ClinicaId = sara.ClinicaId,
                    RichiedenteUserId = "system",
                    RichiedenteNome = sara.NomeCompleto,
                    TipoPersona = TipoPersona.Dipendente,
                    PersonaIdRichiedente = sara.Id,
                    Stato = i % 2 == 0 ? StatoCambioTurno.ApprovataDirettore : StatoCambioTurno.RifiutataDaCollega,
                    NoteRichiesta = "Esigenza personale",
                    CreatedAt = DateTime.SpecifyKind(d, DateTimeKind.Utc)
                });
            }
            await ctx.RichiesteCambioTurno.InsertManyAsync(cambi, cancellationToken: ct);
        }

        // ── Dipendente cessato 6 mesi fa, per il forecast organico (segnale di trend) ──
        var milanoId = dipendenti.FirstOrDefault(d => d.ClinicaId != null)?.ClinicaId;
        if (!string.IsNullOrEmpty(milanoId))
        {
            var cessato = new Dipendente
            {
                TenantId = tenant.Id,
                Nome = "Roberta",
                Cognome = "Vianello",
                Email = "r.vianello@confident.it",
                Ruolo = RuoloDipendente.ASO,
                ClinicaId = milanoId,
                DataAssunzione = DateTime.SpecifyKind(oggi.AddYears(-2), DateTimeKind.Utc),
                DataDimissioni = DateTime.SpecifyKind(oggi.AddMonths(-6), DateTimeKind.Utc),
                MotivoDimissioni = "Trasferimento fuori regione",
                Stato = StatoDipendente.Cessato
            };
            await ctx.Dipendenti.InsertOneAsync(cessato, cancellationToken: ct);

            // Nuovo assunto 2 mesi fa per controbilanciare il trend
            var assunto = new Dipendente
            {
                TenantId = tenant.Id,
                Nome = "Davide",
                Cognome = "Romano",
                Email = "d.romano@confident.it",
                Ruolo = RuoloDipendente.ASO,
                ClinicaId = milanoId,
                DataAssunzione = DateTime.SpecifyKind(oggi.AddMonths(-2), DateTimeKind.Utc),
                Stato = StatoDipendente.Onboarding,
                GiorniFerieResidui = 22
            };
            await ctx.Dipendenti.InsertOneAsync(assunto, cancellationToken: ct);
        }

        logger.LogInformation("Seeded historical AI demo data");
    }

    /// <summary>
    /// Migra dati pre-refactor ruoli (Operatore/HR/Manager/Admin) al nuovo
    /// modello (Staff/Backoffice/Direttore/Management). Idempotente.
    /// </summary>
    private static async Task MigrateLegacyRolesAsync(MongoContext ctx, ILogger logger, CancellationToken ct)
    {
        try
        {
            // Rinomina tenant "Confident Dental" → "Confident" (una tantum, idempotente).
            await ctx.Tenants.UpdateManyAsync(
                t => t.DisplayName == "Confident Dental",
                Builders<Tenant>.Update.Set(t => t.DisplayName, "Confident"),
                cancellationToken: ct);

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
