using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Chipdent.Web.Infrastructure.Identity;

/// <summary>
/// Policy di autorizzazione allineate alla mappa funzionale Chipdent
/// (Management / Direttore / Backoffice / Staff). Owner è una variante tecnica
/// di Management usata solo per il last-owner-guard del workspace.
/// </summary>
public static class Policies
{
    public const string RequireOwner      = nameof(RequireOwner);
    public const string RequireManagement = nameof(RequireManagement);
    public const string RequireDirettore  = nameof(RequireDirettore);
    public const string RequireBackoffice = nameof(RequireBackoffice);
    public const string RequireFornitore  = nameof(RequireFornitore);

    public static class Names
    {
        public const string Owner      = "Owner";
        public const string Management = "Management";
        public const string Direttore  = "Direttore";
        public const string Backoffice = "Backoffice";
        public const string Staff      = "Staff";
        public const string Fornitore  = "Fornitore";
    }

    public static void Configure(AuthorizationOptions o)
    {
        // Owner: amministratore tecnico del workspace (last-owner-guard).
        o.AddPolicy(RequireOwner,      p => p.RequireRole(Names.Owner));

        // Management: vista direzionale completa (Admin/CEO/COO/HR Director).
        o.AddPolicy(RequireManagement, p => p.RequireRole(Names.Owner, Names.Management));

        // Direttore: operatività di sede (turni, sostituzioni, comunicazioni mgmt).
        // Il Backoffice NON è incluso: non gestisce scheduling.
        o.AddPolicy(RequireDirettore,  p => p.RequireRole(Names.Owner, Names.Management, Names.Direttore));

        // Backoffice: anagrafiche e compliance (dottori/dipendenti/RLS).
        // Il Direttore è incluso: può gestire le anagrafiche della propria sede.
        o.AddPolicy(RequireBackoffice, p => p.RequireRole(Names.Owner, Names.Management, Names.Backoffice, Names.Direttore));

        // Fornitore: utente esterno autenticato con accesso al solo portale /fornitori.
        // Non viene MAI incluso nelle altre policy: vede solo i propri dati.
        o.AddPolicy(RequireFornitore, p => p.RequireRole(Names.Fornitore));
    }
}

public static class UserAccess
{
    public static bool IsOwner(this ClaimsPrincipal? user) =>
        user?.IsInRole(Policies.Names.Owner) == true;

    public static bool IsManagement(this ClaimsPrincipal? user) =>
        user is not null && (user.IsInRole(Policies.Names.Owner) || user.IsInRole(Policies.Names.Management));

    public static bool IsDirettore(this ClaimsPrincipal? user) =>
        user?.IsInRole(Policies.Names.Direttore) == true;

    public static bool IsBackoffice(this ClaimsPrincipal? user) =>
        user?.IsInRole(Policies.Names.Backoffice) == true;

    public static bool IsStaff(this ClaimsPrincipal? user) =>
        user?.IsInRole(Policies.Names.Staff) == true;

    public static bool IsFornitore(this ClaimsPrincipal? user) =>
        user?.IsInRole(Policies.Names.Fornitore) == true;

    /// <summary>
    /// Vero se l'utente ha visibilità completa su tutto il tenant (Management/Owner).
    /// Direttore, Backoffice e Staff sono sempre limitati al proprio scope.
    /// </summary>
    public static bool HasFullTenantAccess(this ClaimsPrincipal? user) => user.IsManagement();

    public static bool CanManageUsers(this ClaimsPrincipal? user) => user.IsManagement();

    /// <summary>
    /// Può approvare richieste operative (ferie, sostituzioni). Direttore + Management.
    /// </summary>
    public static bool CanApprove(this ClaimsPrincipal? user) =>
        user.IsManagement() || user.IsDirettore();

    /// <summary>
    /// Può vedere/gestire anagrafiche e compliance. Backoffice + Direttore + Management.
    /// </summary>
    public static bool CanSeeAnagrafiche(this ClaimsPrincipal? user) =>
        user.IsManagement() || user.IsDirettore() || user.IsBackoffice();

    public static string? LinkedPersonId(this ClaimsPrincipal? user)
    {
        var v = user?.FindFirst("linked_person_id")?.Value;
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public static string? LinkedPersonType(this ClaimsPrincipal? user)
    {
        var v = user?.FindFirst("linked_person_type")?.Value;
        return string.IsNullOrEmpty(v) || v == "None" ? null : v;
    }
}
