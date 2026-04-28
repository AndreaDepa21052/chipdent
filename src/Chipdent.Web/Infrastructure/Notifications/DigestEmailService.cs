using System.Text;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Notifications;

/// <summary>
/// Background service che invia un digest giornaliero per tenant
/// alle 7:00 (server time) agli utenti con DigestEmail attivo.
/// Riassume: scadenze imminenti, alert RLS, richieste in attesa.
/// </summary>
public class DigestEmailService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<DigestEmailService> _log;
    private static readonly TimeSpan DigestTime = new(7, 0, 0);

    public DigestEmailService(IServiceProvider sp, ILogger<DigestEmailService> log)
    {
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.Add(DigestTime);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            var wait = nextRun - now;

            try { await Task.Delay(wait, stoppingToken); }
            catch (TaskCanceledException) { return; }

            try
            {
                await using var scope = _sp.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<MongoContext>();
                var mailer = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await SendDigestsAsync(ctx, mailer, stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Digest run failed");
            }
        }
    }

    private async Task SendDigestsAsync(MongoContext ctx, IEmailSender mailer, CancellationToken ct)
    {
        var tenants = await ctx.Tenants.Find(t => t.IsActive).ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            // Utenti che hanno il digest abilitato
            var users = await ctx.Users.Find(u => u.TenantId == tenant.Id && u.IsActive).ToListAsync(ct);
            var withDigest = users.Where(u => u.Preferences?.DigestEmail == true).ToList();
            if (!withDigest.Any()) continue;

            // Aggregati da includere nel digest
            var soon = DateTime.UtcNow.AddDays(30);
            var docsInScad = await ctx.DocumentiClinica
                .Find(d => d.TenantId == tenant.Id && d.DataScadenza != null && d.DataScadenza < soon)
                .ToListAsync(ct);
            var visiteInScad = await ctx.VisiteMediche
                .Find(v => v.TenantId == tenant.Id && v.ScadenzaIdoneita != null && v.ScadenzaIdoneita < soon)
                .ToListAsync(ct);
            var ferieInAttesa = await ctx.RichiesteFerie
                .Find(r => r.TenantId == tenant.Id && r.Stato == StatoRichiestaFerie.InAttesa)
                .ToListAsync(ct);

            foreach (var u in withDigest)
            {
                var html = BuildDigestHtml(tenant.DisplayName, docsInScad.Count, visiteInScad.Count, ferieInAttesa.Count);
                await mailer.SendAsync(new EmailMessage(
                    To: u.Email,
                    Subject: $"Chipdent · digest del {DateTime.Today:dd/MM/yyyy}",
                    HtmlBody: html), ct);
            }
            _log.LogInformation("Sent digest to {Count} users for tenant {Tenant}", withDigest.Count, tenant.Slug);
        }
    }

    private static string BuildDigestHtml(string tenantName, int docs, int visite, int ferie)
    {
        var sb = new StringBuilder();
        sb.Append("<div style='font-family:Inter,sans-serif;color:#2a1508;'>");
        sb.Append($"<h2 style='font-family:Playfair Display,serif;color:#3b1e0c;'>Buongiorno · {tenantName}</h2>");
        sb.Append("<p>Ecco il digest delle cose che richiedono attenzione oggi:</p>");
        sb.Append("<ul style='line-height:1.8;'>");
        sb.Append($"<li><strong>{docs}</strong> document{(docs == 1 ? "o" : "i")} in scadenza nei prossimi 30 giorni</li>");
        sb.Append($"<li><strong>{visite}</strong> visit{(visite == 1 ? "a medica" : "e mediche")} in scadenza</li>");
        sb.Append($"<li><strong>{ferie}</strong> richiest{(ferie == 1 ? "a" : "e")} di ferie in attesa di approvazione</li>");
        sb.Append("</ul>");
        sb.Append("<p style='margin-top:24px;color:#7a5c3a;font-size:13px;'>Apri Chipdent per i dettagli. Puoi disattivare il digest dalle Preferenze.</p>");
        sb.Append("</div>");
        return sb.ToString();
    }
}
