using System.Security.Claims;
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
    public IActionResult Login(string? returnUrl = null)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var user = await _mongo.Users
            .Find(u => u.Email == vm.Email && u.IsActive)
            .FirstOrDefaultAsync();

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
            new(TenantResolverMiddleware.TenantSlugClaim, tenant.Slug)
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

        var destination = (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
            ? vm.ReturnUrl
            : Url.Action("Index", "Dashboard")!;

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
