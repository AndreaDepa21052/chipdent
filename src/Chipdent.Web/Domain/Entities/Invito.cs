using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Invito : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Ruolo { get; set; } = UserRole.Staff;
    public List<string> ClinicaIds { get; set; } = new();
    public string Token { get; set; } = string.Empty;
    public DateTime ScadeIl { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime? UsatoIl { get; set; }
    public string InvitatoDaUserId { get; set; } = string.Empty;

    public bool IsValido => UsatoIl is null && ScadeIl > DateTime.UtcNow;
}
