using System.Security.Claims;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Chipdent.Web.Hubs;

/// <summary>
/// Hub realtime per la chat: DM 1:1 e gruppi sede.
/// I client si registrano nei rispettivi gruppi su <c>OnConnectedAsync</c> e ricevono
/// messaggi via il method <c>NuovoMessaggio</c>. La persistenza è gestita lato controller.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = Context.User?.FindFirst(TenantResolverMiddleware.TenantIdClaim)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(userId));
        }
        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-tenant:{tenantId}");
        }
        await base.OnConnectedAsync();
    }

    /// <summary>Iscrive il client al gruppo della sede (chiamato dal client dopo l'apertura del thread).</summary>
    public Task JoinClinica(string clinicaId)
    {
        if (string.IsNullOrEmpty(clinicaId)) return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupForClinica(clinicaId));
    }

    public Task LeaveClinica(string clinicaId)
    {
        if (string.IsNullOrEmpty(clinicaId)) return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForClinica(clinicaId));
    }

    public static string GroupForUser(string userId) => $"chat-user:{userId}";
    public static string GroupForClinica(string clinicaId) => $"chat-clinica:{clinicaId}";
}

public interface IChatPublisher
{
    Task PublishDirectAsync(string fromUserId, string toUserId, object payload);
    Task PublishToClinicaAsync(string clinicaId, object payload);
}

public class ChatPublisher : IChatPublisher
{
    private readonly IHubContext<ChatHub> _hub;
    public ChatPublisher(IHubContext<ChatHub> hub) => _hub = hub;

    public Task PublishDirectAsync(string fromUserId, string toUserId, object payload)
    {
        var groups = new[] { ChatHub.GroupForUser(fromUserId), ChatHub.GroupForUser(toUserId) };
        return _hub.Clients.Groups(groups).SendAsync("NuovoMessaggio", payload);
    }

    public Task PublishToClinicaAsync(string clinicaId, object payload)
        => _hub.Clients.Group(ChatHub.GroupForClinica(clinicaId)).SendAsync("NuovoMessaggio", payload);
}
