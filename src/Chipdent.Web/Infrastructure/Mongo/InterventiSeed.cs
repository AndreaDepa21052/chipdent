using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

internal static class InterventiSeed
{
    /// <summary>
    /// Inserisce il calendario interventi reale (registro antincendio, manutenzioni elettriche,
    /// radiografico, bombole O₂, contratti smaltimento rifiuti, RENTRI…) dal file fornito da Confident.
    /// Idempotente: salta se trova già righe per il tenant.
    /// </summary>
    public static async Task SeedAsync(MongoContext ctx, Tenant tenant, List<Clinica> cliniche, ILogger logger, CancellationToken ct)
    {
        if (cliniche.Count == 0) return;
        if (await ctx.InterventiClinica.Find(i => i.TenantId == tenant.Id).AnyAsync(ct)) return;

        var byNome = cliniche.ToDictionary(c => c.Nome, c => c.Id);

        // Helper: ritorna l'id se la clinica esiste in mappa, altrimenti null (riga skippata).
        string? ResolveId(string nome) => byNome.TryGetValue(nome, out var id) ? id : null;

        var interventi = new List<InterventoClinica>
        {
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 09/02/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 12/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 20/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 09/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 19/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/03/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/03/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato l' 08/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 10/03/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 10/03/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 10/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/03/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 16/12/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.RegistroAntincendio, Fornitore = "CVZ Antincendi S.r.l.", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 08/4/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 26/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 23/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 26/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 26/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 23/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 22/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 29/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 22/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il  27/4/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 26/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 22/04/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 29/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 29/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.PuliziaFiltriCondizionatori, Fornitore = "Manutentore condizionatori", Frequenza = "a 6 mesi", DataUltimoIntervento = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Effettuato il 22/4/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2024, 10, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 05/10/2024", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 25/06/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 20/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2024, 5, 22, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il  22/05/2024", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2024, 11, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il  20/11/2024", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 15/02/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/06/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 30/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Programmato per il 7/4/2026 ore 9,00", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.MessaATerra, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 10, 5, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 27/10/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 20/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 5, 22, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Programmato per il 19/5/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 11, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/11/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 10/02/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/06/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 30/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 5, 5, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Ricordare a Motta di fare controllo DAE maggio 2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/09/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 19/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 7/4/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Programmato per il 4/5/2026 ore 14,00", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.ImpiantoElettricoAnnuale, Fornitore = "Motta Impianti", Frequenza = "annuale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 27/10/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 07/01/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 5, 22, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 11, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 11, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 10/02/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 03/09/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 19/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.ElettromedicaliBiennale, Fornitore = "Motta Impianti", Frequenza = "biennale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 5, 24, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Programmato per il 19/05/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 11, 18, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 24/11/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 6, 21, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Programmato per il 26/05/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 11, 18, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 10/11/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 16/02/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Programmato per il 26/05/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 02/12/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Programmato per il 19/05/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 28/08/2025", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 20/01/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = true, Note = "Effettuato il 16/03/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Programmato per il 15/06/2026", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.Radiografico, Fornitore = "Esperto Qualificato Radioprotezione", Frequenza = "annuale", DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 7, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.BombolaOssigeno, Fornitore = "SOL Group", Frequenza = null, DataUltimoIntervento = null, ProssimaScadenza = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.Nolomedical, Fornitore = "Nolomedical s.r.l.", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Rinnovato", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.Nolomedical, Fornitore = "Nolomedical s.r.l.", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Rinnovato", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.Nolomedical, Fornitore = "Nolomedical s.r.l.", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Rinnovato", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.Nolomedical, Fornitore = "Nolomedical s.r.l.", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "Da rinnovare", Dettagli = new() },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "5 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.276,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "5 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.276,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "5 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.276,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "5 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.276,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "5 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.276,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteContratto, Fornitore = "Ecologia Ambiente", Frequenza = "biennale", DataUltimoIntervento = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2028, 12, 31, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "disdetta via PEC entro 90 gg dalla scadenza (ago-2028)", Dettagli = new()
                {
                    ["Contenitori forniti"] = "3 da 40lt",
                    ["Rifiuti sanitari"] = "€ 1.025,00 + IVA",
                    ["Contenitore supplementare"] = "€ 11,90 + IVA",
                    ["Amalgama"] = "omaggio",
                    ["Compilazione MUD"] = "omaggio",
                    ["Registro carico/scarico"] = "omaggio",
                    ["Autoclave"] = "omaggio",
                    ["Smaltimento farmaci"] = "omaggio",
                    ["Ritiro toner"] = "omaggio"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("DESIO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335254",
                    ["Numero iscrizione"] = "OP2601RGF335254-MB0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "675846",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("VARESE") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335327",
                    ["Numero iscrizione"] = "OP2601YKF335327-VA0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "150269",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("GIUSSANO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335400",
                    ["Numero iscrizione"] = "OP2601LUE335400-MB0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "829065",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("CORMANO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335509",
                    ["Numero iscrizione"] = "OP2601NTS335509-MI0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "984102",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335570",
                    ["Numero iscrizione"] = "OP2601HSQ335570-CO0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "206615",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO7") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335650",
                    ["Numero iscrizione"] = "OP2601S7V335650-MI0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "817270",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO9") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335700",
                    ["Numero iscrizione"] = "OP2601PBF335700-MI0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "921854",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("SGM") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335778",
                    ["Numero iscrizione"] = "OP2601NQA335778-MI0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "983040",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BUSTO A.") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335853",
                    ["Numero iscrizione"] = "OP26015L6335853-VA0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "575745",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BOLLATE") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00335922",
                    ["Numero iscrizione"] = "OP2601XXT335922-MI0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "485440",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO6") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = "18/02 Manuel conferma a voce perché non riesce ad accedere all'e-mail", Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00336004",
                    ["Numero iscrizione"] = "OP2601NLA336004-MI0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "986995",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("MILANO3") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00336074",
                    ["Numero iscrizione"] = "OP2601CY7336074-MI0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "882352",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("BRUGHERIO") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-260120-00336232",
                    ["Numero iscrizione"] = "OP2601C89336232-MB0001",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "783570",
                    ["App scaricata"] = "Sì"
                } },
                new InterventoClinica { TenantId = tenant.Id, ClinicaId = ResolveId("COMASINA") ?? string.Empty, Tipo = TipoIntervento.EcologiaAmbienteRentri, Fornitore = "Ecologia Ambiente / RENTRI", Frequenza = "annuale", DataUltimoIntervento = new DateTime(2025, 9, 10, 0, 0, 0, DateTimeKind.Utc), ProssimaScadenza = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc), ArchiviatoFaldoneAts = false, Note = null, Dettagli = new()
                {
                    ["Codice identificativo"] = "01-250910-00249547",
                    ["Numero iscrizione"] = "OP2509NDW249547",
                    ["Diritto segreteria (€)"] = "10",
                    ["Contributo annuale (€)"] = "15",
                    ["PIN App FIR"] = "682624",
                    ["App scaricata"] = "Sì"
                } },
        };

        // Filtro safety: rimuove eventuali righe per cliniche assenti (in caso di rete custom).
        interventi = interventi.Where(i => !string.IsNullOrEmpty(i.ClinicaId)).ToList();

        if (interventi.Count > 0)
        {
            await ctx.InterventiClinica.InsertManyAsync(interventi, cancellationToken: ct);
            logger.LogInformation("Seeded {Count} interventi clinica", interventi.Count);
        }
    }
}
