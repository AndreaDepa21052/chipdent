using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chipdent.Web.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst(TenantResolverMiddleware.TenantIdClaim)?.Value;
        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForTenant(tenantId));
        }
        await base.OnConnectedAsync();
    }

    public static string GroupForTenant(string tenantId) => $"tenant:{tenantId}";
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
