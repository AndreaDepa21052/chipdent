using System.Collections.Concurrent;
using System.Security.Claims;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chipdent.Web.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
    private readonly IUserPresenceTracker _presence;

    public NotificationsHub(IUserPresenceTracker presence) => _presence = presence;

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst(TenantResolverMiddleware.TenantIdClaim)?.Value;
        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForTenant(tenantId));

            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var becameOnline = _presence.Add(tenantId, userId, Context.ConnectionId);
                if (becameOnline)
                {
                    var fullName = Context.User?.FindFirst(ClaimTypes.Name)?.Value
                                   ?? Context.User?.Identity?.Name
                                   ?? "Utente";
                    var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
                    await Clients.OthersInGroup(GroupForTenant(tenantId)).SendAsync("user-connected", new
                    {
                        userId,
                        fullName,
                        role,
                        when = DateTime.UtcNow
                    });
                }
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tenantId = Context.User?.FindFirst(TenantResolverMiddleware.TenantIdClaim)?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(userId))
        {
            _presence.Remove(tenantId, userId, Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public static string GroupForTenant(string tenantId) => $"tenant:{tenantId}";
}

/// <summary>
/// Traccia connessioni SignalR per (tenant, user) per emettere "user-connected" una volta sola
/// quando un utente passa da offline a online (e non per ogni tab/refresh).
/// </summary>
public interface IUserPresenceTracker
{
    /// <summary>true se questa è la prima connessione attiva dell'utente nel tenant.</summary>
    bool Add(string tenantId, string userId, string connectionId);
    /// <summary>true se l'utente non ha più connessioni attive nel tenant.</summary>
    bool Remove(string tenantId, string userId, string connectionId);
}

public class UserPresenceTracker : IUserPresenceTracker
{
    // tenantId → userId → set di connectionId
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HashSet<string>>> _byTenant = new();

    public bool Add(string tenantId, string userId, string connectionId)
    {
        var users = _byTenant.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, HashSet<string>>());
        var firstForUser = false;
        users.AddOrUpdate(userId,
            _ => { firstForUser = true; return new HashSet<string> { connectionId }; },
            (_, set) =>
            {
                lock (set)
                {
                    if (set.Count == 0) firstForUser = true;
                    set.Add(connectionId);
                }
                return set;
            });
        return firstForUser;
    }

    public bool Remove(string tenantId, string userId, string connectionId)
    {
        if (!_byTenant.TryGetValue(tenantId, out var users)) return false;
        if (!users.TryGetValue(userId, out var set)) return false;
        bool wentOffline;
        lock (set)
        {
            set.Remove(connectionId);
            wentOffline = set.Count == 0;
        }
        if (wentOffline) users.TryRemove(userId, out _);
        return wentOffline;
    }
}

public interface INotificationPublisher
{
    Task PublishAsync(string tenantId, string channel, object payload);
}

public class NotificationPublisher : INotificationPublisher
{
    private readonly IHubContext<NotificationsHub> _hub;

    public NotificationPublisher(IHubContext<NotificationsHub> hub) => _hub = hub;

    public Task PublishAsync(string tenantId, string channel, object payload)
        => _hub.Clients.Group(NotificationsHub.GroupForTenant(tenantId))
                       .SendAsync(channel, payload);
}
