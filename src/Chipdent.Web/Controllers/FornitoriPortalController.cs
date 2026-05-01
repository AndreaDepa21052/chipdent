using System.Security.Claims;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Sepa;
using Chipdent.Web.Infrastructure.Storage;
using Chipdent.Web.Infrastructure.Tenancy;
using Chipdent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Chipdent.Web.Controllers;

/// <summary>
/// Portale fornitori — area self-service riservata agli utenti con ruolo Fornitore.
/// Layout dedicato (_PortalLayout) e scope rigoroso: ogni query è filtrata per FornitoreId
/// estratto da User.LinkedPersonId.
/// </summary>
[Authorize(Policy = Policies.RequireFornitore)]
[Route("fornitori")]
public class FornitoriPortalController : Controller
{
    private const long MaxUploadBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".xml", ".p7m" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;

    public FornitoriPortalController(MongoContext mongo, ITenantContext tenant, IFileStorage storage)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
    }

    private string? FornitoreId => User.FindFirst("linked_person_id")?.Value;

    private async Task<Fornitore?> GetMyFornitoreAsync()
    {
        var fid = FornitoreId;
        if (string.IsNullOrEmpty(fid)) return null;
        return await _mongo.Fornitori.Find(f => f.Id == fid && f.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var fornitore = await GetMyFornitoreAsync();
        if (fornitore is null) return RedirectToAction("Login", "Account");

        var tid = _tenant.TenantId!;
        var oggi = DateTime.UtcNow.Date;

        var scadenze = await _mongo.ScadenzePagamento.Find(s => s.TenantId == tid && s.FornitoreId == fornitore.Id).ToListAsync();
        var fatture = await _mongo.Fatture.Find(f => f.TenantId == tid && f.FornitoreId == fornitore.Id)
            .SortByDescending(f => f.DataEmissione).ToListAsync();
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Nome);

        var aperte = scadenze.Where(s => s.Stato == StatoScadenza.DaPagare || s.Stato == StatoScadenza.Programmato).ToList();
        var passate = scadenze.Where(s => s.Stato == StatoScadenza.Pagato || s.Stato == StatoScadenza.Annullato || s.Stato == StatoScadenza.Insoluto).ToList();

        var fatturePerId = fatture.ToDictionary(f => f.Id);
        RigaScadenzaFornitore Map(ScadenzaPagamento s, DateTime today)
        {
            var fa = fatturePerId.GetValueOrDefault(s.FatturaId);
            var stato = (s.Stato == StatoScadenza.DaPagare && s.DataScadenza < today) ? StatoScadenza.Insoluto : s.Stato;
            return new RigaScadenzaFornitore
            {
                Id = s.Id,
                DataScadenza = s.DataScadenza,
                Importo = s.Importo,
                Stato = stato,
                Metodo = s.Metodo,
                ClinicaNome = cliniche.GetValueOrDefault(s.ClinicaId, "—"),
                NumeroFattura = fa?.Numero ?? "—",
                DataPagamento = s.DataPagamento,
                DataProgrammata = s.DataProgrammata
            };
        }

        var anno = oggi.Year;
        var totaleYTD = fatture.Where(f => f.DataEmissione.Year == anno && f.Stato != StatoFattura.Rifiutata).Sum(f => f.Totale);

        ViewData["Section"] = "fornitori-dashboard";
        return View(new PortaleFornitoreDashboardViewModel
        {
            Fornitore = fornitore,
            TotaleFatturatoYTD = totaleYTD,
            EspostoApertoTotale = aperte.Sum(s => s.Importo),
            FattureInApprovazione = fatture.Count(f => f.Stato == StatoFattura.Caricata),
            ScadenzeProssime30 = aperte.Count(s => s.DataScadenza >= oggi && s.DataScadenza <= oggi.AddDays(30)),
            ScadenzeScadute = aperte.Count(s => s.DataScadenza < oggi),
            ScadenzeAperte = aperte.OrderBy(s => s.DataScadenza).Select(s => Map(s, oggi)).ToList(),
            ScadenzePassate = passate.OrderByDescending(s => s.DataScadenza).Take(50).Select(s => Map(s, oggi)).ToList(),
            Fatture = fatture.Take(50).Select(f => new RigaFatturaFornitore
            {
                Id = f.Id,
                Numero = f.Numero,
                DataEmissione = f.DataEmissione,
                Totale = f.Totale,
                Stato = f.Stato,
                MotivoRifiuto = f.MotivoRifiuto,
                ClinicaNome = cliniche.GetValueOrDefault(f.ClinicaId, "—"),
                HasAllegato = !string.IsNullOrEmpty(f.AllegatoPath)
            }).ToList(),
            ClinicheLookup = cliniche
        });
    }

    [HttpGet("fattura/nuova")]
    public async Task<IActionResult> NuovaFattura()
    {
        var fornitore = await GetMyFornitoreAsync();
        if (fornitore is null) return RedirectToAction("Login", "Account");
        ViewData["Section"] = "fornitori-upload";
        return View(new FornitoreUploadFatturaViewModel
        {
            Categoria = fornitore.CategoriaDefault,
            Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync()
        });
    }

    [HttpPost("fattura/nuova")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> NuovaFattura(FornitoreUploadFatturaViewModel vm)
    {
        var fornitore = await GetMyFornitoreAsync();
        if (fornitore is null) return RedirectToAction("Login", "Account");

        if (vm.DataScadenza < vm.DataEmissione)
            ModelState.AddModelError(nameof(vm.DataScadenza), "La scadenza non può precedere la data di emissione.");

        if (!ModelState.IsValid)
        {
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
            ViewData["Section"] = "fornitori-upload";
            return View(vm);
        }

        var fattura = new FatturaFornitore
        {
            TenantId = _tenant.TenantId!,
            FornitoreId = fornitore.Id,
            ClinicaId = vm.ClinicaId,
            Numero = vm.Numero.Trim(),
            DataEmissione = DateTime.SpecifyKind(vm.DataEmissione.Date, DateTimeKind.Utc),
            MeseCompetenza = DateTime.SpecifyKind(new DateTime(vm.MeseCompetenza.Year, vm.MeseCompetenza.Month, 1), DateTimeKind.Utc),
            Categoria = vm.Categoria,
            Imponibile = vm.Imponibile,
            Iva = vm.Iva,
            Totale = vm.Imponibile + vm.Iva,
            Note = vm.Note,
            Origine = OrigineFattura.PortaleFornitore,
            CaricataDaUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Stato = StatoFattura.Caricata
        };

        if (vm.Allegato is { Length: > 0 })
        {
            if (vm.Allegato.Length > MaxUploadBytes)
            {
                ModelState.AddModelError(nameof(vm.Allegato), $"File troppo grande (max {MaxUploadBytes / (1024 * 1024)}MB).");
            }
            else
            {
                var ext = Path.GetExtension(vm.Allegato.FileName).ToLowerInvariant();
                if (!AllowedExtensions.Contains(ext))
                {
                    ModelState.AddModelError(nameof(vm.Allegato), $"Estensione non consentita: {ext}");
                }
                else
                {
                    await using var stream = vm.Allegato.OpenReadStream();
                    var stored = await _storage.SaveAsync(_tenant.TenantId!, "fatture", vm.Allegato.FileName, stream, vm.Allegato.ContentType);
                    fattura.AllegatoNome = vm.Allegato.FileName;
                    fattura.AllegatoPath = stored.RelativePath;
                    fattura.AllegatoSize = stored.SizeBytes;
                }
            }
        }

        if (!ModelState.IsValid)
        {
            vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();
            ViewData["Section"] = "fornitori-upload";
            return View(vm);
        }

        await _mongo.Fatture.InsertOneAsync(fattura);

        // Pre-creo la scadenza in stato "DaPagare" ma con flag implicito di approvazione pendente
        // (la scadenza vive solo se la fattura viene approvata). Per ora la teniamo allineata con
        // la fattura: se approvata genera scadenza, altrimenti niente.
        // Strategy: salvo in collection separata solo dopo approvazione owner.
        // Però per dare visibilità al fornitore della "data scadenza proposta", la inserisco
        // con Stato = DaPagare e sarà l'Owner a decidere se confermare.
        var scadenza = new ScadenzaPagamento
        {
            TenantId = _tenant.TenantId!,
            FatturaId = fattura.Id,
            FornitoreId = fattura.FornitoreId,
            ClinicaId = fattura.ClinicaId,
            Categoria = fattura.Categoria,
            DataScadenza = DateTime.SpecifyKind(vm.DataScadenza.Date, DateTimeKind.Utc),
            DataScadenzaAttesa = DateTime.SpecifyKind(
                PagamentiHelper.CalcolaScadenzaAttesa(fattura.DataEmissione, fornitore.TerminiPagamentoGiorni, fornitore.BasePagamento),
                DateTimeKind.Utc),
            Importo = fattura.Totale,
            Metodo = vm.Metodo,
            Iban = fornitore.Iban,
            Stato = StatoScadenza.DaPagare,
            Note = "Proposta dal fornitore — in attesa di approvazione fattura."
        };
        await _mongo.ScadenzePagamento.InsertOneAsync(scadenza);

        TempData["flash"] = "Fattura caricata. È in attesa di approvazione dall'amministrazione.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("fattura/{id}/scarica")]
    public async Task<IActionResult> ScaricaAllegato(string id)
    {
        var fid = FornitoreId;
        var f = await _mongo.Fatture.Find(x => x.Id == id && x.TenantId == _tenant.TenantId && x.FornitoreId == fid).FirstOrDefaultAsync();
        if (f is null || string.IsNullOrEmpty(f.AllegatoPath)) return NotFound();
        var abs = Path.Combine(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath, f.AllegatoPath);
        if (!System.IO.File.Exists(abs)) return NotFound();
        var ext = Path.GetExtension(f.AllegatoNome ?? "").ToLowerInvariant();
        var mime = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
        return PhysicalFile(abs, mime, f.AllegatoNome ?? "fattura");
    }

    [HttpGet("anagrafica")]
    public async Task<IActionResult> Anagrafica()
    {
        var f = await GetMyFornitoreAsync();
        if (f is null) return RedirectToAction("Login", "Account");
        ViewData["Section"] = "fornitori-anagrafica";
        return View(new FornitoreSelfAnagraficaViewModel
        {
            RagioneSociale = f.RagioneSociale,
            PartitaIva = f.PartitaIva,
            CodiceFiscale = f.CodiceFiscale,
            CodiceSdi = f.CodiceSdi,
            Pec = f.Pec,
            EmailContatto = f.EmailContatto,
            Telefono = f.Telefono,
            Indirizzo = f.Indirizzo,
            Iban = f.Iban
        });
    }

    [HttpPost("anagrafica")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Anagrafica(FornitoreSelfAnagraficaViewModel vm)
    {
        var f = await GetMyFornitoreAsync();
        if (f is null) return RedirectToAction("Login", "Account");
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "fornitori-anagrafica";
            return View(vm);
        }
        await _mongo.Fornitori.UpdateOneAsync(x => x.Id == f.Id,
            Builders<Fornitore>.Update
                .Set(x => x.RagioneSociale, vm.RagioneSociale.Trim())
                .Set(x => x.PartitaIva, vm.PartitaIva)
                .Set(x => x.CodiceFiscale, vm.CodiceFiscale)
                .Set(x => x.CodiceSdi, vm.CodiceSdi)
                .Set(x => x.Pec, vm.Pec)
                .Set(x => x.EmailContatto, vm.EmailContatto)
                .Set(x => x.Telefono, vm.Telefono)
                .Set(x => x.Indirizzo, vm.Indirizzo)
                .Set(x => x.Iban, vm.Iban)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Anagrafica aggiornata. Le modifiche all'IBAN saranno verificate dall'amministrazione prima del prossimo bonifico.";
        return RedirectToAction(nameof(Anagrafica));
    }
}
