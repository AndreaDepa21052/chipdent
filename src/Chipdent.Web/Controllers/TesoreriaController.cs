using System.Globalization;
using System.Security.Claims;
using System.Text;
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
/// Tesoreria — scadenziario pagamenti fornitori.
/// Accessibile a Owner + Management + Backoffice: tutte e tre le figure possono vedere
/// lo scadenziario, gestire l'anagrafica fornitori, caricare e approvare fatture, segnare
/// pagamenti e generare distinte SEPA. Direttore e Staff esclusi (la tesoreria è cross-sede).
/// </summary>
[Authorize(Policy = Policies.RequireTesoreria)]
[Route("tesoreria")]
public class TesoreriaController : Controller
{
    private const long MaxUploadBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".xml", ".p7m" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IFileStorage _storage;
    private readonly IPasswordHasher _hasher;

    public TesoreriaController(MongoContext mongo, ITenantContext tenant, IFileStorage storage, IPasswordHasher hasher)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
        _hasher = hasher;
    }

    // ─────────────────────────────────────────────────────────────
    //  DASHBOARD
    // ─────────────────────────────────────────────────────────────
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] TesoreriaFilter? filtro = null)
    {
        filtro ??= new TesoreriaFilter();
        var tid = _tenant.TenantId!;
        var oggi = DateTime.UtcNow.Date;

        var fornitori = await _mongo.Fornitori.Find(f => f.TenantId == tid).ToListAsync();
        var fornitoriById = fornitori.ToDictionary(f => f.Id);
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var clinicheById = cliniche.ToDictionary(c => c.Id);
        var fatture = await _mongo.Fatture.Find(f => f.TenantId == tid).ToListAsync();
        var fatturePerId = fatture.ToDictionary(f => f.Id);

        var scadenze = await _mongo.ScadenzePagamento.Find(s => s.TenantId == tid).ToListAsync();

        // Stato derivato: scadenze "DaPagare" con data passata sono "Insolute" lato view
        // (non riscriviamo il record per non perdere il momento del cambio di stato volontario).
        IEnumerable<ScadenzaPagamento> filtered = scadenze;
        if (!string.IsNullOrEmpty(filtro.FornitoreId))
            filtered = filtered.Where(s => s.FornitoreId == filtro.FornitoreId);
        if (!string.IsNullOrEmpty(filtro.ClinicaId))
            filtered = filtered.Where(s => s.ClinicaId == filtro.ClinicaId);
        if (filtro.Categoria.HasValue)
            filtered = filtered.Where(s => s.Categoria == filtro.Categoria.Value);
        if (filtro.Stato.HasValue)
        {
            var voluto = filtro.Stato.Value;
            filtered = filtered.Where(s => DerivedStato(s, oggi) == voluto);
        }
        if (filtro.Metodo.HasValue)
            filtered = filtered.Where(s => s.Metodo == filtro.Metodo.Value);
        if (filtro.Dal.HasValue)
            filtered = filtered.Where(s => s.DataScadenza >= filtro.Dal.Value.Date);
        if (filtro.Al.HasValue)
            filtered = filtered.Where(s => s.DataScadenza <= filtro.Al.Value.Date);
        if (!string.IsNullOrWhiteSpace(filtro.Q))
        {
            var q = filtro.Q.Trim().ToLowerInvariant();
            filtered = filtered.Where(s =>
            {
                var f = fornitoriById.GetValueOrDefault(s.FornitoreId);
                var fa = fatturePerId.GetValueOrDefault(s.FatturaId);
                return (f?.RagioneSociale.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                       || (fa?.Numero.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                       || (s.Note?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
            });
        }

        var righe = filtered
            .OrderBy(s => s.Stato == StatoScadenza.Pagato ? 1 : 0)
            .ThenBy(s => s.DataScadenza)
            .Select(s =>
            {
                var f = fornitoriById.GetValueOrDefault(s.FornitoreId);
                var fa = fatturePerId.GetValueOrDefault(s.FatturaId);
                var c = clinicheById.GetValueOrDefault(s.ClinicaId);
                return new RigaTesoreria
                {
                    ScadenzaId = s.Id,
                    FatturaId = s.FatturaId,
                    DataScadenza = s.DataScadenza,
                    DataScadenzaAttesa = s.DataScadenzaAttesa,
                    MeseCompetenza = fa?.MeseCompetenza.ToString("MMM yy", new CultureInfo("it-IT")) ?? "—",
                    Loc = SiglaSede(c),
                    ClinicaId = s.ClinicaId,
                    NumeroDoc = fa?.Numero ?? "—",
                    FornitoreNome = f?.RagioneSociale ?? "— fornitore rimosso —",
                    FornitoreId = s.FornitoreId,
                    Imponibile = fa?.Imponibile ?? 0,
                    Iva = fa?.Iva ?? 0,
                    Totale = s.Importo,
                    Metodo = s.Metodo,
                    Stato = DerivedStato(s, oggi),
                    Categoria = s.Categoria,
                    Note = s.Note ?? fa?.Note,
                    Iban = s.Iban,
                    FlagBM = fa?.BonificoMultiploCbi ?? false,
                    FlagEM = fa?.FlagEM,
                    TipoEmissione = fa?.TipoEmissione ?? TipoEmissioneFattura.NonSpecificato,
                    BonificoMultiploCbi = fa?.BonificoMultiploCbi ?? false,
                    IsHolding = c?.IsHolding ?? false,
                    ScadenzaPadreId = s.ScadenzaPadreId,
                    HasAllegato = !string.IsNullOrEmpty(fa?.AllegatoPath)
                };
            }).ToList();

        // Conta i mismatch sull'intero set filtrato (prima del filtro "solo fuori termini")
        // per avere un KPI stabile.
        var fuoriTerminiCount = righe.Count(r => r.ScadenzaFuoriTermini);
        if (filtro.SoloFuoriTermini)
        {
            righe = righe.Where(r => r.ScadenzaFuoriTermini).ToList();
        }

        // ── KPI ─────────────────────────────────────────────────
        var aperte = scadenze.Where(s => s.Stato == StatoScadenza.DaPagare || s.Stato == StatoScadenza.Programmato).ToList();
        var espostoProssimi30 = aperte.Where(s => s.DataScadenza >= oggi && s.DataScadenza <= oggi.AddDays(30)).Sum(s => s.Importo);
        var scadute = aperte.Where(s => s.DataScadenza < oggi).ToList();
        var fineSettimana = oggi.AddDays(7 - (int)oggi.DayOfWeek);
        var settSet = aperte.Where(s => s.Stato == StatoScadenza.DaPagare && s.DataScadenza >= oggi && s.DataScadenza <= fineSettimana).ToList();
        var pagatoMese = scadenze.Where(s => s.Stato == StatoScadenza.Pagato && s.DataPagamento.HasValue
                                           && s.DataPagamento.Value.Year == oggi.Year
                                           && s.DataPagamento.Value.Month == oggi.Month).Sum(s => s.Importo);
        var fattureInApprovazione = fatture.Count(f => f.Stato == StatoFattura.Caricata);

        // ── Top fornitori per esposto ───────────────────────────
        var top = aperte
            .GroupBy(s => s.FornitoreId)
            .Select(g => new TopFornitoreRow
            {
                FornitoreId = g.Key,
                Nome = fornitoriById.GetValueOrDefault(g.Key)?.RagioneSociale ?? "—",
                Esposto = g.Sum(x => x.Importo),
                NumeroScadenze = g.Count()
            })
            .OrderByDescending(t => t.Esposto)
            .Take(5)
            .ToList();

        // ── Grafico: spesa per categoria ultimi 12 mesi ─────────
        var dodiciMesiFa = new DateTime(oggi.Year, oggi.Month, 1).AddMonths(-11);
        var perCategoria = scadenze
            .Where(s => s.Stato == StatoScadenza.Pagato || s.Stato == StatoScadenza.DaPagare || s.Stato == StatoScadenza.Programmato)
            .Where(s => s.DataScadenza >= dodiciMesiFa)
            .GroupBy(s => s.Categoria)
            .Select(g => new SerieMese(g.Key.ToString(), g.Sum(x => x.Importo)))
            .OrderByDescending(x => x.Valore)
            .ToList();

        // ── Grafico: cash-out previsto prossimi 90gg ────────────
        var futuro = aperte.Where(s => s.DataScadenza >= oggi && s.DataScadenza <= oggi.AddDays(90))
            .GroupBy(s => new DateTime(s.DataScadenza.Year, s.DataScadenza.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new SerieMese(g.Key.ToString("MMM yy", new CultureInfo("it-IT")), g.Sum(x => x.Importo)))
            .ToList();

        // ── Grafico: spesa per sede ────────────────────────────
        var perSede = scadenze
            .Where(s => s.DataScadenza >= dodiciMesiFa)
            .GroupBy(s => s.ClinicaId)
            .Select(g =>
            {
                var c = clinicheById.GetValueOrDefault(g.Key);
                return new SerieClinica(SiglaSede(c), c?.Nome ?? "—", g.Sum(x => x.Importo));
            })
            .OrderByDescending(x => x.Valore)
            .ToList();

        // ── Grafico: ripartizione stati ────────────────────────
        var ripartizione = scadenze.GroupBy(s => DerivedStato(s, oggi))
            .ToDictionary(g => g.Key.ToString(), g => g.Sum(x => x.Importo));

        var vm = new TesoreriaIndexViewModel
        {
            EspostoProssimi30gg = espostoProssimi30,
            ImportoScaduto = scadute.Sum(s => s.Importo),
            CountScadute = scadute.Count,
            DaProgrammareSettimana = settSet.Sum(s => s.Importo),
            CountDaProgrammareSettimana = settSet.Count,
            PagatoMeseCorrente = pagatoMese,
            CountFattureInApprovazione = fattureInApprovazione,
            CountFuoriTermini = fuoriTerminiCount,
            Righe = righe,
            TopFornitori = top,
            Filtro = filtro,
            CliniceLookup = cliniche.OrderBy(c => c.Nome).Select(c => (c.Id, c.Nome)).ToList(),
            FornitoriLookup = fornitori.OrderBy(f => f.RagioneSociale).Select(f => (f.Id, f.RagioneSociale)).ToList(),
            SpesaPerCategoria12m = perCategoria,
            CashOutFuturo90gg = futuro,
            SpesaPerSede = perSede,
            RipartizioneStati = ripartizione
        };

        ViewData["Section"] = "tesoreria";
        return View(vm);
    }

    // ─────────────────────────────────────────────────────────────
    //  FATTURE — CREATE / APPROVE / REJECT
    // ─────────────────────────────────────────────────────────────
    [HttpGet("fattura/nuova")]
    public async Task<IActionResult> NuovaFattura()
    {
        ViewData["Section"] = "tesoreria";
        var vm = new FatturaFormViewModel
        {
            Fornitori = await _mongo.Fornitori.Find(f => f.TenantId == _tenant.TenantId && f.Stato == StatoFornitore.Attivo)
                .SortBy(f => f.RagioneSociale).ToListAsync(),
            Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId)
                .SortBy(c => c.Nome).ToListAsync()
        };
        return View("FatturaForm", vm);
    }

    [HttpPost("fattura/nuova")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> NuovaFattura(FatturaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateFatturaLookups(vm);
            ViewData["Section"] = "tesoreria";
            return View("FatturaForm", vm);
        }

        var fattura = new FatturaFornitore
        {
            TenantId = _tenant.TenantId!,
            FornitoreId = vm.FornitoreId,
            ClinicaId = vm.ClinicaId,
            Numero = vm.Numero.Trim(),
            DataEmissione = DateTime.SpecifyKind(vm.DataEmissione.Date, DateTimeKind.Utc),
            MeseCompetenza = DateTime.SpecifyKind(new DateTime(vm.MeseCompetenza.Year, vm.MeseCompetenza.Month, 1), DateTimeKind.Utc),
            Categoria = vm.Categoria,
            Imponibile = vm.Imponibile,
            Iva = vm.Iva,
            Totale = vm.Imponibile + vm.Iva,
            Note = vm.Note,
            FlagEM = vm.FlagEM,
            BonificoMultiploCbi = vm.FlagBM,
            TipoEmissione = vm.FlagEM?.Trim().ToUpperInvariant() switch
            {
                "E" => TipoEmissioneFattura.Elettronica,
                "M" => TipoEmissioneFattura.Manuale,
                _   => TipoEmissioneFattura.NonSpecificato
            },
            Origine = OrigineFattura.Backoffice,
            CaricataDaUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Stato = StatoFattura.Approvata,           // back-office: approvata di default
            ApprovataIl = DateTime.UtcNow,
            ApprovataDaUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        };

        if (vm.Allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(fattura, vm.Allegato);
            if (err is not null)
            {
                ModelState.AddModelError(nameof(vm.Allegato), err);
                await PopulateFatturaLookups(vm);
                ViewData["Section"] = "tesoreria";
                return View("FatturaForm", vm);
            }
        }

        await _mongo.Fatture.InsertOneAsync(fattura);

        // Crea automaticamente la prima scadenza
        var fornitore = await _mongo.Fornitori.Find(f => f.Id == vm.FornitoreId).FirstOrDefaultAsync();
        var scadenza = new ScadenzaPagamento
        {
            TenantId = _tenant.TenantId!,
            FatturaId = fattura.Id,
            FornitoreId = fattura.FornitoreId,
            ClinicaId = fattura.ClinicaId,
            Categoria = fattura.Categoria,
            DataScadenza = DateTime.SpecifyKind(vm.DataScadenza.Date, DateTimeKind.Utc),
            DataScadenzaAttesa = fornitore is null
                ? null
                : DateTime.SpecifyKind(
                    PagamentiHelper.CalcolaScadenzaAttesa(fattura.DataEmissione, fornitore.TerminiPagamentoGiorni, fornitore.BasePagamento),
                    DateTimeKind.Utc),
            Importo = fattura.Totale,
            Metodo = vm.Metodo,
            Iban = fornitore?.Iban,
            Stato = StatoScadenza.DaPagare
        };
        await _mongo.ScadenzePagamento.InsertOneAsync(scadenza);

        TempData["flash"] = "Fattura registrata e scadenza creata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("fattura/{id}/approva")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovaFattura(string id, DateTime? dataScadenza = null, MetodoPagamento? metodo = null)
    {
        var f = await _mongo.Fatture.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (f is null) return NotFound();
        if (f.Stato == StatoFattura.Approvata)
        {
            TempData["flash"] = "Fattura già approvata.";
            return RedirectToAction(nameof(Index));
        }

        // Se la fattura arriva dal portale fornitore esiste già una scadenza pre-creata
        // (in stato DaPagare con nota "Proposta dal fornitore"). La promuoviamo invece di duplicarla.
        var scadenzaEsistente = await _mongo.ScadenzePagamento
            .Find(s => s.FatturaId == id && s.TenantId == _tenant.TenantId).FirstOrDefaultAsync();

        var fornitore = await _mongo.Fornitori.Find(x => x.Id == f.FornitoreId).FirstOrDefaultAsync();
        var scadenzaAttesa = fornitore is null
            ? (DateTime?)null
            : DateTime.SpecifyKind(
                PagamentiHelper.CalcolaScadenzaAttesa(f.DataEmissione, fornitore.TerminiPagamentoGiorni, fornitore.BasePagamento),
                DateTimeKind.Utc);

        if (scadenzaEsistente is not null)
        {
            var update = Builders<ScadenzaPagamento>.Update
                .Set(s => s.DataScadenzaAttesa, scadenzaAttesa)
                .Set(s => s.UpdatedAt, DateTime.UtcNow);
            if (dataScadenza.HasValue)
                update = update.Set(s => s.DataScadenza, DateTime.SpecifyKind(dataScadenza.Value.Date, DateTimeKind.Utc));
            if (metodo.HasValue)
                update = update.Set(s => s.Metodo, metodo.Value);
            // Rimuovo la nota di proposta (è ora confermata)
            update = update.Unset(s => s.Note);
            await _mongo.ScadenzePagamento.UpdateOneAsync(s => s.Id == scadenzaEsistente.Id, update);
        }
        else
        {
            await _mongo.ScadenzePagamento.InsertOneAsync(new ScadenzaPagamento
            {
                TenantId = _tenant.TenantId!,
                FatturaId = f.Id,
                FornitoreId = f.FornitoreId,
                ClinicaId = f.ClinicaId,
                Categoria = f.Categoria,
                DataScadenza = DateTime.SpecifyKind((dataScadenza ?? DateTime.UtcNow.AddDays(30)).Date, DateTimeKind.Utc),
                DataScadenzaAttesa = scadenzaAttesa,
                Importo = f.Totale,
                Metodo = metodo ?? MetodoPagamento.Bonifico,
                Iban = fornitore?.Iban,
                Stato = StatoScadenza.DaPagare
            });
        }

        await _mongo.Fatture.UpdateOneAsync(
            x => x.Id == id,
            Builders<FatturaFornitore>.Update
                .Set(x => x.Stato, StatoFattura.Approvata)
                .Set(x => x.ApprovataIl, DateTime.UtcNow)
                .Set(x => x.ApprovataDaUserId, User.FindFirst(ClaimTypes.NameIdentifier)?.Value)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        TempData["flash"] = "Fattura approvata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("fattura/{id}/rifiuta")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RifiutaFattura(string id, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["flash"] = "Motivo rifiuto obbligatorio.";
            return RedirectToAction(nameof(Index));
        }
        await _mongo.Fatture.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<FatturaFornitore>.Update
                .Set(x => x.Stato, StatoFattura.Rifiutata)
                .Set(x => x.MotivoRifiuto, motivo)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Fattura rifiutata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("fattura/{id}/scarica")]
    public async Task<IActionResult> ScaricaAllegatoFattura(string id)
    {
        var f = await _mongo.Fatture.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
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

    // ─────────────────────────────────────────────────────────────
    //  PAGAMENTI — programma / segna pagato
    // ─────────────────────────────────────────────────────────────
    [HttpPost("scadenza/{id}/programma")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProgrammaPagamento(string id, DateTime dataProgrammata, string? riferimento = null)
    {
        var update = Builders<ScadenzaPagamento>.Update
            .Set(s => s.Stato, StatoScadenza.Programmato)
            .Set(s => s.DataProgrammata, DateTime.SpecifyKind(dataProgrammata.Date, DateTimeKind.Utc))
            .Set(s => s.RiferimentoPagamento, riferimento)
            .Set(s => s.UpdatedAt, DateTime.UtcNow);
        await _mongo.ScadenzePagamento.UpdateOneAsync(s => s.Id == id && s.TenantId == _tenant.TenantId, update);
        TempData["flash"] = "Pagamento programmato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("scadenza/{id}/paga")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SegnaPagato(string id, DateTime? dataPagamento = null, string? riferimento = null)
    {
        var data = dataPagamento ?? DateTime.UtcNow;
        var update = Builders<ScadenzaPagamento>.Update
            .Set(s => s.Stato, StatoScadenza.Pagato)
            .Set(s => s.DataPagamento, DateTime.SpecifyKind(data.Date, DateTimeKind.Utc))
            .Set(s => s.RiferimentoPagamento, riferimento)
            .Set(s => s.UpdatedAt, DateTime.UtcNow);
        await _mongo.ScadenzePagamento.UpdateOneAsync(s => s.Id == id && s.TenantId == _tenant.TenantId, update);
        TempData["flash"] = "Scadenza marcata come pagata.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("scadenza/{id}/annulla")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnnullaScadenza(string id)
    {
        await _mongo.ScadenzePagamento.UpdateOneAsync(
            s => s.Id == id && s.TenantId == _tenant.TenantId,
            Builders<ScadenzaPagamento>.Update
                .Set(s => s.Stato, StatoScadenza.Annullato)
                .Set(s => s.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Scadenza annullata.";
        return RedirectToAction(nameof(Index));
    }

    // ─────────────────────────────────────────────────────────────
    //  FORNITORI — anagrafica
    // ─────────────────────────────────────────────────────────────
    [HttpGet("fornitori")]
    public async Task<IActionResult> Fornitori()
    {
        var tid = _tenant.TenantId!;
        var fornitori = await _mongo.Fornitori.Find(f => f.TenantId == tid).SortBy(f => f.RagioneSociale).ToListAsync();
        var users = await _mongo.Users.Find(u => u.TenantId == tid && u.Role == UserRole.Fornitore).ToListAsync();
        var userByForn = users
            .Where(u => u.LinkedPersonType == LinkedPersonType.Fornitore && !string.IsNullOrEmpty(u.LinkedPersonId))
            .ToDictionary(u => u.LinkedPersonId!, u => u);

        var aperte = await _mongo.ScadenzePagamento.Find(s => s.TenantId == tid &&
            (s.Stato == StatoScadenza.DaPagare || s.Stato == StatoScadenza.Programmato)).ToListAsync();
        var oggi = DateTime.UtcNow.Date;
        var anno = oggi.Year;
        var fattureAnno = await _mongo.Fatture.Find(f => f.TenantId == tid && f.DataEmissione.Year == anno).ToListAsync();

        var rows = fornitori.Select(f => new FornitoreRow
        {
            Fornitore = f,
            HaUtentePortale = userByForn.ContainsKey(f.Id),
            EspostoCorrente = aperte.Where(s => s.FornitoreId == f.Id).Sum(s => s.Importo),
            FatturePeriodoCorrente = fattureAnno.Count(x => x.FornitoreId == f.Id)
        }).ToList();

        ViewData["Section"] = "fornitori";
        return View(new FornitoriIndexViewModel { Fornitori = rows });
    }

    [HttpGet("fornitori/nuovo")]
    public IActionResult NuovoFornitore()
    {
        ViewData["Section"] = "fornitori";
        return View("FornitoreForm", new FornitoreFormViewModel());
    }

    [HttpPost("fornitori/nuovo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovoFornitore(FornitoreFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "fornitori";
            return View("FornitoreForm", vm);
        }

        var f = new Fornitore
        {
            TenantId = _tenant.TenantId!,
            RagioneSociale = vm.RagioneSociale.Trim(),
            PartitaIva = vm.PartitaIva,
            CodiceFiscale = vm.CodiceFiscale,
            CodiceSdi = vm.CodiceSdi,
            Pec = vm.Pec,
            EmailContatto = vm.EmailContatto,
            Telefono = vm.Telefono,
            Indirizzo = vm.Indirizzo,
            Iban = vm.Iban,
            CategoriaDefault = vm.CategoriaDefault,
            Stato = vm.Stato,
            Note = vm.Note,
            TerminiPagamentoGiorni = vm.TerminiPagamentoGiorni,
            BasePagamento = vm.BasePagamento
        };
        await _mongo.Fornitori.InsertOneAsync(f);

        if (vm.AbilitaPortale && !string.IsNullOrWhiteSpace(vm.EmailContatto) && !string.IsNullOrWhiteSpace(vm.PortalePassword))
        {
            await CreaUtentePortaleAsync(f, vm.EmailContatto!, vm.PortalePassword!);
        }

        TempData["flash"] = "Fornitore registrato.";
        return RedirectToAction(nameof(Fornitori));
    }

    [HttpGet("fornitori/{id}")]
    public async Task<IActionResult> EditFornitore(string id)
    {
        var f = await _mongo.Fornitori.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (f is null) return NotFound();
        var hasUser = await _mongo.Users.Find(u => u.TenantId == _tenant.TenantId
            && u.Role == UserRole.Fornitore && u.LinkedPersonId == f.Id).AnyAsync();
        ViewData["Section"] = "fornitori";
        return View("FornitoreForm", new FornitoreFormViewModel
        {
            Id = f.Id,
            RagioneSociale = f.RagioneSociale,
            PartitaIva = f.PartitaIva,
            CodiceFiscale = f.CodiceFiscale,
            CodiceSdi = f.CodiceSdi,
            Pec = f.Pec,
            EmailContatto = f.EmailContatto,
            Telefono = f.Telefono,
            Indirizzo = f.Indirizzo,
            Iban = f.Iban,
            CategoriaDefault = f.CategoriaDefault,
            Stato = f.Stato,
            Note = f.Note,
            TerminiPagamentoGiorni = f.TerminiPagamentoGiorni,
            BasePagamento = f.BasePagamento,
            HaUtentePortale = hasUser,
            IsDottoreOmbra = !string.IsNullOrEmpty(f.DottoreId)
        });
    }

    [HttpPost("fornitori/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditFornitore(string id, FornitoreFormViewModel vm)
    {
        var f = await _mongo.Fornitori.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (f is null) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "fornitori";
            return View("FornitoreForm", vm);
        }

        await _mongo.Fornitori.UpdateOneAsync(x => x.Id == id,
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
                .Set(x => x.CategoriaDefault, vm.CategoriaDefault)
                .Set(x => x.Stato, vm.Stato)
                .Set(x => x.Note, vm.Note)
                .Set(x => x.TerminiPagamentoGiorni, vm.TerminiPagamentoGiorni)
                .Set(x => x.BasePagamento, vm.BasePagamento)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        if (vm.AbilitaPortale && !string.IsNullOrWhiteSpace(vm.EmailContatto) && !string.IsNullOrWhiteSpace(vm.PortalePassword))
        {
            var existing = await _mongo.Users.Find(u => u.TenantId == _tenant.TenantId
                && u.Role == UserRole.Fornitore && u.LinkedPersonId == f.Id).FirstOrDefaultAsync();
            if (existing is null)
            {
                await CreaUtentePortaleAsync(f, vm.EmailContatto!, vm.PortalePassword!);
            }
            else
            {
                await _mongo.Users.UpdateOneAsync(u => u.Id == existing.Id,
                    Builders<User>.Update
                        .Set(u => u.PasswordHash, _hasher.Hash(vm.PortalePassword!))
                        .Set(u => u.UpdatedAt, DateTime.UtcNow));
            }
        }

        TempData["flash"] = "Fornitore aggiornato.";
        return RedirectToAction(nameof(Fornitori));
    }

    // ─────────────────────────────────────────────────────────────
    //  DATI BANCARI ORDINANTE (Tenant settings)
    // ─────────────────────────────────────────────────────────────
    [HttpGet("banca")]
    public async Task<IActionResult> DatiBancari()
    {
        var tenant = await _mongo.Tenants.Find(t => t.Id == _tenant.TenantId).FirstOrDefaultAsync();
        if (tenant is null) return NotFound();
        ViewData["Section"] = "tesoreria";
        return View(new DatiBancariFormViewModel
        {
            PagatoreIban = tenant.PagatoreIban ?? string.Empty,
            PagatoreBic = tenant.PagatoreBic,
            PagatoreRagioneSociale = tenant.PagatoreRagioneSociale ?? tenant.RagioneSociale ?? tenant.DisplayName,
            PagatoreCodiceFiscale = tenant.PagatoreCodiceFiscale ?? tenant.CodiceFiscale,
            IsConfigured = !string.IsNullOrEmpty(tenant.PagatoreIban)
        });
    }

    [HttpPost("banca")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DatiBancari(DatiBancariFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "tesoreria";
            return View(vm);
        }
        var iban = NormalizeIban(vm.PagatoreIban);
        if (!IsValidIban(iban))
        {
            ModelState.AddModelError(nameof(vm.PagatoreIban), "IBAN non valido (controlla formato e check digit).");
            ViewData["Section"] = "tesoreria";
            return View(vm);
        }
        await _mongo.Tenants.UpdateOneAsync(t => t.Id == _tenant.TenantId,
            Builders<Tenant>.Update
                .Set(t => t.PagatoreIban, iban)
                .Set(t => t.PagatoreBic, string.IsNullOrWhiteSpace(vm.PagatoreBic) ? null : vm.PagatoreBic.Trim().ToUpperInvariant())
                .Set(t => t.PagatoreRagioneSociale, vm.PagatoreRagioneSociale.Trim())
                .Set(t => t.PagatoreCodiceFiscale, string.IsNullOrWhiteSpace(vm.PagatoreCodiceFiscale) ? null : vm.PagatoreCodiceFiscale.Trim()));
        TempData["flash"] = "Dati bancari ordinante salvati. Ora puoi generare distinte SEPA.";
        return RedirectToAction(nameof(Index));
    }

    // ─────────────────────────────────────────────────────────────
    //  DISTINTE SEPA — generazione + storico
    // ─────────────────────────────────────────────────────────────
    [HttpGet("distinte")]
    public async Task<IActionResult> Distinte()
    {
        var distinte = await _mongo.DistinteSepa.Find(d => d.TenantId == _tenant.TenantId)
            .SortByDescending(d => d.CreatedAt).Limit(100).ToListAsync();
        ViewData["Section"] = "tesoreria-distinte";
        return View(new DistinteIndexViewModel { Distinte = distinte });
    }

    [HttpPost("distinte/genera")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneraSepaXml(string[] scadenzaIds, DateTime dataEsecuzione, string? etichetta = null)
    {
        if (scadenzaIds is null || scadenzaIds.Length == 0)
        {
            TempData["flash"] = "Seleziona almeno una scadenza.";
            return RedirectToAction(nameof(Index));
        }

        var tenant = await _mongo.Tenants.Find(t => t.Id == _tenant.TenantId).FirstOrDefaultAsync();
        if (tenant is null) return NotFound();

        var tid = _tenant.TenantId!;
        var ids = scadenzaIds.Distinct().ToArray();
        var scadenze = await _mongo.ScadenzePagamento
            .Find(s => s.TenantId == tid && ids.Contains(s.Id)).ToListAsync();

        // Filtri di sicurezza: solo DaPagare con metodo bonificabile.
        var ammesse = scadenze.Where(s =>
            s.Stato == StatoScadenza.DaPagare
            && (s.Metodo == MetodoPagamento.Bonifico || s.Metodo == MetodoPagamento.Altro)
        ).ToList();
        if (ammesse.Count == 0)
        {
            TempData["flash"] = "Nessuna delle scadenze selezionate è bonificabile (devono essere DaPagare con metodo Bonifico).";
            return RedirectToAction(nameof(Index));
        }

        // Lookup fornitori, fatture, cliniche
        var fornitoreIds = ammesse.Select(s => s.FornitoreId).Distinct().ToList();
        var fatturaIds = ammesse.Select(s => s.FatturaId).Distinct().ToList();
        var clinicaIds = ammesse.Select(s => s.ClinicaId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        var fornitori = (await _mongo.Fornitori.Find(f => f.TenantId == tid && fornitoreIds.Contains(f.Id)).ToListAsync())
            .ToDictionary(f => f.Id);
        var fatture = (await _mongo.Fatture.Find(f => f.TenantId == tid && fatturaIds.Contains(f.Id)).ToListAsync())
            .ToDictionary(f => f.Id);
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid && clinicaIds.Contains(c.Id)).ToListAsync())
            .ToDictionary(c => c.Id);

        // Validazione IBAN beneficiario
        var senzaIban = ammesse.Where(s =>
        {
            var iban = !string.IsNullOrWhiteSpace(s.Iban) ? s.Iban : fornitori.GetValueOrDefault(s.FornitoreId)?.Iban;
            return string.IsNullOrWhiteSpace(iban);
        }).ToList();
        if (senzaIban.Count > 0)
        {
            var nomi = string.Join(", ", senzaIban
                .Select(s => fornitori.GetValueOrDefault(s.FornitoreId)?.RagioneSociale ?? "—")
                .Distinct().Take(3));
            TempData["flash"] = $"Manca l'IBAN per {senzaIban.Count} scadenze ({nomi}…). Aggiornali sul fornitore prima di generare la distinta.";
            return RedirectToAction(nameof(Index));
        }

        // Raggruppamento per IBAN ordinante (clinica → fallback tenant).
        // Ogni gruppo distinto diventa un <PmtInf> nel pain.001 risultante.
        var ordinantiPerScadenza = ammesse
            .Select(s => new
            {
                Scadenza = s,
                Ordinante = PagamentiHelper.Risolvi(cliniche.GetValueOrDefault(s.ClinicaId), tenant)
            })
            .ToList();

        var senzaOrdinante = ordinantiPerScadenza.Where(x => string.IsNullOrEmpty(x.Ordinante.Iban)).ToList();
        if (senzaOrdinante.Count > 0)
        {
            TempData["flash"] = "Configura l'IBAN ordinante (sul tenant o sulla clinica destinataria delle fatture) prima di generare la distinta.";
            return RedirectToAction(nameof(DatiBancari));
        }

        var distintaCount = await _mongo.DistinteSepa.CountDocumentsAsync(d => d.TenantId == tid);
        var messageId = $"DIS-{DateTime.UtcNow:yyyyMMddHHmmss}-{distintaCount + 1:0000}";

        var gruppi = ordinantiPerScadenza
            .GroupBy(x => x.Ordinante.Iban)
            .Select((g, gi) =>
            {
                var ordinante = g.First().Ordinante;
                var transazioni = g.Select((x, idx) =>
                {
                    var s = x.Scadenza;
                    var f = fornitori.GetValueOrDefault(s.FornitoreId);
                    var fa = fatture.GetValueOrDefault(s.FatturaId);
                    var ibanBenef = !string.IsNullOrWhiteSpace(s.Iban) ? s.Iban! : f!.Iban!;
                    var causale = fa is null
                        ? $"Pagamento scadenza {s.Id}"
                        : $"Fattura {fa.Numero} {f?.RagioneSociale}";
                    return new SepaXmlBuilder.SepaTransazione(
                        EndToEndId: $"{messageId}-{gi + 1:00}-{idx + 1:0000}",
                        Importo: Math.Round(s.Importo, 2),
                        BeneficiarioNome: f?.RagioneSociale ?? "Fornitore",
                        BeneficiarioIban: ibanBenef,
                        BeneficiarioBic: null,
                        Causale: causale);
                }).ToList();

                return new SepaXmlBuilder.SepaGruppoOrdinante(
                    PaymentInfoId: $"PMT-{messageId}-{gi + 1:00}",
                    OrdinanteNome: ordinante.RagioneSociale,
                    OrdinanteIban: ordinante.Iban,
                    OrdinanteBic: ordinante.Bic,
                    OrdinanteCodiceFiscale: ordinante.CodiceFiscale,
                    Transazioni: transazioni);
            })
            .ToList();

        // L'InitiatingParty del messaggio è sempre il tenant (chi "firma" il pacchetto).
        var initiatingNome = tenant.PagatoreRagioneSociale ?? tenant.RagioneSociale ?? tenant.DisplayName;
        var (xml, totale, count) = SepaXmlBuilder.Build(new SepaXmlBuilder.SepaInput(
            MessageId: messageId,
            DataCreazione: DateTime.UtcNow,
            DataEsecuzione: DateTime.SpecifyKind(dataEsecuzione.Date, DateTimeKind.Utc),
            InitiatingPartyNome: initiatingNome,
            InitiatingPartyCodiceFiscale: tenant.PagatoreCodiceFiscale,
            Gruppi: gruppi));

        // Persisti distinta. Conserviamo l'IBAN del primo gruppo + l'elenco scadenze;
        // l'XML completo (con tutti i PmtInf) è nello stesso documento.
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var ordinantePrincipale = gruppi.First();
        var distinta = new DistintaPagamento
        {
            TenantId = tid,
            MessageId = messageId,
            Etichetta = string.IsNullOrWhiteSpace(etichetta) ? messageId : etichetta!.Trim(),
            DataEsecuzione = DateTime.SpecifyKind(dataEsecuzione.Date, DateTimeKind.Utc),
            PagatoreIban = ordinantePrincipale.OrdinanteIban,
            PagatoreBic = ordinantePrincipale.OrdinanteBic,
            PagatoreRagioneSociale = ordinantePrincipale.OrdinanteNome,
            NumeroTransazioni = count,
            Totale = totale,
            ScadenzaIds = ammesse.Select(s => s.Id).ToList(),
            Xml = xml,
            CreatoDaUserId = userId
        };
        await _mongo.DistinteSepa.InsertOneAsync(distinta);

        // Marca le scadenze come "Programmato", snapshot dell'IBAN ordinante usato.
        var ibanPerScadenza = ordinantiPerScadenza.ToDictionary(x => x.Scadenza.Id, x => x.Ordinante.Iban);
        var bulkOps = ammesse.Select(s =>
            new UpdateOneModel<ScadenzaPagamento>(
                Builders<ScadenzaPagamento>.Filter.Eq(x => x.Id, s.Id),
                Builders<ScadenzaPagamento>.Update
                    .Set(x => x.Stato, StatoScadenza.Programmato)
                    .Set(x => x.DataProgrammata, DateTime.SpecifyKind(dataEsecuzione.Date, DateTimeKind.Utc))
                    .Set(x => x.RiferimentoPagamento, distinta.Etichetta)
                    .Set(x => x.IbanOrdinanteUsato, ibanPerScadenza[s.Id])
                    .Set(x => x.UpdatedAt, DateTime.UtcNow))
        ).ToList();
        await _mongo.ScadenzePagamento.BulkWriteAsync(bulkOps);

        await _mongo.Audit.InsertOneAsync(new AuditEntry
        {
            TenantId = tid,
            UserId = userId ?? "",
            UserName = User.Identity?.Name ?? "",
            Action = AuditAction.Created,
            EntityType = nameof(DistintaPagamento),
            EntityId = distinta.Id,
            EntityLabel = $"Distinta SEPA {distinta.Etichetta} · {count} bonifici · {totale:N2} €",
            Note = gruppi.Count == 1
                ? $"Esecuzione: {dataEsecuzione:dd/MM/yyyy}. Ordinante: {distinta.PagatoreIban}."
                : $"Esecuzione: {dataEsecuzione:dd/MM/yyyy}. {gruppi.Count} ordinanti distinti (multi-banca)."
        });

        var bytes = Encoding.UTF8.GetBytes(xml);
        var fileName = $"{distinta.Etichetta}.xml".Replace(' ', '_');
        return File(bytes, "application/xml", fileName);
    }

    [HttpGet("distinte/{id}/scarica")]
    public async Task<IActionResult> ScaricaDistinta(string id)
    {
        var d = await _mongo.DistinteSepa.Find(x => x.Id == id && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
        if (d is null) return NotFound();
        var bytes = Encoding.UTF8.GetBytes(d.Xml);
        var fileName = $"{d.Etichetta ?? d.MessageId}.xml".Replace(' ', '_');
        return File(bytes, "application/xml", fileName);
    }

    // ─────────────────────────────────────────────────────────────
    //  EXPORT CSV
    // ─────────────────────────────────────────────────────────────
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var tid = _tenant.TenantId!;
        var scadenze = await _mongo.ScadenzePagamento.Find(s => s.TenantId == tid).ToListAsync();
        var fatture = (await _mongo.Fatture.Find(f => f.TenantId == tid).ToListAsync()).ToDictionary(f => f.Id);
        var fornitori = (await _mongo.Fornitori.Find(f => f.TenantId == tid).ToListAsync()).ToDictionary(f => f.Id);
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync()).ToDictionary(c => c.Id);

        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM UTF-8 (Excel)
        sb.AppendLine("Data;Competenza;LOC;EM;BM;NumeroDoc;Fornitore;Imponibile;IVA;Totale;Metodo;Stato;Categoria;Note;IBAN");
        var oggi = DateTime.UtcNow.Date;
        var inv = CultureInfo.InvariantCulture;
        foreach (var s in scadenze.OrderBy(x => x.DataScadenza))
        {
            var fa = fatture.GetValueOrDefault(s.FatturaId);
            var fr = fornitori.GetValueOrDefault(s.FornitoreId);
            var c = cliniche.GetValueOrDefault(s.ClinicaId);
            var stato = DerivedStato(s, oggi);
            sb.Append(s.DataScadenza.ToString("dd/MM/yyyy")).Append(';')
              .Append(Csv(fa?.MeseCompetenza.ToString("MMM yy", new CultureInfo("it-IT")))).Append(';')
              .Append(Csv(SiglaSede(c))).Append(';')
              .Append(Csv(fa?.FlagEM)).Append(';')
              .Append(fa?.FlagBM == true ? "BM" : "").Append(';')
              .Append(Csv(fa?.Numero)).Append(';')
              .Append(Csv(fr?.RagioneSociale)).Append(';')
              .Append(Csv(fa?.Imponibile.ToString("0.00", inv))).Append(';')
              .Append(Csv(fa?.Iva.ToString("0.00", inv))).Append(';')
              .Append(s.Importo.ToString("0.00", inv)).Append(';')
              .Append(Csv(s.Metodo.ToString())).Append(';')
              .Append(Csv(stato.ToString())).Append(';')
              .Append(Csv(s.Categoria.ToString())).Append(';')
              .Append(Csv(s.Note ?? fa?.Note)).Append(';')
              .Append(Csv(s.Iban))
              .AppendLine();
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"tesoreria-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────
    private async Task PopulateFatturaLookups(FatturaFormViewModel vm)
    {
        vm.Fornitori = await _mongo.Fornitori.Find(f => f.TenantId == _tenant.TenantId && f.Stato == StatoFornitore.Attivo)
            .SortBy(f => f.RagioneSociale).ToListAsync();
        vm.Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId)
            .SortBy(c => c.Nome).ToListAsync();
    }

    private async Task<string?> TryAttachAsync(FatturaFornitore target, Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (file.Length > MaxUploadBytes) return $"File troppo grande (max {MaxUploadBytes / (1024 * 1024)}MB).";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return $"Estensione non consentita: {ext}";

        await using var stream = file.OpenReadStream();
        var stored = await _storage.SaveAsync(_tenant.TenantId!, "fatture", file.FileName, stream, file.ContentType);
        target.AllegatoNome = file.FileName;
        target.AllegatoPath = stored.RelativePath;
        target.AllegatoSize = stored.SizeBytes;
        return null;
    }

    private async Task CreaUtentePortaleAsync(Fornitore f, string email, string password)
    {
        var existing = await _mongo.Users.Find(u => u.Email == email).FirstOrDefaultAsync();
        if (existing is not null) return;
        await _mongo.Users.InsertOneAsync(new User
        {
            TenantId = _tenant.TenantId!,
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = _hasher.Hash(password),
            FullName = f.RagioneSociale,
            Role = UserRole.Fornitore,
            LinkedPersonType = LinkedPersonType.Fornitore,
            LinkedPersonId = f.Id,
            IsActive = true
        });
    }

    private static string NormalizeIban(string iban) =>
        new string((iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Validazione IBAN: lunghezza per paese + check digit MOD 97 (ISO 13616).</summary>
    private static bool IsValidIban(string iban)
    {
        if (string.IsNullOrEmpty(iban) || iban.Length < 15 || iban.Length > 34) return false;
        if (!iban.All(char.IsLetterOrDigit)) return false;

        // IT richiede 27 caratteri totali (convenzione italiana standard)
        if (iban.StartsWith("IT") && iban.Length != 27) return false;

        // Sposta i primi 4 caratteri in fondo, sostituisci lettere con numeri (A=10, B=11, ...)
        var rearranged = iban[4..] + iban[..4];
        var numeric = new StringBuilder();
        foreach (var ch in rearranged)
        {
            numeric.Append(char.IsDigit(ch) ? ch.ToString() : (ch - 'A' + 10).ToString());
        }
        // Mod 97 a chunk per evitare overflow
        var s = numeric.ToString();
        var remainder = 0;
        foreach (var ch in s)
        {
            remainder = (remainder * 10 + (ch - '0')) % 97;
        }
        return remainder == 1;
    }

    private static StatoScadenza DerivedStato(ScadenzaPagamento s, DateTime oggi)
    {
        if (s.Stato == StatoScadenza.DaPagare && s.DataScadenza < oggi) return StatoScadenza.Insoluto;
        return s.Stato;
    }

    /// <summary>
    /// Sigla 3 caratteri usata da Confident nel file scadenziario (BOL, BUS, CCH, COM, COR,
    /// DES, GIU, MI3, MI6, MI7, MI9, SGM, VAR, BRU, CMS).
    /// </summary>
    private static string SiglaSede(Clinica? c)
    {
        if (c is null) return "—";
        var nome = (c.Nome ?? "").Trim().ToUpperInvariant().Replace(".", "").Replace(" ", "");
        return nome switch
        {
            "DESIO"     => "DES",
            "VARESE"    => "VAR",
            "GIUSSANO"  => "GIU",
            "CORMANO"   => "COR",
            "COMO"      => "COM",
            "MILANO7"   => "MI7",
            "MILANO9"   => "MI9",
            "SGM"       => "SGM",
            "BUSTOA"    => "BUS",
            "BOLLATE"   => "BOL",
            "MILANO6"   => "MI6",
            "MILANO3"   => "MI3",
            "BRUGHERIO" => "BRU",
            "COMASINA"  => "CMS",
            "CCH"       => "CCH",
            _ => string.IsNullOrEmpty(nome) ? "—" : new string(nome.Take(3).ToArray())
        };
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
