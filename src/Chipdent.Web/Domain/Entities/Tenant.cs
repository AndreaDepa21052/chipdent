using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Tenant : Entity
{
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string PrimaryColor { get; set; } = "#c47830";
    public bool IsActive { get; set; } = true;
}
