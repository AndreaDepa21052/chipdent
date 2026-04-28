using System.Globalization;
using System.Text;
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

[Authorize(Policy = Policies.RequireBackoffice)]
[Route("contratti")]
public class ContrattiController : Controller
{
    private const long MaxUploadBytes = 50 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx", ".odt" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;

    public ContrattiController(MongoContext mongo, ITenantContext tenant, IFileStorage storage)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(StatoContratto? filter = null)
    {
        var tid = _tenant.TenantId!;
        var contratti = await _mongo.Contratti
            .Find(c => c.TenantId == tid)
            .SortByDescending(c => c.DataInizio)
            .ToListAsync();

        var dipendenti = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync())
            .ToDictionary(d => d.Id);
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        var rows = contratti.Select(c =>
        {
            var dip = dipendenti.GetValueOrDefault(c.DipendenteId);
            var clinicaNome = dip is null ? "—" : cliniche.GetValueOrDefault(dip.ClinicaId, "—");
            return new ContrattoRow(c,
                dip?.NomeCompleto ?? "— dipendente rimosso —",
                clinicaNome,
                dip?.Ruolo.ToString() ?? "—");
        }).ToList();

        var filtered = filter.HasValue ? rows.Where(r => r.Contratto.StatoCalcolato == filter.Value).ToList() : rows;

        ViewData["Section"] = "contratti";
        return View(new ContrattiIndexViewModel { Contratti = filtered, Filter = filter });
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Create(string? dipendenteId = null)
    {
        ViewData["Section"] = "contratti";
        ViewData["IsNew"] = true;
        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId && d.Stato != StatoDipendente.Cessato)
            .SortBy(d => d.Cognome).ToListAsync();
        return View("Form", new ContrattoFormViewModel
        {
            DipendenteId = dipendenteId ?? string.Empty,
            LockedDipendente = !string.IsNullOrEmpty(dipendenteId),
            Dipendenti = dipendenti
        });
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Create(ContrattoFormViewModel vm)
    {
        if (vm.DataFine.HasValue && vm.DataFine.Value < vm.DataInizio)
            ModelState.AddModelError(nameof(vm.DataFine), "La data di fine deve essere dopo quella di inizio.");

        if (!ModelState.IsValid || string.IsNullOrEmpty(vm.DipendenteId))
        {
            ViewData["Section"] = "contratti"; ViewData["IsNew"] = true;
            vm.Dipendenti = await DipendentiAsync();
            return View("Form", vm);
        }

        var c = new Contratto
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = vm.DipendenteId,
            Tipo = vm.Tipo,
            Livello = vm.Livello,
            RetribuzioneMensileLorda = vm.RetribuzioneMensileLorda,
            DataInizio = DateTime.SpecifyKind(vm.DataInizio.Date, DateTimeKind.Utc),
            DataFine = vm.DataFine.HasValue ? DateTime.SpecifyKind(vm.DataFine.Value.Date, DateTimeKind.Utc) : null,
            Note = vm.Note
        };

        if (vm.Allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(c, vm.Allegato);
            if (err is not null)
            {
                ModelState.AddModelError(nameof(vm.Allegato), err);
                ViewData["Section"] = "contratti"; ViewData["IsNew"] = true;
                vm.Dipendenti = await DipendentiAsync();
                return View("Form", vm);
            }
        }

        await _mongo.Contratti.InsertOneAsync(c);
        TempData["flash"] = "Contratto registrato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Edit(string id)
    {
        var c = await _mongo.Contratti.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (c is null) return NotFound();
        ViewData["Section"] = "contratti"; ViewData["IsNew"] = false;
        return View("Form", new ContrattoFormViewModel
        {
            Id = c.Id,
            DipendenteId = c.DipendenteId,
            Tipo = c.Tipo,
            Livello = c.Livello,
            RetribuzioneMensileLorda = c.RetribuzioneMensileLorda,
            DataInizio = c.DataInizio,
            DataFine = c.DataFine,
            Note = c.Note,
            AllegatoNomeAttuale = c.AllegatoNome,
            AllegatoPathAttuale = c.AllegatoPath,
            Dipendenti = await DipendentiAsync(),
            LockedDipendente = true
        });
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Edit(string id, ContrattoFormViewModel vm)
    {
        var existing = await _mongo.Contratti.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (existing is null) return NotFound();

        if (vm.DataFine.HasValue && vm.DataFine.Value < vm.DataInizio)
            ModelState.AddModelError(nameof(vm.DataFine), "La data di fine deve essere dopo quella di inizio.");

        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "contratti"; ViewData["IsNew"] = false;
            vm.Dipendenti = await DipendentiAsync();
            vm.AllegatoNomeAttuale = existing.AllegatoNome;
            vm.AllegatoPathAttuale = existing.AllegatoPath;
            return View("Form", vm);
        }

        var update = Builders<Contratto>.Update
            .Set(x => x.Tipo, vm.Tipo)
            .Set(x => x.Livello, vm.Livello)
            .Set(x => x.RetribuzioneMensileLorda, vm.RetribuzioneMensileLorda)
            .Set(x => x.DataInizio, DateTime.SpecifyKind(vm.DataInizio.Date, DateTimeKind.Utc))
            .Set(x => x.DataFine, vm.DataFine.HasValue ? DateTime.SpecifyKind(vm.DataFine.Value.Date, DateTimeKind.Utc) : (DateTime?)null)
            .Set(x => x.Note, vm.Note)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        if (vm.Allegato is { Length: > 0 })
        {
            var temp = new Contratto();
            var err = await TryAttachAsync(temp, vm.Allegato);
            if (err is not null)
            {
                ModelState.AddModelError(nameof(vm.Allegato), err);
                ViewData["Section"] = "contratti"; ViewData["IsNew"] = false;
                vm.Dipendenti = await DipendentiAsync();
                vm.AllegatoNomeAttuale = existing.AllegatoNome;
                vm.AllegatoPathAttuale = existing.AllegatoPath;
                return View("Form", vm);
            }
            if (!string.IsNullOrEmpty(existing.AllegatoPath))
                await _storage.DeleteAsync(_tenant.TenantId!, existing.AllegatoPath);
            update = update
                .Set(x => x.AllegatoNome, temp.AllegatoNome)
                .Set(x => x.AllegatoPath, temp.AllegatoPath)
                .Set(x => x.AllegatoSize, temp.AllegatoSize);
        }

        await _mongo.Contratti.UpdateOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, update);
        TempData["flash"] = "Contratto aggiornato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var existing = await _mongo.Contratti.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (existing is not null && !string.IsNullOrEmpty(existing.AllegatoPath))
            await _storage.DeleteAsync(_tenant.TenantId!, existing.AllegatoPath);
        await _mongo.Contratti.DeleteOneAsync(x => x.Id == id && x.TenantId == _tenant.TenantId);
        TempData["flash"] = "Contratto eliminato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/scarica")]
    public async Task<IActionResult> Download(string id)
    {
        var c = await _mongo.Contratti.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (c is null || string.IsNullOrEmpty(c.AllegatoPath)) return NotFound();
        var abs = Path.Combine(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath, c.AllegatoPath);
        if (!System.IO.File.Exists(abs)) return NotFound();
        var ext = Path.GetExtension(c.AllegatoNome ?? "").ToLowerInvariant();
        var mime = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
        return PhysicalFile(abs, mime, c.AllegatoNome ?? "contratto");
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var tid = _tenant.TenantId!;
        var contratti = await _mongo.Contratti.Find(c => c.TenantId == tid).ToListAsync();
        var dipendenti = (await _mongo.Dipendenti.Find(d => d.TenantId == tid).ToListAsync())
            .ToDictionary(d => d.Id);
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        var sb = new StringBuilder();
        sb.Append('﻿'); // UTF-8 BOM (Excel)
        sb.AppendLine("Dipendente;CodiceFiscale;Email;Sede;Tipo;Livello;DataInizio;DataFine;Stato;Retribuzione;Allegato");
        foreach (var c in contratti.OrderByDescending(x => x.DataInizio))
        {
            var dip = dipendenti.GetValueOrDefault(c.DipendenteId);
            var sede = dip is null ? "—" : cliniche.GetValueOrDefault(dip.ClinicaId, "—");
            sb.Append(Csv(dip?.NomeCompleto)).Append(';')
              .Append(Csv(dip?.CodiceFiscale)).Append(';')
              .Append(Csv(dip?.Email)).Append(';')
              .Append(Csv(sede)).Append(';')
              .Append(Csv(c.Tipo.ToString())).Append(';')
              .Append(Csv(c.Livello)).Append(';')
              .Append(c.DataInizio.ToString("yyyy-MM-dd")).Append(';')
              .Append(c.DataFine?.ToString("yyyy-MM-dd") ?? "").Append(';')
              .Append(c.StatoCalcolato).Append(';')
              .Append(c.RetribuzioneMensileLorda?.ToString(CultureInfo.InvariantCulture) ?? "").Append(';')
              .Append(Csv(c.AllegatoNome))
              .AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"contratti-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private async Task<string?> TryAttachAsync(Contratto target, Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (file.Length > MaxUploadBytes) return $"File troppo grande (max {MaxUploadBytes / (1024 * 1024)}MB).";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return $"Estensione non consentita: {ext}";

        await using var stream = file.OpenReadStream();
        var stored = await _storage.SaveAsync(_tenant.TenantId!, "contratti", file.FileName, stream, file.ContentType);
        target.AllegatoNome = file.FileName;
        target.AllegatoPath = stored.RelativePath;
        target.AllegatoSize = stored.SizeBytes;
        return null;
    }

    private Task<List<Dipendente>> DipendentiAsync()
        => _mongo.Dipendenti.Find(d => d.TenantId == _tenant.TenantId && d.Stato != StatoDipendente.Cessato)
                            .SortBy(d => d.Cognome).ToListAsync();
}
