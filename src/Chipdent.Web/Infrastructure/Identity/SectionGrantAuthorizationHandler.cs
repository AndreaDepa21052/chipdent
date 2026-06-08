using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Chipdent.Web.Infrastructure.Identity;

/// <summary>
/// Handler additivo che concede l'accesso a un controller quando l'utente ha un
/// grant per-utente (claim <c>section_grants</c>) per una delle sezioni servite da
/// quel controller. È puramente additivo: non revoca mai accessi che il ruolo già
/// concede. Per sicurezza non interviene sulle policy esclusive Owner/PlatformAdmin
/// (le cui <see cref="RolesAuthorizationRequirement.AllowedRoles"/> non includono
/// nessun ruolo "normale"), così un grant di sezione non può scalare a quelle aree.
/// </summary>
public class SectionGrantAuthorizationHandler : AuthorizationHandler<RolesAuthorizationRequirement>
{
    private static readonly string[] NormalRoles =
    {
        Policies.Names.Management,
        Policies.Names.Direttore,
        Policies.Names.Backoffice,
        Policies.Names.Staff
    };

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RolesAuthorizationRequirement requirement)
    {
        // Solo policy "di sezione" (che ammettono almeno un ruolo normale).
        if (requirement.AllowedRoles is null
            || !requirement.AllowedRoles.Any(r => NormalRoles.Contains(r, StringComparer.Ordinal)))
        {
            return Task.CompletedTask;
        }

        // Override attivo?
        var overrideOn = string.Equals(
            context.User.FindFirst(TenantResolverMiddleware.SectionOverrideClaim)?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
        if (!overrideOn) return Task.CompletedTask;

        var grantsRaw = context.User.FindFirst(TenantResolverMiddleware.SectionGrantsClaim)?.Value;
        if (string.IsNullOrEmpty(grantsRaw)) return Task.CompletedTask;
        var granted = grantsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (granted.Length == 0) return Task.CompletedTask;

        var controller = ResolveController(context.Resource);
        if (controller is null) return Task.CompletedTask;

        if (SectionRoutes.ControllerSections.TryGetValue(controller, out var slugs)
            && slugs.Any(s => granted.Contains(s, StringComparer.OrdinalIgnoreCase)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static string? ResolveController(object? resource)
    {
        // Con endpoint routing (default) la resource è l'HttpContext; con il filtro
        // MVC classico è l'AuthorizationFilterContext.
        HttpContext? http = resource switch
        {
            HttpContext c => c,
            AuthorizationFilterContext afc => afc.HttpContext,
            _ => null
        };
        var descriptor = http?.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return descriptor?.ControllerName;
    }
}
