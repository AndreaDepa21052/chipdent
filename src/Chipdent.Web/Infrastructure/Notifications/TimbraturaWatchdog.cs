using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Notifications;

/// <summary>
/// Background service che alle 22:00 di ogni giorno (server time) controlla, per ciascun
/// tenant, i dipendenti che hanno fatto check-in ma non hanno mai timbrato il check-out:
/// emette un evento SignalR "activity" + log per ricordarli al direttore.
/// </summary>
public class TimbraturaWatchdog : BackgroundService
{
    private static readonly TimeSpan RunAt = new(22, 0, 0);
    private readonly IServiceProvider _sp;
    private readonly ILogger<TimbraturaWatchdog> _log;

    public TimbraturaWatchdog(IServiceProvider sp, ILogger<TimbraturaWatchdog> log)
    {
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.Add(RunAt);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);
            try { await Task.Delay(nextRun - now, stoppingToken); }
            catch (TaskCanceledException) { return; }

            try { await ScanAsync(stoppingToken); }
            catch (Exception ex) { _log.LogWarning(ex, "TimbraturaWatchdog scan failed"); }
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        await using var scope = _sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MongoContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();

        var oggi = DateTime.UtcNow.Date;
        var domani = oggi.AddDays(1);

        var tenants = await ctx.Tenants.Find(t => t.IsActive).ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            var timb = await ctx.Timbrature
                .Find(x => x.TenantId == tenant.Id && x.Timestamp >= oggi && x.Timestamp < domani)
                .SortBy(x => x.Timestamp).ToListAsync(ct);
            if (timb.Count == 0) continue;

            var dipMap = (await ctx.Dipendenti.Find(d => d.TenantId == tenant.Id).ToListAsync(ct))
                .ToDictionary(d => d.Id, d => d.NomeCompleto);

            // Per ciascun dipendente: ultima timbratura del giorno deve essere CheckOut
            var aperti = timb.GroupBy(x => x.DipendenteId)
                .Where(g => g.Last().Tipo != TipoTimbratura.CheckOut)
                .Select(g => (Id: g.Key, Nome: dipMap.GetValueOrDefault(g.Key, "—")))
                .ToList();

            if (aperti.Count == 0) continue;

            await publisher.PublishAsync(tenant.Id, "activity", new
            {
                kind = "shift",
                title = $"⚠ {aperti.Count} dipendent{(aperti.Count == 1 ? "e" : "i")} senza check-out",
                description = string.Join(", ", aperti.Take(5).Select(a => a.Nome))
                              + (aperti.Count > 5 ? $" e altri {aperti.Count - 5}" : ""),
                when = DateTime.UtcNow
            });

            _log.LogInformation("Watchdog timbrature {Tenant}: {Count} aperti", tenant.Slug, aperti.Count);
        }
    }
}
