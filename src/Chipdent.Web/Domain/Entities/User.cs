using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class User : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Operatore;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public LinkedPersonType LinkedPersonType { get; set; } = LinkedPersonType.None;
    public string? LinkedPersonId { get; set; }
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
