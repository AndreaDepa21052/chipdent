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

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
        {
            return Redirect(vm.ReturnUrl);
        }

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
