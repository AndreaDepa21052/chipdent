using Microsoft.AspNetCore.Authorization;

namespace Chipdent.Web.Infrastructure.Identity;

public static class Policies
{
    public const string RequireOwner   = nameof(RequireOwner);
    public const string RequireAdmin   = nameof(RequireAdmin);
    public const string RequireManager = nameof(RequireManager);
    public const string RequireHR      = nameof(RequireHR);

    public static void Configure(AuthorizationOptions o)
    {
        o.AddPolicy(RequireOwner,   p => p.RequireRole("Owner"));
        o.AddPolicy(RequireAdmin,   p => p.RequireRole("Owner", "Admin"));
        o.AddPolicy(RequireManager, p => p.RequireRole("Owner", "Admin", "Manager"));
        o.AddPolicy(RequireHR,      p => p.RequireRole("Owner", "Admin", "Manager", "HR"));
    }
}
