namespace Chipdent.Web.Infrastructure.Tenancy;

public interface ITenantContext
{
    string? TenantId { get; }
    string? TenantSlug { get; }
    bool HasTenant { get; }

    /// <summary>
    /// Cliniche assegnate all'utente corrente. Vuoto = visibilità su tutte le sedi del tenant
    /// (Management/Owner). Per i Direttori contiene 1+ cliniche.
    /// </summary>
    IReadOnlyList<string> ClinicaIds { get; }

    /// <summary>True se l'utente è limitato a un sottoinsieme di cliniche.</summary>
    bool IsClinicaScoped { get; }

    /// <summary>True se l'utente può accedere alla clinica indicata.</summary>
    bool CanAccessClinica(string? clinicaId);

    void Set(string tenantId, string tenantSlug, IEnumerable<string>? clinicaIds = null);
}

public class TenantContext : ITenantContext
{
    private static readonly IReadOnlyList<string> Empty = Array.Empty<string>();

    public string? TenantId { get; private set; }
    public string? TenantSlug { get; private set; }
    public IReadOnlyList<string> ClinicaIds { get; private set; } = Empty;
    public bool HasTenant => !string.IsNullOrEmpty(TenantId);
    public bool IsClinicaScoped => ClinicaIds.Count > 0;

    public bool CanAccessClinica(string? clinicaId)
    {
        if (!IsClinicaScoped) return true;
        return !string.IsNullOrEmpty(clinicaId) && ClinicaIds.Contains(clinicaId);
    }

    public void Set(string tenantId, string tenantSlug, IEnumerable<string>? clinicaIds = null)
    {
        TenantId = tenantId;
        TenantSlug = tenantSlug;
        ClinicaIds = clinicaIds is null
            ? Empty
            : clinicaIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
    }
}
