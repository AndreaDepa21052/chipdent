using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Storage;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("segnalazioni")]
public class SegnalazioniController : Controller
{
    private const long MaxUploadBytes = 50 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".heic", ".mp4", ".mov", ".doc", ".docx", ".txt" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;
    private readonly INotificationPublisher _publisher;

    public SegnalazioniController(MongoContext mongo, ITenantContext tenant, IFileStorage storage, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(StatoSegnalazione? filter = null, TipoSegnalazione? tipo = null)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var canResolve = User.IsManagement() || User.IsDirettore();

        var filterBuilder = Builders<Segnalazione>.Filter.Eq(s => s.TenantId, tid);
        if (!canResolve) filterBuilder &= Builders<Segnalazione>.Filter.Eq(s => s.MittenteUserId, meId);
        if (filter.HasValue) filterBuilder &= Builders<Segnalazione>.Filter.Eq(s => s.Stato, filter.Value);
        if (tipo.HasValue) filterBuilder &= Builders<Segnalazione>.Filter.Eq(s => s.Tipo, tipo.Value);

        var seg = await _mongo.Segnalazioni.Find(filterBuilder)
            .SortByDescending(s => s.Priorita).ThenByDescending(s => s.CreatedAt).ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        // Counter "ottimistici" (per banner): contati senza filtro stato
        var statBase = Builders<Segnalazione>.Filter.Eq(s => s.TenantId, tid);
        if (!canResolve) statBase &= Builders<Segnalazione>.Filter.Eq(s => s.MittenteUserId, meId);
        var aperte = (int)await _mongo.Segnalazioni.CountDocumentsAsync(statBase & Builders<Segnalazione>.Filter.Eq(s => s.Stato, StatoSegnalazione.Aperta));
        var inLav = (int)await _mongo.Segnalazioni.CountDocumentsAsync(statBase & Builders<Segnalazione>.Filter.Eq(s => s.Stato, StatoSegnalazione.InLavorazione));
        var urgenti = (int)await _mongo.Segnalazioni.CountDocumentsAsync(statBase & Builders<Segnalazione>.Filter.Eq(s => s.Priorita, PrioritaSegnalazione.Urgente) & Builders<Segnalazione>.Filter.Ne(s => s.Stato, StatoSegnalazione.Risolta));

        ViewData["Section"] = "segnalazioni";
        return View(new SegnalazioniIndexViewModel
        {
            Segnalazioni = seg.Select(s => new SegnalazioneRow(s, cliniche.GetValueOrDefault(s.ClinicaId, "—"))).ToList(),
            Filter = filter,
            TipoFilter = tipo,
            CanResolve = canResolve,
            Aperte = aperte,
            InLavorazione = inLav,
            Urgenti = urgenti
        });
    }

    [HttpGet("nuova")]
    public async Task<IActionResult> Create()
    {
        var tid = _tenant.TenantId!;
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();

        // Per Staff linkato a Dipendente, pre-compila la sede
        string? defaultClinica = null;
        if (User.LinkedPersonType() == "Dipendente" && !string.IsNullOrEmpty(User.LinkedPersonId()))
        {
            var dip = await _mongo.Dipendenti.Find(d => d.Id == User.LinkedPersonId() && d.TenantId == tid).FirstOrDefaultAsync();
            defaultClinica = dip?.ClinicaId;
        }

        ViewData["Section"] = "segnalazioni";
        return View(new NuovaSegnalazioneViewModel
        {
            Cliniche = cliniche,
            ClinicaId = defaultClinica ?? string.Empty,
            LockedClinica = !string.IsNullOrEmpty(defaultClinica) && !User.IsManagement()
        });
    }

    [HttpPost("nuova")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Create(NuovaSegnalazioneViewModel vm)
    {
        var tid = _tenant.TenantId!;
        if (string.IsNullOrEmpty(vm.ClinicaId))
            ModelState.AddModelError(nameof(vm.ClinicaId), "Sede obbligatoria.");

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "segnalazioni";
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
            return View(vm);
        }

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var s = new Segnalazione
        {
            TenantId = tid,
            ClinicaId = vm.ClinicaId,
            MittenteUserId = meId,
            MittenteNome = meName,
            Tipo = vm.Tipo,
            Priorita = vm.Priorita,
            Titolo = vm.Titolo.Trim(),
            Descrizione = vm.Descrizione.Trim(),
            Stato = StatoSegnalazione.Aperta
        };

        if (vm.Allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(s, vm.Allegato);
            if (err is not null)
            {
                ModelState.AddModelError(nameof(vm.Allegato), err);
                ViewData["Section"] = "segnalazioni";
                vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
                return View(vm);
            }
        }

        await _mongo.Segnalazioni.InsertOneAsync(s);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "comm",
            title = $"Segnalazione: {s.Titolo}",
            description = $"{s.Tipo} · priorità {s.Priorita} · da {s.MittenteNome}",
            when = DateTime.UtcNow
        });

        TempData["flash"] = "Segnalazione inviata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/prendi-in-carico")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Take(string id)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Segnalazioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";
        await _mongo.Segnalazioni.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<Segnalazione>.Update
                .Set(x => x.Stato, StatoSegnalazione.InLavorazione)
                .Set(x => x.AssegnatoAUserId, meId)
                .Set(x => x.AssegnatoANome, meName)
                .Set(x => x.DataPresaInCarico, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Segnalazione presa in carico.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/risolvi")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(string id, string? note = null)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Segnalazioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var update = Builders<Segnalazione>.Update
            .Set(x => x.Stato, StatoSegnalazione.Risolta)
            .Set(x => x.DataRisoluzione, DateTime.UtcNow)
            .Set(x => x.NoteRisoluzione, note)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        if (string.IsNullOrEmpty(s.AssegnatoAUserId))
        {
            update = update
                .Set(x => x.AssegnatoAUserId, meId)
                .Set(x => x.AssegnatoANome, meName)
                .Set(x => x.DataPresaInCarico, DateTime.UtcNow);
        }
        await _mongo.Segnalazioni.UpdateOneAsync(x => x.Id == id && x.TenantId == tid, update);

        await _publisher.PublishAsync(tid, "activity", new
        {
            kind = "comm",
            title = $"Segnalazione risolta: {s.Titolo}",
            description = note ?? "",
            when = DateTime.UtcNow
        });

        TempData["flash"] = "Segnalazione chiusa.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/annulla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string id)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var s = await _mongo.Segnalazioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();
        if (s.MittenteUserId != meId && !User.IsManagement() && !User.IsDirettore()) return Forbid();
        if (s.Stato == StatoSegnalazione.Risolta)
        {
            TempData["flash"] = "Una segnalazione risolta non può essere annullata.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.Segnalazioni.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<Segnalazione>.Update
                .Set(x => x.Stato, StatoSegnalazione.Annullata)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Segnalazione annullata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/allegato")]
    public async Task<IActionResult> Allegato(string id)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Segnalazioni.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null || string.IsNullOrEmpty(s.AllegatoPath)) return NotFound();

        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (s.MittenteUserId != meId && !User.IsManagement() && !User.IsDirettore()) return Forbid();

        var abs = Path.Combine(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath, s.AllegatoPath);
        if (!System.IO.File.Exists(abs)) return NotFound();
        var ext = Path.GetExtension(s.AllegatoNome ?? "").ToLowerInvariant();
        var mime = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream"
        };
        return PhysicalFile(abs, mime, s.AllegatoNome ?? "allegato");
    }

    private async Task<string?> TryAttachAsync(Segnalazione target, Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (file.Length > MaxUploadBytes) return $"File troppo grande (max {MaxUploadBytes / (1024 * 1024)}MB).";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return $"Estensione non consentita: {ext}";

        await using var stream = file.OpenReadStream();
        var stored = await _storage.SaveAsync(_tenant.TenantId!, "segnalazioni", file.FileName, stream, file.ContentType);
        target.AllegatoNome = file.FileName;
        target.AllegatoPath = stored.RelativePath;
        target.AllegatoSize = stored.SizeBytes;
        return null;
    }
}
