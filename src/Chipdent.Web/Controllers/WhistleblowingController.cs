using System.Security.Claims;
using System.Security.Cryptography;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Storage;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Route("whistleblowing")]
public class WhistleblowingController : Controller
{
    private const long MaxBytes = 20 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".doc", ".docx", ".txt" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;
    private readonly IPasswordHasher _hasher;

    public WhistleblowingController(MongoContext mongo, ITenantContext tenant, IFileStorage storage, IPasswordHasher hasher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
        _hasher = hasher;
    }

    // ─────────── Pubblico (anche non autenticati) ───────────

    [AllowAnonymous]
    [HttpGet("")]
    public async Task<IActionResult> Index(string? tenantSlug = null)
    {
        var tid = await ResolveTenantIdAsync(tenantSlug);
        var cliniche = string.IsNullOrEmpty(tid)
            ? new List<Clinica>()
            : await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
        ViewData["Section"] = "whistleblowing";
        return View(new WhistleblowingPubblicaViewModel
        {
            Cliniche = cliniche,
            TenantSlug = tenantSlug ?? _tenant.TenantSlug
        });
    }

    [AllowAnonymous]
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxBytes)]
    public async Task<IActionResult> Submit(WhistleblowingPubblicaViewModel vm)
    {
        var tid = await ResolveTenantIdAsync(vm.TenantSlug);
        if (string.IsNullOrEmpty(tid))
        {
            ModelState.AddModelError(string.Empty, "Tenant non identificato.");
        }

        if (!vm.Anonima && string.IsNullOrEmpty(vm.FirmatarioNome))
            ModelState.AddModelError(nameof(vm.FirmatarioNome), "Indica almeno il nome se firmi.");

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "whistleblowing";
            vm.Cliniche = string.IsNullOrEmpty(tid)
                ? Array.Empty<Clinica>()
                : await _mongo.Cliniche.Find(c => c.TenantId == tid).SortBy(c => c.Nome).ToListAsync();
            return View("Index", vm);
        }

        var s = new SegnalazioneWhistleblowing
        {
            TenantId = tid!,
            CodiceTracciamento = GenerateCode(),
            CodiceAccessoHash = _hasher.Hash(vm.CodiceAccesso),
            Tipo = vm.Tipo,
            Oggetto = vm.Oggetto.Trim(),
            Descrizione = vm.Descrizione.Trim(),
            FattiESoggetti = vm.FattiESoggetti,
            ClinicaId = string.IsNullOrEmpty(vm.ClinicaId) ? null : vm.ClinicaId,
            Anonima = vm.Anonima,
            FirmatarioNome = vm.Anonima ? null : vm.FirmatarioNome,
            FirmatarioEmail = vm.Anonima ? null : vm.FirmatarioEmail,
            FirmatarioRuolo = vm.Anonima ? null : vm.FirmatarioRuolo,
            Stato = StatoWhistleblowing.Aperta
        };

        if (vm.Allegato is { Length: > 0 })
        {
            if (vm.Allegato.Length > MaxBytes)
                ModelState.AddModelError(nameof(vm.Allegato), $"File troppo grande (max {MaxBytes / 1024 / 1024} MB).");
            else
            {
                var ext = Path.GetExtension(vm.Allegato.FileName).ToLowerInvariant();
                if (!AllowedExt.Contains(ext))
                    ModelState.AddModelError(nameof(vm.Allegato), "Estensione non consentita.");
                else
                {
                    await using var stream = vm.Allegato.OpenReadStream();
                    var stored = await _storage.SaveAsync(tid!, "whistleblowing", vm.Allegato.FileName, stream, vm.Allegato.ContentType);
                    s.AllegatoNome = vm.Allegato.FileName;
                    s.AllegatoPath = stored.RelativePath;
                    s.AllegatoSize = stored.SizeBytes;
                }
            }
        }

        await _mongo.Whistleblowing.InsertOneAsync(s);

        var tenant = await _mongo.Tenants.Find(t => t.Id == tid).FirstOrDefaultAsync();
        return View("Conferma", new WhistleblowingConfermaViewModel
        {
            CodiceTracciamento = s.CodiceTracciamento,
            TenantNome = tenant?.DisplayName ?? "Chipdent"
        });
    }

    [AllowAnonymous]
    [HttpGet("segui")]
    public IActionResult Segui()
    {
        ViewData["Section"] = "whistleblowing";
        return View(new WhistleblowingSeguiViewModel());
    }

    [AllowAnonymous]
    [HttpPost("segui")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Segui(WhistleblowingSeguiViewModel vm)
    {
        if (!ModelState.IsValid) { ViewData["Section"] = "whistleblowing"; return View(vm); }

        var s = await _mongo.Whistleblowing
            .Find(x => x.CodiceTracciamento == vm.CodiceTracciamento.Trim())
            .FirstOrDefaultAsync();
        if (s is null || string.IsNullOrEmpty(s.CodiceAccessoHash) || !_hasher.Verify(vm.CodiceAccesso, s.CodiceAccessoHash))
        {
            vm.Errore = "Codice di tracciamento o codice di accesso non validi.";
            ViewData["Section"] = "whistleblowing";
            return View(vm);
        }

        var clinicaNome = string.IsNullOrEmpty(s.ClinicaId) ? "—" :
            (await _mongo.Cliniche.Find(c => c.Id == s.ClinicaId).FirstOrDefaultAsync())?.Nome ?? "—";

        ViewData["Section"] = "whistleblowing";
        return View("Dettaglio", new WhistleblowingDettaglioPubblicoViewModel
        {
            Segnalazione = s,
            ClinicaNome = clinicaNome,
            CodiceAccessoVerificato = vm.CodiceAccesso
        });
    }

    [AllowAnonymous]
    [HttpPost("rispondi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rispondi(string codiceTracciamento, string codiceAccesso, string testo)
    {
        var s = await _mongo.Whistleblowing.Find(x => x.CodiceTracciamento == codiceTracciamento).FirstOrDefaultAsync();
        if (s is null || string.IsNullOrEmpty(s.CodiceAccessoHash) || !_hasher.Verify(codiceAccesso, s.CodiceAccessoHash))
            return Forbid();
        if (string.IsNullOrWhiteSpace(testo)) return RedirectToAction(nameof(Segui));

        var msg = new MessaggioWhistleblowing
        {
            DalSegnalante = true,
            AutoreNome = s.Anonima ? "Segnalante anonimo" : s.FirmatarioNome,
            Testo = testo.Trim()
        };
        await _mongo.Whistleblowing.UpdateOneAsync(
            x => x.Id == s.Id,
            Builders<SegnalazioneWhistleblowing>.Update.Push(x => x.Conversazione, msg));

        TempData["flash"] = "Messaggio inviato al Compliance Officer.";
        // Re-render con codice valido
        var tempVm = new WhistleblowingSeguiViewModel { CodiceTracciamento = codiceTracciamento, CodiceAccesso = codiceAccesso };
        return await Segui(tempVm);
    }

    // ─────────── Backend (Owner / Management) ───────────

    [Authorize(Policy = Policies.RequireManagement)]
    [HttpGet("admin")]
    public async Task<IActionResult> Admin(StatoWhistleblowing? filter = null)
    {
        var tid = _tenant.TenantId!;
        var filterDef = Builders<SegnalazioneWhistleblowing>.Filter.Eq(s => s.TenantId, tid);
        if (filter.HasValue) filterDef &= Builders<SegnalazioneWhistleblowing>.Filter.Eq(s => s.Stato, filter.Value);
        var items = await _mongo.Whistleblowing.Find(filterDef).SortByDescending(s => s.CreatedAt).ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        var aperte = (int)await _mongo.Whistleblowing.CountDocumentsAsync(
            Builders<SegnalazioneWhistleblowing>.Filter.Eq(s => s.TenantId, tid)
            & Builders<SegnalazioneWhistleblowing>.Filter.Eq(s => s.Stato, StatoWhistleblowing.Aperta));
        var inEsame = (int)await _mongo.Whistleblowing.CountDocumentsAsync(
            Builders<SegnalazioneWhistleblowing>.Filter.Eq(s => s.TenantId, tid)
            & Builders<SegnalazioneWhistleblowing>.Filter.Eq(s => s.Stato, StatoWhistleblowing.InEsame));

        ViewData["Section"] = "whistleblowing";
        return View(new WhistleblowingAdminIndexViewModel
        {
            Tutte = items.Select(s => new WhistleblowingAdminRow(s, string.IsNullOrEmpty(s.ClinicaId) ? "tutto il tenant" : cliniche.GetValueOrDefault(s.ClinicaId, "—"))).ToList(),
            Filter = filter,
            Aperte = aperte,
            InEsame = inEsame
        });
    }

    [Authorize(Policy = Policies.RequireManagement)]
    [HttpGet("admin/{id}")]
    public async Task<IActionResult> AdminDettaglio(string id)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Whistleblowing.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null) return NotFound();
        var clinica = string.IsNullOrEmpty(s.ClinicaId) ? null : await _mongo.Cliniche.Find(c => c.Id == s.ClinicaId).FirstOrDefaultAsync();

        ViewData["Section"] = "whistleblowing";
        return View(new WhistleblowingAdminDettaglioViewModel
        {
            Segnalazione = s,
            ClinicaNome = clinica?.Nome ?? "tutto il tenant"
        });
    }

    [Authorize(Policy = Policies.RequireManagement)]
    [HttpPost("admin/{id}/stato")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminCambiaStato(string id, StatoWhistleblowing stato, string? esito = null)
    {
        var tid = _tenant.TenantId!;
        var meId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var meName = User.Identity?.Name ?? "";

        var update = Builders<SegnalazioneWhistleblowing>.Update
            .Set(x => x.Stato, stato)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var existing = await _mongo.Whistleblowing.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (existing is null) return NotFound();

        if (string.IsNullOrEmpty(existing.GestitoDaUserId))
        {
            update = update
                .Set(x => x.GestitoDaUserId, meId)
                .Set(x => x.GestitoDaNome, meName)
                .Set(x => x.PresoInCaricoIl, DateTime.UtcNow);
        }

        if (stato == StatoWhistleblowing.Risolta || stato == StatoWhistleblowing.Archiviata || stato == StatoWhistleblowing.NonAmmissibile)
        {
            update = update.Set(x => x.DataChiusura, DateTime.UtcNow).Set(x => x.EsitoFinale, esito);
        }

        await _mongo.Whistleblowing.UpdateOneAsync(x => x.Id == id && x.TenantId == tid, update);
        TempData["flash"] = "Stato aggiornato.";
        return RedirectToAction(nameof(AdminDettaglio), new { id });
    }

    [Authorize(Policy = Policies.RequireManagement)]
    [HttpPost("admin/{id}/messaggio")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdminMessaggio(string id, string testo)
    {
        var tid = _tenant.TenantId!;
        if (string.IsNullOrWhiteSpace(testo)) return RedirectToAction(nameof(AdminDettaglio), new { id });
        var meName = User.Identity?.Name ?? "Compliance Officer";
        var msg = new MessaggioWhistleblowing
        {
            DalSegnalante = false,
            AutoreNome = meName,
            Testo = testo.Trim()
        };
        await _mongo.Whistleblowing.UpdateOneAsync(
            x => x.Id == id && x.TenantId == tid,
            Builders<SegnalazioneWhistleblowing>.Update.Push(x => x.Conversazione, msg));
        TempData["flash"] = "Messaggio aggiunto. Il segnalante lo vedrà accedendo col codice di tracciamento.";
        return RedirectToAction(nameof(AdminDettaglio), new { id });
    }

    [Authorize(Policy = Policies.RequireManagement)]
    [HttpGet("admin/{id}/allegato")]
    public async Task<IActionResult> AdminAllegato(string id)
    {
        var tid = _tenant.TenantId!;
        var s = await _mongo.Whistleblowing.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (s is null || string.IsNullOrEmpty(s.AllegatoPath)) return NotFound();
        var abs = Path.Combine(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath, s.AllegatoPath);
        if (!System.IO.File.Exists(abs)) return NotFound();
        return PhysicalFile(abs, "application/octet-stream", s.AllegatoNome ?? "allegato");
    }

    // ─────────── Helpers ───────────

    private async Task<string?> ResolveTenantIdAsync(string? slug)
    {
        if (!string.IsNullOrEmpty(_tenant.TenantId)) return _tenant.TenantId;
        if (string.IsNullOrEmpty(slug)) return null;
        var t = await _mongo.Tenants.Find(x => x.Slug == slug && x.IsActive).FirstOrDefaultAsync();
        return t?.Id;
    }

    /// <summary>Genera un codice tracciamento URL-safe stile WB-XXXX-XXXX-XXXX (gruppi di 4 caratteri alfanumerici).</summary>
    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // niente I, O, 0, 1, 8 per leggibilità
        var bytes = RandomNumberGenerator.GetBytes(12);
        var s = new char[12];
        for (var i = 0; i < 12; i++) s[i] = chars[bytes[i] % chars.Length];
        return $"WB-{new string(s, 0, 4)}-{new string(s, 4, 4)}-{new string(s, 8, 4)}";
    }
}
