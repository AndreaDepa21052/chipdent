using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Chipdent.Web.Infrastructure.Identity;

public static class Policies
{
    public const string RequireOwner   = nameof(RequireOwner);
    public const string RequireAdmin   = nameof(RequireAdmin);
    public const string RequireManager = nameof(RequireManager);
    public const string RequireHR      = nameof(RequireHR);

    /// <summary>Roles that have full read access across the tenant.</summary>
    public const string FullAccessRoles = "Owner,Admin";

    /// <summary>Roles that can read anagrafica/compliance data.</summary>
    public const string StaffRoles = "Owner,Admin,Manager,HR";

    public static void Configure(AuthorizationOptions o)
    {
        o.AddPolicy(RequireOwner,   p => p.RequireRole("Owner"));
        o.AddPolicy(RequireAdmin,   p => p.RequireRole("Owner", "Admin"));
        o.AddPolicy(RequireManager, p => p.RequireRole("Owner", "Admin", "Manager"));
        o.AddPolicy(RequireHR,      p => p.RequireRole("Owner", "Admin", "Manager", "HR"));
    }
}

public static class UserAccess
{
    public static bool IsFullAccess(this ClaimsPrincipal? user) =>
        user is not null && (user.IsInRole("Owner") || user.IsInRole("Admin"));

    public static bool CanSeeAnagrafiche(this ClaimsPrincipal? user) =>
        user is not null && (user.IsInRole("Owner") || user.IsInRole("Admin")
                             || user.IsInRole("Manager") || user.IsInRole("HR"));

    public static bool CanManageUsers(this ClaimsPrincipal? user) =>
        user is not null && (user.IsInRole("Owner") || user.IsInRole("Admin"));

    public static bool CanApprove(this ClaimsPrincipal? user) =>
        user is not null && (user.IsInRole("Owner") || user.IsInRole("Admin")
                             || user.IsInRole("Manager"));
}
