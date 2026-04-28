using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

[Authorize]
[Route("miei-documenti")]
public class MieiDocumentiController : Controller
{
    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IWebHostEnvironment _env;

    public MieiDocumentiController(MongoContext mongo, ITenantContext tenant, IWebHostEnvironment env)
    {
        _mongo = mongo;
        _tenant = tenant;
        _env = env;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tid = _tenant.TenantId!;
        var linkedType = User.LinkedPersonType();
        var linkedId = User.LinkedPersonId();

        ViewData["Section"] = "miei-documenti";

        if (linkedType != "Dipendente" || string.IsNullOrEmpty(linkedId))
        {
            return View(new MieiDocumentiViewModel { HasLinkedPerson = false });
        }

        var dip = await _mongo.Dipendenti.Find(d => d.Id == linkedId && d.TenantId == tid).FirstOrDefaultAsync();
        if (dip is null) return View(new MieiDocumentiViewModel { HasLinkedPerson = false });

        var clinica = await _mongo.Cliniche.Find(c => c.Id == dip.ClinicaId && c.TenantId == tid).FirstOrDefaultAsync();

        var contratti = await _mongo.Contratti
            .Find(c => c.TenantId == tid && c.DipendenteId == dip.Id)
            .SortByDescending(c => c.DataInizio).ToListAsync();
        var oggi = DateTime.UtcNow;
        var corrente = contratti.FirstOrDefault(c => c.DataInizio <= oggi && (c.DataFine is null || c.DataFine >= oggi));
        var storici = contratti.Where(c => c != corrente).ToList();

        var visite = await _mongo.VisiteMediche
            .Find(v => v.TenantId == tid && v.DipendenteId == dip.Id)
            .SortByDescending(v => v.Data).ToListAsync();

        var corsi = await _mongo.Corsi
            .Find(c => c.TenantId == tid && c.DestinatarioTipo == DestinatarioCorso.Dipendente && c.DestinatarioId == dip.Id)
            .SortByDescending(c => c.DataConseguimento).ToListAsync();

        return View(new MieiDocumentiViewModel
        {
            DipendenteNome = dip.NomeCompleto,
            RuoloDipendente = dip.Ruolo.ToString(),
            ClinicaNome = clinica?.Nome,
            HasLinkedPerson = true,
            ContrattoAttuale = corrente,
            ContrattiStorici = storici,
            Visite = visite,
            Corsi = corsi
        });
    }

    [HttpGet("contratto/{id}")]
    public async Task<IActionResult> ScaricaContratto(string id)
        => await ScaricaPersonale<Contratto>(id, c => c.DipendenteId, c => c.AllegatoPath, c => c.AllegatoNome);

    [HttpGet("visita/{id}")]
    public async Task<IActionResult> ScaricaVisita(string id)
        => await ScaricaPersonale<VisitaMedica>(id, v => v.DipendenteId, v => v.AllegatoPath, v => v.AllegatoNome);

    [HttpGet("corso/{id}")]
    public async Task<IActionResult> ScaricaCorso(string id)
    {
        var tid = _tenant.TenantId!;
        var linkedId = User.LinkedPersonId();
        if (User.LinkedPersonType() != "Dipendente" || string.IsNullOrEmpty(linkedId)) return Forbid();

        var c = await _mongo.Corsi.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (c is null || c.DestinatarioTipo != DestinatarioCorso.Dipendente || c.DestinatarioId != linkedId) return NotFound();
        if (string.IsNullOrEmpty(c.AttestatoPath)) return NotFound();
        return ServePhysical(c.AttestatoPath, c.AttestatoNome);
    }

    private async Task<IActionResult> ScaricaPersonale<T>(string id,
        Func<T, string> ownerSelector, Func<T, string?> pathSelector, Func<T, string?> nameSelector)
        where T : Chipdent.Web.Domain.Common.TenantEntity
    {
        var tid = _tenant.TenantId!;
        var linkedId = User.LinkedPersonId();
        if (User.LinkedPersonType() != "Dipendente" || string.IsNullOrEmpty(linkedId)) return Forbid();

        var coll = typeof(T) == typeof(Contratto) ? (object)_mongo.Contratti
                : typeof(T) == typeof(VisitaMedica) ? _mongo.VisiteMediche
                : null;
        if (coll is null) return NotFound();

        T? doc = null;
        if (typeof(T) == typeof(Contratto))
        {
            doc = (T?)(object?)await _mongo.Contratti.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        }
        else if (typeof(T) == typeof(VisitaMedica))
        {
            doc = (T?)(object?)await _mongo.VisiteMediche.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        }
        if (doc is null) return NotFound();
        if (ownerSelector(doc) != linkedId) return Forbid();
        var path = pathSelector(doc);
        if (string.IsNullOrEmpty(path)) return NotFound();
        return ServePhysical(path, nameSelector(doc));
    }

    private IActionResult ServePhysical(string relativePath, string? originalName)
    {
        var abs = Path.Combine(_env.WebRootPath, relativePath);
        if (!System.IO.File.Exists(abs)) return NotFound();
        var ext = Path.GetExtension(originalName ?? relativePath).ToLowerInvariant();
        var mime = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
        return PhysicalFile(abs, mime, originalName ?? "documento");
    }
}
