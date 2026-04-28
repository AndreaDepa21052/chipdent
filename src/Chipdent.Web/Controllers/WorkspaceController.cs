using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Storage;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("workspace")]
public class WorkspaceController : Controller
{
    private const long MaxLogoBytes = 4 * 1024 * 1024;
    private static readonly HashSet<string> AllowedLogoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".svg" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;
    private readonly IPasswordHasher _hasher;

    public WorkspaceController(MongoContext mongo, ITenantContext tenant, IFileStorage storage, IPasswordHasher hasher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
        _hasher = hasher;
    }

    // ───── Impostazioni workspace ─────

    [HttpGet("impostazioni")]
    [Authorize(Policy = Policies.RequireManagement)]
    public async Task<IActionResult> Impostazioni()
    {
        var tid = _tenant.TenantId!;
        var t = await _mongo.Tenants.Find(x => x.Id == tid).FirstOrDefaultAsync();
        if (t is null) return NotFound();

        ViewData["Section"] = "workspace";
        return View(MapTo(t));
    }

    [HttpPost("impostazioni")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxLogoBytes)]
    public async Task<IActionResult> Impostazioni(WorkspaceImpostazioniViewModel vm)
    {
        var tid = _tenant.TenantId!;
        var t = await _mongo.Tenants.Find(x => x.Id == tid).FirstOrDefaultAsync();
        if (t is null) return NotFound();

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "workspace";
            vm.Slug = t.Slug;
            vm.LogoPath = t.LogoPath;
            vm.CreatedAt = t.CreatedAt;
            vm.DataAttivazione = t.DataAttivazione;
            return View(vm);
        }

        var update = Builders<Tenant>.Update
            .Set(x => x.DisplayName, vm.DisplayName.Trim())
            .Set(x => x.Descrizione, vm.Descrizione)
            .Set(x => x.PrimaryColor, vm.PrimaryColor)
            .Set(x => x.RagioneSociale, vm.RagioneSociale)
            .Set(x => x.PartitaIva, vm.PartitaIva)
            .Set(x => x.CodiceFiscale, vm.CodiceFiscale)
            .Set(x => x.IndirizzoLegale, vm.IndirizzoLegale)
            .Set(x => x.FusoOrario, string.IsNullOrWhiteSpace(vm.FusoOrario) ? "Europe/Rome" : vm.FusoOrario)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        // Logo: rimozione esplicita
        if (vm.RimuoviLogo && !string.IsNullOrEmpty(t.LogoPath))
        {
            await _storage.DeleteAsync(tid, t.LogoPath);
            update = update.Set(x => x.LogoPath, (string?)null);
        }

        // Logo: upload nuovo
        if (vm.LogoFile is { Length: > 0 })
        {
            if (vm.LogoFile.Length > MaxLogoBytes)
            {
                ModelState.AddModelError(nameof(vm.LogoFile), $"Il logo non può superare {MaxLogoBytes / 1024 / 1024} MB.");
                ViewData["Section"] = "workspace";
                return View(MapTo(t));
            }
            var ext = Path.GetExtension(vm.LogoFile.FileName).ToLowerInvariant();
            if (!AllowedLogoExt.Contains(ext))
            {
                ModelState.AddModelError(nameof(vm.LogoFile), "Formato logo non consentito (PNG/JPG/WEBP/SVG).");
                ViewData["Section"] = "workspace";
                return View(MapTo(t));
            }
            if (!string.IsNullOrEmpty(t.LogoPath)) await _storage.DeleteAsync(tid, t.LogoPath);

            await using var stream = vm.LogoFile.OpenReadStream();
            var stored = await _storage.SaveAsync(tid, "branding", "logo" + ext, stream, vm.LogoFile.ContentType);
            update = update.Set(x => x.LogoPath, stored.RelativePath);
        }

        await _mongo.Tenants.UpdateOneAsync(x => x.Id == tid, update);
        TempData["flash"] = "Impostazioni workspace aggiornate.";
        return RedirectToAction(nameof(Impostazioni));
    }

    // ───── Nuovo workspace ─────

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireOwner)]
    public IActionResult Nuovo()
    {
        ViewData["Section"] = "workspace";
        return View(new NuovoWorkspaceViewModel());
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireOwner)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Nuovo(NuovoWorkspaceViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "workspace";
            return View(vm);
        }

        var slug = vm.Slug.Trim().ToLowerInvariant();
        var existing = await _mongo.Tenants.Find(t => t.Slug == slug).AnyAsync();
        if (existing)
        {
            ModelState.AddModelError(nameof(vm.Slug), "Slug già in uso. Scegline un altro.");
            ViewData["Section"] = "workspace";
            return View(vm);
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "Owner";
        var meEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
        if (string.IsNullOrEmpty(meEmail))
        {
            TempData["flash"] = "Email del tuo account non disponibile, impossibile creare un workspace senza Owner.";
            return RedirectToAction(nameof(Nuovo));
        }

        // Recupera la password hash dell'Owner attuale per riusarla nel nuovo Owner clonato
        var meUser = await _mongo.Users.Find(u => u.Id == meId).FirstOrDefaultAsync();
        if (meUser is null) return Forbid();

        var newTenant = new Tenant
        {
            Slug = slug,
            DisplayName = vm.DisplayName.Trim(),
            PrimaryColor = vm.PrimaryColor,
            Descrizione = vm.Descrizione,
            IsActive = true,
            DataAttivazione = DateTime.UtcNow,
            CreatoDaUserId = meId
        };
        await _mongo.Tenants.InsertOneAsync(newTenant);

        var newOwner = new User
        {
            TenantId = newTenant.Id,
            Email = meUser.Email,                    // stessa email
            FullName = meUser.FullName,
            PasswordHash = meUser.PasswordHash,      // stessa password
            Role = UserRole.Owner,
            IsActive = true
        };
        await _mongo.Users.InsertOneAsync(newOwner);

        TempData["flash"] = $"Workspace «{newTenant.DisplayName}» creato. Per accedervi fai logout e rilogga selezionando «{newTenant.Slug}».";
        return RedirectToAction(nameof(Impostazioni));
    }

    // ───── Switch workspace ─────

    /// <summary>
    /// Logout dal workspace corrente e redirect alla login con lo slug del tenant target preselezionato.
    /// L'utente dovrà confermare la sua password (anche se è la stessa) per il nuovo cookie auth.
    /// </summary>
    [HttpPost("switch/{slug}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string slug)
    {
        var meEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
        // Verifica che lo slug sia uno dei workspace dell'utente
        var target = await _mongo.Tenants.Find(t => t.Slug == slug && t.IsActive).FirstOrDefaultAsync();
        if (target is null) return NotFound();
        var hasAccount = await _mongo.Users.Find(u => u.TenantId == target.Id && u.Email == meEmail && u.IsActive).AnyAsync();
        if (!hasAccount) return Forbid();

        await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect($"/account/login?tenantSlug={Uri.EscapeDataString(slug)}");
    }

    private static WorkspaceImpostazioniViewModel MapTo(Tenant t) => new()
    {
        Id = t.Id,
        Slug = t.Slug,
        DisplayName = t.DisplayName,
        Descrizione = t.Descrizione,
        PrimaryColor = t.PrimaryColor,
        RagioneSociale = t.RagioneSociale,
        PartitaIva = t.PartitaIva,
        CodiceFiscale = t.CodiceFiscale,
        IndirizzoLegale = t.IndirizzoLegale,
        FusoOrario = t.FusoOrario,
        LogoPath = t.LogoPath,
        DataAttivazione = t.DataAttivazione,
        CreatedAt = t.CreatedAt
    };
}
