using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class AuditEntry : TenantEntity
{
    public string EntityType { get; set; } = string.Empty;   // "Dottore", "Dipendente", "Clinica", "User", ...
    public string EntityId { get; set; } = string.Empty;
    public string EntityLabel { get; set; } = string.Empty;  // human-readable identifier (es. "Dr. Marco Bianchi")
    public AuditAction Action { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;

    public List<FieldChange> Changes { get; set; } = new();

    public string? Note { get; set; }
}

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    Transferred,
    Dismissed,
    Reactivated,
    Linked,
    Unlinked,
    RoleChanged,
    Activated,
    Deactivated
}

public class FieldChange
{
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
