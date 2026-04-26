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
        var tid = _tenant.TenantId!;
        var users = await _mongo.Users
            .Find(u => u.TenantId == tid)
            .SortBy(u => u.FullName).ToListAsync();
        var inviti = await _mongo.Inviti
            .Find(i => i.TenantId == tid && i.UsatoIl == null && i.ScadeIl > DateTime.UtcNow)
            .SortByDescending(i => i.CreatedAt).ToListAsync();

        var personaLookup = new Dictionary<string, string>();
        foreach (var d in await _mongo.Dottori.Find(x => x.TenantId == tid).ToListAsync())
            personaLookup[d.Id] = d.NomeCompleto;
        foreach (var p in await _mongo.Dipendenti.Find(x => x.TenantId == tid).ToListAsync())
            personaLookup[p.Id] = p.NomeCompleto;

        ViewData["Section"] = "users";
        return View(new UsersIndexViewModel
        {
            Users = users,
            InvitiAttivi = inviti,
            PersonaLookup = personaLookup,
            CurrentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty
        });
    }

    [HttpGet("{id}/modifica")]
    public async Task<IActionResult> Edit(string id)
    {
        var u = await _mongo.Users.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (u is null) return NotFound();

        var dottori = await _mongo.Dottori.Find(d => d.TenantId == _tenant.TenantId).SortBy(d => d.Cognome).ToListAsync();
        var dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == _tenant.TenantId).SortBy(d => d.Cognome).ToListAsync();
        var current = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        ViewData["Section"] = "users";
        return View(new UserEditViewModel
        {
            Id = u.Id,
            Email = u.Email,
            FullName = u.FullName,
            Role = u.Role,
            LinkedPersonType = u.LinkedPersonType,
            LinkedPersonId = u.LinkedPersonId,
            IsActive = u.IsActive,
            IsCurrent = u.Id == current,
            Dottori = dottori,
            Dipendenti = dipendenti
        });
    }

    [HttpPost("{id}/modifica")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserEditViewModel vm)
    {
        if (id != vm.Id) return BadRequest();
        var existing = await _mongo.Users.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (existing is null) return NotFound();

        var current = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var isCurrent = existing.Id == current;

        if (isCurrent && existing.Role == UserRole.Owner && vm.Role != UserRole.Owner)
        {
            ModelState.AddModelError(nameof(vm.Role), "Non puoi declassare te stesso da Owner.");
        }

        if (existing.Role == UserRole.Owner && vm.Role != UserRole.Owner)
        {
            var otherOwners = await _mongo.Users
                .Find(x => x.TenantId == _tenant.TenantId && x.Role == UserRole.Owner && x.Id != id && x.IsActive)
                .AnyAsync();
            if (!otherOwners)
            {
                ModelState.AddModelError(nameof(vm.Role), "Impossibile rimuovere l'unico Owner del workspace.");
            }
        }

        if (vm.LinkedPersonType != LinkedPersonType.None && string.IsNullOrEmpty(vm.LinkedPersonId))
        {
            ModelState.AddModelError(nameof(vm.LinkedPersonId), "Seleziona la persona da collegare oppure scegli «Nessuno».");
        }
        if (vm.LinkedPersonType == LinkedPersonType.None) vm.LinkedPersonId = null;

        if (!string.IsNullOrEmpty(vm.LinkedPersonId))
        {
            var alreadyLinked = await _mongo.Users
                .Find(x => x.TenantId == _tenant.TenantId
                           && x.Id != id
                           && x.LinkedPersonType == vm.LinkedPersonType
                           && x.LinkedPersonId == vm.LinkedPersonId)
                .AnyAsync();
            if (alreadyLinked)
            {
                ModelState.AddModelError(nameof(vm.LinkedPersonId), "Questa persona è già collegata a un altro utente.");
            }
        }

        if (!ModelState.IsValid)
        {
            vm.Email = existing.Email;
            vm.IsCurrent = isCurrent;
            vm.Dottori = await _mongo.Dottori.Find(d => d.TenantId == _tenant.TenantId).SortBy(d => d.Cognome).ToListAsync();
            vm.Dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == _tenant.TenantId).SortBy(d => d.Cognome).ToListAsync();
            ViewData["Section"] = "users";
            return View(vm);
        }

        await _mongo.Users.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<User>.Update
                .Set(x => x.FullName, vm.FullName)
                .Set(x => x.Role, vm.Role)
                .Set(x => x.LinkedPersonType, vm.LinkedPersonType)
                .Set(x => x.LinkedPersonId, vm.LinkedPersonId)
                .Set(x => x.IsActive, vm.IsActive)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = $"Utente «{vm.FullName}» aggiornato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/ruolo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(string id, UserRole role)
    {
        var u = await _mongo.Users.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (u is null) return NotFound();

        var current = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (u.Id == current && u.Role == UserRole.Owner && role != UserRole.Owner)
        {
            TempData["flash"] = "Non puoi declassare te stesso da Owner.";
            return RedirectToAction(nameof(Index));
        }
        if (u.Role == UserRole.Owner && role != UserRole.Owner)
        {
            var otherOwners = await _mongo.Users
                .Find(x => x.TenantId == _tenant.TenantId && x.Role == UserRole.Owner && x.Id != id && x.IsActive)
                .AnyAsync();
            if (!otherOwners)
            {
                TempData["flash"] = "Impossibile rimuovere l'unico Owner del workspace.";
                return RedirectToAction(nameof(Index));
            }
        }

        await _mongo.Users.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<User>.Update.Set(x => x.Role, role).Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = $"Ruolo aggiornato a {role}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("permessi")]
    public IActionResult Permissions()
    {
        ViewData["Section"] = "users";
        return View(BuildMatrix());
    }

    private static PermissionsMatrixViewModel BuildMatrix()
    {
        var roles = new[] { UserRole.Operatore, UserRole.HR, UserRole.Manager, UserRole.Admin, UserRole.Owner };

        bool All(UserRole r) => true;
        bool Staff(UserRole r) => r != UserRole.Operatore;
        bool Hr(UserRole r) => r == UserRole.HR || r == UserRole.Manager || r == UserRole.Admin || r == UserRole.Owner;
        bool Manager(UserRole r) => r == UserRole.Manager || r == UserRole.Admin || r == UserRole.Owner;
        bool Admin(UserRole r) => r == UserRole.Admin || r == UserRole.Owner;
        bool Owner(UserRole r) => r == UserRole.Owner;

        PermissionRow Row(string mod, string action, Func<UserRole, bool> rule) =>
            new(mod, action, roles.ToDictionary(r => r, rule));

        var rows = new List<PermissionRow>
        {
            Row("Dashboard", "Visualizza", All),
            Row("Turni", "Visualizza calendario", All),
            Row("Turni", "Crea / modifica turni", Manager),
            Row("Comunicazioni", "Visualizza inbox", All),
            Row("Comunicazioni", "Invia comunicazione", All),
            Row("Comunicazioni", "Approva richieste", Manager),
            Row("Cliniche", "Visualizza", Staff),
            Row("Cliniche", "Crea / modifica", Manager),
            Row("Cliniche", "Elimina", Admin),
            Row("Dottori", "Visualizza", Staff),
            Row("Dottori", "Crea / modifica", Hr),
            Row("Dipendenti", "Visualizza", Staff),
            Row("Dipendenti", "Crea / modifica", Hr),
            Row("RLS / Sicurezza", "Visualizza", Staff),
            Row("RLS / Sicurezza", "Crea visite / corsi", Hr),
            Row("RLS / Sicurezza", "Gestisci DVR", Manager),
            Row("Documentazione", "Visualizza", Staff),
            Row("Documentazione", "Crea / modifica", Manager),
            Row("Documentazione", "Elimina", Admin),
            Row("Utenti", "Gestisci utenti e inviti", Admin),
            Row("Utenti", "Cambia ruoli", Admin),
            Row("Workspace", "Impostazioni workspace", Owner)
        };

        return new PermissionsMatrixViewModel { Roles = roles, Rows = rows };
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
        if (u.IsActive && u.Role == UserRole.Owner)
        {
            var otherOwners = await _mongo.Users
                .Find(x => x.TenantId == _tenant.TenantId && x.Role == UserRole.Owner && x.Id != id && x.IsActive)
                .AnyAsync();
            if (!otherOwners)
            {
                TempData["flash"] = "Impossibile disattivare l'unico Owner del workspace.";
                return RedirectToAction(nameof(Index));
            }
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
