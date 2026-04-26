using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class User : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public UserRole Role { get; set; } = UserRole.Operatore;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public LinkedPersonType LinkedPersonType { get; set; } = LinkedPersonType.None;
    public string? LinkedPersonId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
}

public class UserPreferences
{
    public bool DigestEmail { get; set; } = true;
    public bool NotificheInApp { get; set; } = true;
    public bool SuoniNotifiche { get; set; } = false;
    public bool MostraToast { get; set; } = true;
    public string Lingua { get; set; } = "it";
    public string Densita { get; set; } = "comoda";   // comoda | compatta
}

public enum UserRole
{
    Operatore = 0,
    HR = 10,
    Manager = 20,
    Admin = 30,
    Owner = 99
}

public enum LinkedPersonType
{
    None,
    Dottore,
    Dipendente
}
