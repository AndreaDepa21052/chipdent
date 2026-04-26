using System.Security.Cryptography;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireAdmin)]
[Route("utenti")]
public class UsersController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;

    public UsersController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var users = await _mongo.Users
            .Find(u => u.TenantId == _tenant.TenantId)
            .SortBy(u => u.FullName).ToListAsync();
        var inviti = await _mongo.Inviti
            .Find(i => i.TenantId == _tenant.TenantId && i.UsatoIl == null && i.ScadeIl > DateTime.UtcNow)
            .SortByDescending(i => i.CreatedAt).ToListAsync();

        ViewData["Section"] = "users";
        return View(new UsersIndexViewModel { Users = users, InvitiAttivi = inviti });
    }

    [HttpGet("invita")]
    public IActionResult Invite()
    {
        ViewData["Section"] = "users";
        return View(new InviteUserViewModel());
    }

    [HttpPost("invita")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteUserViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "users";
            return View(vm);
        }

        var existing = await _mongo.Users
            .Find(u => u.Email == vm.Email && u.TenantId == _tenant.TenantId)
            .AnyAsync();
        if (existing)
        {
            ModelState.AddModelError(nameof(vm.Email), "Esiste già un utente con questa email.");
            ViewData["Section"] = "users";
            return View(vm);
        }

        var invito = new Invito
        {
            TenantId = _tenant.TenantId!,
            Email = vm.Email.Trim().ToLowerInvariant(),
            FullName = vm.FullName.Trim(),
            Ruolo = vm.Ruolo,
            Token = GenerateToken(),
            ScadeIl = DateTime.UtcNow.AddDays(7),
            InvitatoDaUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty
        };
        await _mongo.Inviti.InsertOneAsync(invito);

        var url = Url.Action("Accept", "Account", new { token = invito.Token }, Request.Scheme);
        TempData["flash"] = $"Invito creato. Link da inviare a {invito.Email}: {url}";

        await _publisher.PublishAsync(_tenant.TenantId!, "activity", new
        {
            kind = "comm",
            title = "Invito utente creato",
            description = $"{invito.FullName} ({invito.Ruolo})",
            when = DateTime.UtcNow
        });

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("inviti/{id}/revoca")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeInvite(string id)
    {
        await _mongo.Inviti.DeleteOneAsync(i => i.Id == id && i.TenantId == _tenant.TenantId);
        TempData["flash"] = "Invito revocato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/disattiva")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string id)
    {
        var u = await _mongo.Users.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (u is null) return NotFound();
        var current = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (u.Id == current)
        {
            TempData["flash"] = "Non puoi disattivare te stesso.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.Users.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<User>.Update.Set(x => x.IsActive, !u.IsActive));
        TempData["flash"] = u.IsActive ? "Utente disattivato." : "Utente riattivato.";
        return RedirectToAction(nameof(Index));
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
