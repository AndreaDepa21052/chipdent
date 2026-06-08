using System.Security.Cryptography;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Chipdent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize(Policy = Policies.RequireManagement)]
[Route("utenti")]
public class UsersController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly INotificationPublisher _publisher;
    private readonly IMenuVisibilityService _menu;

    public UsersController(MongoContext mongo, ITenantContext tenant, INotificationPublisher publisher, IMenuVisibilityService menu)
    {
        _mongo = mongo;
        _tenant = tenant;
        _publisher = publisher;
        _menu = menu;
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
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
        var current = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        ViewData["Section"] = "users";
        return View(new UserEditViewModel
        {
            Id = u.Id,
            Email = u.Email,
            FullName = u.FullName,
            Role = u.Role,
            AccessLevel = u.AccessLevel,
            ClinicaIds = u.ClinicaIds?.ToList() ?? new(),
            LinkedPersonType = u.LinkedPersonType,
            LinkedPersonId = u.LinkedPersonId,
            IsActive = u.IsActive,
            IsCurrent = u.Id == current,
            Cliniche = cliniche,
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

        if (isCurrent && vm.AccessLevel == AccessLevel.SolaLettura)
        {
            ModelState.AddModelError(nameof(vm.AccessLevel), "Non puoi limitare te stesso alla sola lettura.");
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

        // Direttore: deve essere assegnato ad almeno una clinica.
        // Management/Owner: lasciare vuoto = visibilità su tutte le sedi.
        // Backoffice/Staff: ClinicaIds è informativo, non obbligatorio.
        var clinicaIds = (vm.ClinicaIds ?? new())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
        if (vm.Role == UserRole.Direttore && clinicaIds.Count == 0)
        {
            ModelState.AddModelError(nameof(vm.ClinicaIds), "Un Direttore deve essere assegnato ad almeno una clinica.");
        }

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
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
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
                .Set(x => x.AccessLevel, vm.AccessLevel)
                .Set(x => x.ClinicaIds, clinicaIds)
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

    // ── Accessi per-utente ────────────────────────────────────────────────
    // Sostituisce la vecchia matrice statica per-ruolo: per ogni utente si
    // configurano le sezioni della sidebar a cui può accedere. L'override può
    // solo restringere le sezioni già consentite dal ruolo dell'utente.

    [HttpGet("permessi")]
    public async Task<IActionResult> Permissions(string? userId = null)
    {
        var tid = _tenant.TenantId!;
        var currentId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        var users = await _mongo.Users
            .Find(u => u.TenantId == tid)
            .SortBy(u => u.FullName).ToListAsync();

        var rows = users
            .Select(u => new UserAccessRow(u.Id, u.FullName, u.Email, u.Role, u.HasSectionOverride, u.Id == currentId, u.IsActive))
            .ToList();

        UserSectionEditorViewModel? editor = null;
        var selected = users.FirstOrDefault(u => u.Id == userId);
        if (selected is not null)
        {
            editor = await BuildEditorAsync(selected, currentId);
        }

        ViewData["Section"] = "users";
        return View(new UserSectionAccessViewModel { Users = rows, Editor = editor });
    }

    [HttpPost("permessi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSections(string userId, bool personalizza, List<string>? sezioni)
    {
        var user = await _mongo.Users.Find(x => x.Id == userId && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (user is null) return NotFound();

        var currentId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        if (user.Id == currentId)
        {
            TempData["flash"] = "Non puoi modificare i tuoi stessi accessi: chiedi a un altro amministratore.";
            return RedirectToAction(nameof(Permissions), new { userId });
        }

        bool hasOverride;
        List<string> visible;
        if (!personalizza)
        {
            // Torna a ereditare dal ruolo.
            hasOverride = false;
            visible = new();
        }
        else
        {
            // Si possono abilitare solo le sezioni consentite dal ruolo.
            var roleAvailable = await GetRoleAvailableAsync(user.Role);
            visible = (sezioni ?? new())
                .Where(s => roleAvailable.Contains(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            hasOverride = true;
        }

        await _mongo.Users.UpdateOneAsync(
            x => x.Id == userId && x.TenantId == _tenant.TenantId,
            Builders<User>.Update
                .Set(x => x.HasSectionOverride, hasOverride)
                .Set(x => x.VisibleSections, visible)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = personalizza
            ? $"Accessi personalizzati salvati per «{user.FullName}» ({visible.Count} sezioni)."
            : $"«{user.FullName}» eredita di nuovo gli accessi del ruolo.";
        return RedirectToAction(nameof(Permissions), new { userId });
    }

    /// <summary>
    /// Slug delle sezioni realmente raggiungibili dal ruolo: rispecchia i gate di gruppo
    /// della sidebar (_Layout) e sottrae le sezioni nascoste al ruolo dal pannello menu.
    /// Sono le uniche sezioni configurabili nell'editor per-utente.
    /// </summary>
    private async Task<HashSet<string>> GetRoleAvailableAsync(UserRole role)
    {
        if (role is UserRole.Owner or UserRole.PlatformAdmin)
            return MenuCatalog.AllSections.Select(s => s.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hidden = await _menu.GetHiddenForRoleAsync(role.ToString());
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canTesoreriaFornitori = role is UserRole.Management or UserRole.Backoffice;
        foreach (var g in MenuCatalog.Groups)
        {
            if (!GroupReachable(role, g.Key)) continue;
            foreach (var s in g.Sections)
            {
                // "Fornitori" nel gruppo Anagrafiche ha un gate extra (no Direttore).
                if (s.Slug == "fornitori" && !canTesoreriaFornitori) continue;
                if (hidden.Contains(s.Slug)) continue;
                result.Add(s.Slug);
            }
        }
        return result;
    }

    /// <summary>
    /// Replica i gate di gruppo della sidebar: quali gruppi della navigazione sono
    /// strutturalmente visibili a un ruolo (a prescindere dal pannello menu).
    /// </summary>
    private static bool GroupReachable(UserRole role, string groupKey)
    {
        var mgmt = role is UserRole.Management or UserRole.Owner or UserRole.PlatformAdmin;
        var anagrafiche = mgmt || role is UserRole.Direttore or UserRole.Backoffice; // canSeeAnagrafiche
        return groupKey switch
        {
            "operativita"     => true,
            "anagrafiche"     => anagrafiche,
            "direzionale"     => mgmt,
            "tesoreria"       => mgmt || role == UserRole.Backoffice,
            "compliance"      => anagrafiche,
            "amministrazione" => mgmt,
            _ => false
        };
    }

    private async Task<UserSectionEditorViewModel> BuildEditorAsync(User user, string currentId)
    {
        var roleAvailable = await GetRoleAvailableAsync(user.Role);
        // Checkbox spuntati: se c'è override usa la sua allow-list, altrimenti
        // mostra tutto ciò che il ruolo consente (stato ereditato).
        var allowed = user.HasSectionOverride
            ? new HashSet<string>(user.VisibleSections ?? new(), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(roleAvailable, StringComparer.OrdinalIgnoreCase);

        return new UserSectionEditorViewModel
        {
            UserId = user.Id,
            FullName = user.FullName,
            Role = user.Role,
            IsCurrent = user.Id == currentId,
            HasOverride = user.HasSectionOverride,
            Groups = MenuCatalog.Groups,
            Allowed = allowed,
            RoleAvailable = roleAvailable
        };
    }

    [HttpGet("invita")]
    public async Task<IActionResult> Invite()
    {
        ViewData["Section"] = "users";
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
        return View(new InviteUserViewModel { Cliniche = cliniche });
    }

    [HttpPost("invita")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(InviteUserViewModel vm)
    {
        var clinicaIds = (vm.ClinicaIds ?? new())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();
        if (vm.Ruolo == UserRole.Direttore && clinicaIds.Count == 0)
        {
            ModelState.AddModelError(nameof(vm.ClinicaIds), "Un Direttore deve essere assegnato ad almeno una clinica.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "users";
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
            return View(vm);
        }

        var existing = await _mongo.Users
            .Find(u => u.Email == vm.Email && u.TenantId == _tenant.TenantId)
            .AnyAsync();
        if (existing)
        {
            ModelState.AddModelError(nameof(vm.Email), "Esiste già un utente con questa email.");
            ViewData["Section"] = "users";
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
            return View(vm);
        }

        var invito = new Invito
        {
            TenantId = _tenant.TenantId!,
            Email = vm.Email.Trim().ToLowerInvariant(),
            FullName = vm.FullName.Trim(),
            Ruolo = vm.Ruolo,
            AccessLevel = vm.AccessLevel,
            ClinicaIds = clinicaIds,
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
