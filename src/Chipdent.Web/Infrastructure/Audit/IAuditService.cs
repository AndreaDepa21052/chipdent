using System.Security.Claims;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Infrastructure.Audit;

public interface IAuditService
{
    Task LogAsync(string entityType, string entityId, string entityLabel, AuditAction action,
                  IEnumerable<FieldChange>? changes = null, string? note = null,
                  ClaimsPrincipal? actor = null);

    Task LogDiffAsync<T>(T? oldState, T newState, string entityType, string entityLabel,
                        AuditAction action, ClaimsPrincipal? actor = null, string? note = null,
                        params string[] ignoreFields) where T : Domain.Common.Entity;
}
