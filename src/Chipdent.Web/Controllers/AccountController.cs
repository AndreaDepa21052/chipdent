using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly MongoContext _mongo;
    private readonly IPasswordHasher _hasher;

    public AccountController(MongoContext mongo, IPasswordHasher hasher)
    {
        _mongo = mongo;
        _hasher = hasher;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null, string? tenantSlug = null)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }
        return View(new LoginViewModel { ReturnUrl = returnUrl, TenantSlug = tenantSlug });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var users = await _mongo.Users
            .Find(u => u.Email == vm.Email && u.IsActive)
            .ToListAsync();

        User? user = null;

        if (!string.IsNullOrEmpty(vm.TenantSlug))
        {
            var targetTenant = await _mongo.Tenants.Find(t => t.Slug == vm.TenantSlug).FirstOrDefaultAsync();
            if (targetTenant is not null)
                user = users.FirstOrDefault(u => u.TenantId == targetTenant.Id);
        }

        if (user is null && users.Count == 1) user = users[0];

        if (user is null && users.Count > 1)
        {
            var tenantIds = users.Select(u => u.TenantId).ToList();
            var tenantsForEmail = await _mongo.Tenants
                .Find(t => tenantIds.Contains(t.Id) && t.IsActive)
                .ToListAsync();
            vm.Workspaces = tenantsForEmail
                .OrderBy(t => t.DisplayName)
                .Select(t => (t.Slug, t.DisplayName))
                .ToList();
            vm.Error = "Hai più workspace con questa email. Seleziona quello a cui accedere.";
            return View(vm);
        }

        if (user is null || !_hasher.Verify(vm.Password, user.PasswordHash))
        {
            vm.Error = "Email o password non valide.";
            return View(vm);
        }

        var tenant = await _mongo.Tenants.Find(t => t.Id == user.TenantId).FirstOrDefaultAsync();
        if (tenant is null || !tenant.IsActive)
        {
            vm.Error = "Tenant non disponibile.";
            return View(vm);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(TenantResolverMiddleware.TenantIdClaim, tenant.Id),
            new(TenantResolverMiddleware.TenantSlugClaim, tenant.Slug),
            new(TenantResolverMiddleware.ClinicaIdsClaim, string.Join(",", user.ClinicaIds ?? new())),
            new(TenantResolverMiddleware.LinkedPersonTypeClaim, user.LinkedPersonType.ToString()),
            new(TenantResolverMiddleware.LinkedPersonIdClaim, user.LinkedPersonId ?? string.Empty)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = vm.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(vm.RememberMe ? 24 * 14 : 8)
            });

        await _mongo.Users.UpdateOneAsync(
            u => u.Id == user.Id,
            Builders<Chipdent.Web.Domain.Entities.User>.Update.Set(u => u.LastLoginAt, DateTime.UtcNow));

        // Fornitori: non hanno accesso alla dashboard interna, vengono mandati al portale dedicato.
        var defaultLanding = user.Role == UserRole.Fornitore
            ? Url.Action("Index", "FornitoriPortal")!
            : Url.Action("Index", "Dashboard")!;
        var destination = (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
            ? vm.ReturnUrl
            : defaultLanding;

        return RedirectToAction(nameof(Loading), new { next = destination });
    }

    [HttpGet("loading")]
    public IActionResult Loading(string? next = null)
    {
        var target = (!string.IsNullOrEmpty(next) && Url.IsLocalUrl(next))
            ? next
            : Url.Action("Index", "Dashboard")!;
        ViewData["Next"] = target;
        return View();
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("invito/{token}")]
    public async Task<IActionResult> Accept(string token)
    {
        var invito = await _mongo.Inviti.Find(i => i.Token == token).FirstOrDefaultAsync();
        if (invito is null || !invito.IsValido)
        {
            return View("AcceptInvalid");
        }
        var tenant = await _mongo.Tenants.Find(t => t.Id == invito.TenantId).FirstOrDefaultAsync();
        return View(new AcceptInviteViewModel
        {
            Token = invito.Token,
            Email = invito.Email,
            FullName = invito.FullName,
            TenantName = tenant?.DisplayName ?? "Chipdent"
        });
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet("profilo")]
    public async Task<IActionResult> Profile()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var u = await _mongo.Users.Find(x => x.Id == userId).FirstOrDefaultAsync();
        if (u is null) return RedirectToAction(nameof(Login));

        string? linkedName = null;
        if (!string.IsNullOrEmpty(u.LinkedPersonId))
        {
            if (u.LinkedPersonType == Chipdent.Web.Domain.Entities.LinkedPersonType.Dottore)
            {
                var d = await _mongo.Dottori.Find(x => x.Id == u.LinkedPersonId).FirstOrDefaultAsync();
                linkedName = d?.NomeCompleto;
            }
            else if (u.LinkedPersonType == Chipdent.Web.Domain.Entities.LinkedPersonType.Dipendente)
            {
                var p = await _mongo.Dipendenti.Find(x => x.Id == u.LinkedPersonId).FirstOrDefaultAsync();
                linkedName = p?.NomeCompleto;
            }
        }

        return View(new MyProfileViewModel
        {
            Id = u.Id, Email = u.Email, FullName = u.FullName, Phone = u.Phone,
            Role = u.Role, LinkedPersonType = u.LinkedPersonType, LinkedPersonId = u.LinkedPersonId,
            LinkedPersonName = linkedName, CreatedAt = u.CreatedAt, LastLoginAt = u.LastLoginAt
        });
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("profilo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(MyProfileViewModel vm)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var u = await _mongo.Users.Find(x => x.Id == userId).FirstOrDefaultAsync();
        if (u is null) return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
        {
            vm.Id = u.Id; vm.Email = u.Email; vm.Role = u.Role;
            vm.CreatedAt = u.CreatedAt; vm.LastLoginAt = u.LastLoginAt;
            vm.LinkedPersonType = u.LinkedPersonType; vm.LinkedPersonId = u.LinkedPersonId;
            return View(vm);
        }

        await _mongo.Users.UpdateOneAsync(
            x => x.Id == userId,
            Builders<Chipdent.Web.Domain.Entities.User>.Update
                .Set(x => x.FullName, vm.FullName.Trim())
                .Set(x => x.Phone, string.IsNullOrWhiteSpace(vm.Phone) ? null : vm.Phone.Trim())
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Profilo aggiornato. Esci e rientra per vedere il nome aggiornato in tutto il portale.";
        return RedirectToAction(nameof(Profile));
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var u = await _mongo.Users.Find(x => x.Id == userId).FirstOrDefaultAsync();
        if (u is null) return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid || !_hasher.Verify(vm.CurrentPassword, u.PasswordHash))
        {
            if (ModelState.IsValid) ModelState.AddModelError(nameof(vm.CurrentPassword), "Password attuale errata.");
            // re-render Profile with the password section showing the error
            TempData["pwdErrors"] = string.Join("|", ModelState
                .Where(s => s.Value!.Errors.Count > 0)
                .SelectMany(s => s.Value!.Errors.Select(e => $"{s.Key}::{e.ErrorMessage}")));
            return RedirectToAction(nameof(Profile));
        }

        await _mongo.Users.UpdateOneAsync(
            x => x.Id == userId,
            Builders<Chipdent.Web.Domain.Entities.User>.Update
                .Set(x => x.PasswordHash, _hasher.Hash(vm.NewPassword))
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Password aggiornata.";
        return RedirectToAction(nameof(Profile));
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet("preferenze")]
    public async Task<IActionResult> Preferences()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var u = await _mongo.Users.Find(x => x.Id == userId).FirstOrDefaultAsync();
        if (u is null) return RedirectToAction(nameof(Login));

        var p = u.Preferences ?? new Chipdent.Web.Domain.Entities.UserPreferences();
        return View(new PreferencesViewModel
        {
            NotificheInApp = p.NotificheInApp,
            MostraToast = p.MostraToast,
            SuoniNotifiche = p.SuoniNotifiche,
            DigestEmail = p.DigestEmail,
            Lingua = p.Lingua,
            Densita = p.Densita
        });
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost("preferenze")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preferences(PreferencesViewModel vm)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        if (!ModelState.IsValid) return View(vm);

        var prefs = new Chipdent.Web.Domain.Entities.UserPreferences
        {
            NotificheInApp = vm.NotificheInApp,
            MostraToast = vm.MostraToast,
            SuoniNotifiche = vm.SuoniNotifiche,
            DigestEmail = vm.DigestEmail,
            Lingua = string.IsNullOrEmpty(vm.Lingua) ? "it" : vm.Lingua,
            Densita = string.IsNullOrEmpty(vm.Densita) ? "comoda" : vm.Densita
        };
        await _mongo.Users.UpdateOneAsync(
            x => x.Id == userId,
            Builders<Chipdent.Web.Domain.Entities.User>.Update
                .Set(x => x.Preferences, prefs)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Preferenze salvate.";
        return RedirectToAction(nameof(Preferences));
    }

    [HttpPost("invito")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(AcceptInviteViewModel vm)
    {
        var invito = await _mongo.Inviti.Find(i => i.Token == vm.Token).FirstOrDefaultAsync();
        if (invito is null || !invito.IsValido) return View("AcceptInvalid");

        if (!ModelState.IsValid)
        {
            var tenantR = await _mongo.Tenants.Find(t => t.Id == invito.TenantId).FirstOrDefaultAsync();
            vm.Email = invito.Email;
            vm.FullName = invito.FullName;
            vm.TenantName = tenantR?.DisplayName ?? "Chipdent";
            return View(vm);
        }

        var user = new Chipdent.Web.Domain.Entities.User
        {
            TenantId = invito.TenantId,
            Email = invito.Email,
            FullName = invito.FullName,
            PasswordHash = _hasher.Hash(vm.Password),
            Role = invito.Ruolo,
            ClinicaIds = invito.ClinicaIds?.ToList() ?? new(),
            IsActive = true
        };
        await _mongo.Users.InsertOneAsync(user);
        await _mongo.Inviti.UpdateOneAsync(
            i => i.Id == invito.Id,
            Builders<Chipdent.Web.Domain.Entities.Invito>.Update.Set(i => i.UsatoIl, DateTime.UtcNow));

        TempData["flash"] = "Account creato. Effettua il login.";
        return RedirectToAction(nameof(Login));
    }
}
