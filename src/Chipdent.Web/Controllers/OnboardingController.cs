using System.Security.Cryptography;
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

[Authorize(Policy = Policies.RequireManagement)]
[Route("workspace/onboarding")]
public class OnboardingController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;
    private readonly INotificationPublisher _publisher;

    public OnboardingController(MongoContext mongo, ITenantContext tenant, IFileStorage storage, INotificationPublisher publisher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
        _publisher = publisher;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int? step = null)
    {
        var state = await BuildStateAsync(step);
        ViewData["Section"] = "onboarding";
        return View(state);
    }

    // ───── Step 1: Branding ─────

    [HttpPost("branding")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> SaveBranding(OnboardingBrandingForm form)
    {
        var tid = _tenant.TenantId!;
        var t = await _mongo.Tenants.Find(x => x.Id == tid).FirstOrDefaultAsync();
        if (t is null) return NotFound();

        var update = Builders<Tenant>.Update
            .Set(x => x.DisplayName, form.DisplayName.Trim())
            .Set(x => x.Descrizione, form.Descrizione)
            .Set(x => x.PrimaryColor, form.PrimaryColor)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        if (form.Logo is { Length: > 0 })
        {
            var ext = Path.GetExtension(form.Logo.FileName).ToLowerInvariant();
            var allowed = new HashSet<string> { ".png", ".jpg", ".jpeg", ".webp", ".svg" };
            if (allowed.Contains(ext))
            {
                if (!string.IsNullOrEmpty(t.LogoPath)) await _storage.DeleteAsync(tid, t.LogoPath);
                await using var stream = form.Logo.OpenReadStream();
                var stored = await _storage.SaveAsync(tid, "branding", "logo" + ext, stream, form.Logo.ContentType);
                update = update.Set(x => x.LogoPath, stored.RelativePath);
            }
        }

        await _mongo.Tenants.UpdateOneAsync(x => x.Id == tid, update);
        TempData["flash"] = "✓ Branding salvato.";
        return RedirectToAction(nameof(Index), new { step = 2 });
    }

    // ───── Step 2: Prima clinica ─────

    [HttpPost("clinica")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveClinica(OnboardingClinicaForm form)
    {
        await _mongo.Cliniche.InsertOneAsync(new Clinica
        {
            TenantId = _tenant.TenantId!,
            Nome = form.Nome.Trim(),
            Citta = form.Citta.Trim(),
            Indirizzo = form.Indirizzo.Trim(),
            NumeroRiuniti = form.NumeroRiuniti,
            OrganicoTarget = form.OrganicoTarget,
            Stato = ClinicaStato.Operativa
        });
        TempData["flash"] = "✓ Prima clinica creata.";
        return RedirectToAction(nameof(Index), new { step = 3 });
    }

    // ───── Step 3: Primo invito ─────

    [HttpPost("invito")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveInvito(OnboardingInvitoForm form)
    {
        var tid = _tenant.TenantId!;
        var existing = await _mongo.Users.Find(u => u.Email == form.Email && u.TenantId == tid).AnyAsync();
        if (existing)
        {
            TempData["flash"] = "Esiste già un utente con questa email in questo workspace.";
            return RedirectToAction(nameof(Index), new { step = 3 });
        }
        var meId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        var invito = new Invito
        {
            TenantId = tid,
            Email = form.Email.Trim().ToLowerInvariant(),
            FullName = form.FullName.Trim(),
            Ruolo = form.Ruolo,
            Token = GenerateToken(),
            ScadeIl = DateTime.UtcNow.AddDays(7),
            InvitatoDaUserId = meId
        };
        await _mongo.Inviti.InsertOneAsync(invito);

        var url = Url.Action("Accept", "Account", new { token = invito.Token }, Request.Scheme);
        TempData["flash"] = $"✓ Invito creato. Link da inviare a {invito.Email}: {url}";
        return RedirectToAction(nameof(Index), new { step = 4 });
    }

    // ───── Step 4: Primo template turno ─────

    [HttpPost("template")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(OnboardingTemplateForm form)
    {
        await _mongo.TurniTemplate.InsertOneAsync(new TurnoTemplate
        {
            TenantId = _tenant.TenantId!,
            Nome = form.Nome.Trim(),
            OraInizio = form.OraInizio,
            OraFine = form.OraFine,
            ColoreHex = form.ColoreHex,
            Attivo = true
        });
        TempData["flash"] = "🎉 Onboarding completato! Benvenuto su Chipdent.";
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost("skip")]
    [ValidateAntiForgeryToken]
    public IActionResult Skip(int step)
    {
        var next = Math.Min(5, step + 1);
        if (next >= 5) return RedirectToAction("Index", "Dashboard");
        return RedirectToAction(nameof(Index), new { step = next });
    }

    // ───── Helpers ─────

    private async Task<OnboardingStateViewModel> BuildStateAsync(int? requestedStep)
    {
        var tid = _tenant.TenantId!;
        var t = await _mongo.Tenants.Find(x => x.Id == tid).FirstOrDefaultAsync();
        var nClin = await _mongo.Cliniche.CountDocumentsAsync(c => c.TenantId == tid);
        var nInv = await _mongo.Inviti.CountDocumentsAsync(i => i.TenantId == tid);
        var nTpl = await _mongo.TurniTemplate.CountDocumentsAsync(x => x.TenantId == tid && x.Attivo);

        var state = new OnboardingStateViewModel
        {
            TenantNome = t?.DisplayName ?? "Workspace",
            HasLogo = !string.IsNullOrEmpty(t?.LogoPath),
            HasClinica = nClin > 0,
            HasInvito = nInv > 0,
            HasTemplate = nTpl > 0,
            Branding = new OnboardingBrandingForm
            {
                DisplayName = t?.DisplayName ?? "",
                Descrizione = t?.Descrizione,
                PrimaryColor = t?.PrimaryColor ?? "#c47830"
            }
        };

        // Determina lo step di default: il primo non completato
        if (requestedStep is null or < 1 or > 4)
        {
            if (!state.HasLogo) state.Step = 1;
            else if (!state.HasClinica) state.Step = 2;
            else if (!state.HasInvito) state.Step = 3;
            else if (!state.HasTemplate) state.Step = 4;
            else state.Step = 4;
        }
        else state.Step = requestedStep.Value;

        return state;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
