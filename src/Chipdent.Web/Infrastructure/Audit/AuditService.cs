using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;

namespace Chipdent.Web.Infrastructure.Audit;

public class AuditService : IAuditService
{
    private static readonly HashSet<string> AlwaysIgnore = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Domain.Common.Entity.Id),
        nameof(Domain.Common.Entity.CreatedAt),
        nameof(Domain.Common.Entity.UpdatedAt),
        nameof(Domain.Common.TenantEntity.TenantId),
        "PasswordHash"
    };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;
    private readonly ILogger<AuditService> _logger;

    public AuditService(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher, ILogger<AuditService> logger)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task LogAsync(string entityType, string entityId, string entityLabel,
                               AuditAction action,
                               IEnumerable<FieldChange>? changes = null,
                               string? note = null,
                               ClaimsPrincipal? actor = null)
    {
        if (string.IsNullOrEmpty(_tenant.TenantId)) return;

        var userId = actor?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var userName = actor?.Identity?.Name ?? "system";

        var entry = new AuditEntry
        {
            TenantId = _tenant.TenantId!,
            EntityType = entityType,
            EntityId = entityId,
            EntityLabel = entityLabel,
            Action = action,
            UserId = userId,
            UserName = userName,
            Changes = changes?.ToList() ?? new List<FieldChange>(),
            Note = note
        };

        try
        {
            await _mongo.Audit.InsertOneAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit insert failed for {Type}/{Id}", entityType, entityId);
            return;
        }

        _ = _publisher.PublishAsync(_tenant.TenantId!, "audit", new
        {
            id = entry.Id,
            entityType, entityId, entityLabel,
            action = action.ToString(),
            user = userName,
            when = entry.CreatedAt,
            changeCount = entry.Changes.Count,
            note
        });

        _ = _publisher.PublishAsync(_tenant.TenantId!, "activity", new
        {
            kind = "audit",
            title = $"{action} · {entityLabel}",
            description = $"da {userName}",
            when = entry.CreatedAt
        });
    }

    public Task LogDiffAsync<T>(T? oldState, T newState,
                                string entityType, string entityLabel,
                                AuditAction action,
                                ClaimsPrincipal? actor = null,
                                string? note = null,
                                params string[] ignoreFields) where T : Domain.Common.Entity
    {
        var ignored = new HashSet<string>(AlwaysIgnore, StringComparer.OrdinalIgnoreCase);
        foreach (var f in ignoreFields) ignored.Add(f);

        var changes = oldState is null
            ? new List<FieldChange>()
            : Diff(oldState, newState, ignored);

        return LogAsync(entityType, newState.Id, entityLabel, action, changes, note, actor);
    }

    private static List<FieldChange> Diff<T>(T oldState, T newState, HashSet<string> ignored)
    {
        var props = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                              .Where(p => p.CanRead && !ignored.Contains(p.Name));

        var list = new List<FieldChange>();
        foreach (var p in props)
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var oldV = p.GetValue(oldState);
            var newV = p.GetValue(newState);
            if (Equal(oldV, newV)) continue;
            list.Add(new FieldChange
            {
                Field = p.Name,
                OldValue = Format(oldV),
                NewValue = Format(newV)
            });
        }
        return list;
    }

    private static bool Equal(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is System.Collections.IEnumerable ea && a is not string && b is System.Collections.IEnumerable eb)
        {
            var listA = ea.Cast<object?>().Select(Format).ToList();
            var listB = eb.Cast<object?>().Select(Format).ToList();
            return listA.SequenceEqual(listB);
        }
        return a.Equals(b);
    }

    private static string? Format(object? v)
    {
        if (v is null) return null;
        return v switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            System.Collections.IEnumerable e and not string =>
                string.Join(", ", e.Cast<object?>().Select(x => x?.ToString() ?? "")),
            _ => v.ToString()
        };
    }
}
