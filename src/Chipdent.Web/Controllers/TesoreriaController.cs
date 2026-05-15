using System.Globalization;
using System.Security.Claims;
using System.Text;
using Chipdent.Web.Domain.Common;
using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Audit;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Sepa;
using Chipdent.Web.Infrastructure.Tesoreria;
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
    private readonly IAuditService _audit;
    private readonly ILogger<TesoreriaController> _logger;

    public TesoreriaController(MongoContext mongo, ITenantContext tenant, IFileStorage storage, IPasswordHasher hasher, IAuditService audit, ILogger<TesoreriaController> logger)
    {
        _mongo = mongo;
        _tenant = tenant;
        _storage = storage;
        _hasher = hasher;
        _audit = audit;
        _logger = logger;
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

        // Mappa userId -> FullName per risolvere "Caricata da" sulle fatture create dal back-office.
        var users = await _mongo.Users.Find(u => u.TenantId == tid).ToListAsync();
        var usersById = users.ToDictionary(u => u.Id, u => u.FullName);

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

                // IBAN da usare per la distinta: prima la snapshot sulla scadenza, poi l'anagrafica.
                var ibanEffettivo = !string.IsNullOrWhiteSpace(s.Iban) ? s.Iban : f?.Iban;
                var richiedeBonifico = s.Metodo == MetodoPagamento.Bonifico || s.Metodo == MetodoPagamento.Altro;
                var ibanMancante = richiedeBonifico
                    && (string.IsNullOrWhiteSpace(ibanEffettivo)
                        || !FornitoreCompletezza.IsValidIban(FornitoreCompletezza.NormalizeIban(ibanEffettivo)));

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
                    FornitoreNome = f?.NomePerPagamento ?? "— fornitore rimosso —",
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
                    Origine = fa?.Origine ?? OrigineFattura.Backoffice,
                    CaricataIl = fa?.CreatedAt ?? s.CreatedAt,
                    CaricataDaNome = (fa?.Origine == OrigineFattura.PortaleFornitore)
                        ? (f?.RagioneSociale ?? "Portale fornitore")
                        : (string.IsNullOrEmpty(fa?.CaricataDaUserId) ? "—" : usersById.GetValueOrDefault(fa.CaricataDaUserId, "—")),
                    DataProgrammata = s.DataProgrammata,
                    DataPagamento = s.DataPagamento,
                    DistintaSepaId = s.DistintaSepaId,
                    HasAllegato = !string.IsNullOrEmpty(fa?.AllegatoPath),
                    FornitoreSconosciuto = f is null,
                    MancaIbanFornitore = ibanMancante,
                    MancaIdFiscale = f is not null
                        && string.IsNullOrWhiteSpace(f.PartitaIva)
                        && string.IsNullOrWhiteSpace(f.CodiceFiscale)
                };
            }).ToList();

        // Conta i mismatch sull'intero set filtrato (prima del filtro "solo fuori termini")
        // per avere un KPI stabile.
        var fuoriTerminiCount = righe.Count(r => r.ScadenzaFuoriTermini);
        if (filtro.SoloFuoriTermini)
        {
            righe = righe.Where(r => r.ScadenzaFuoriTermini).ToList();
        }

        // Sort utente (?sort=col&dir=asc|desc). Default: data crescente, pagate in fondo (già fatto).
        if (!string.IsNullOrEmpty(filtro.Sort))
        {
            var asc = !string.Equals(filtro.Dir, "desc", StringComparison.OrdinalIgnoreCase);
            IOrderedEnumerable<RigaTesoreria>? ord = filtro.Sort.ToLowerInvariant() switch
            {
                "data"      => asc ? righe.OrderBy(r => r.DataScadenza)        : righe.OrderByDescending(r => r.DataScadenza),
                "loc"       => asc ? righe.OrderBy(r => r.Loc)                 : righe.OrderByDescending(r => r.Loc),
                "em"        => asc ? righe.OrderBy(r => r.TipoEmissione)       : righe.OrderByDescending(r => r.TipoEmissione),
                "doc"       => asc ? righe.OrderBy(r => r.NumeroDoc)           : righe.OrderByDescending(r => r.NumeroDoc),
                "fornitore" => asc ? righe.OrderBy(r => r.FornitoreNome)       : righe.OrderByDescending(r => r.FornitoreNome),
                "totale"    => asc ? righe.OrderBy(r => r.Totale)              : righe.OrderByDescending(r => r.Totale),
                "metodo"    => asc ? righe.OrderBy(r => r.Metodo)              : righe.OrderByDescending(r => r.Metodo),
                "stato"     => asc ? righe.OrderBy(r => r.Stato)               : righe.OrderByDescending(r => r.Stato),
                "inserita"  => asc ? righe.OrderBy(r => r.CaricataIl)          : righe.OrderByDescending(r => r.CaricataIl),
                "chi"       => asc ? righe.OrderBy(r => r.CaricataDaNome)      : righe.OrderByDescending(r => r.CaricataDaNome),
                _           => null
            };
            if (ord is not null) righe = ord.ToList();
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

    /// <summary>Versione partial della form "Nuova fattura" usata dalla modale callout su /tesoreria.</summary>
    [HttpGet("fattura/nuova/modale")]
    public async Task<IActionResult> NuovaFatturaModale()
    {
        var vm = new FatturaFormViewModel
        {
            Fornitori = await _mongo.Fornitori.Find(f => f.TenantId == _tenant.TenantId && f.Stato == StatoFornitore.Attivo)
                .SortBy(f => f.RagioneSociale).ToListAsync(),
            Cliniche = await _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId)
                .SortBy(c => c.Nome).ToListAsync()
        };
        return PartialView("_NuovaFatturaModale", vm);
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

    /// <summary>
    /// Restituisce la modale di dettaglio/modifica per una singola scadenza.
    /// Mostra TUTTI i campi del file Excel (DATA, COMPETENZA, LOC, E/M, BM, n° doc,
    /// FORNITORE, IMPORTO, IVA, IMPORTO TOTALE, MET PAG, STATUS, TIPO, NOTE, IBAN)
    /// e ne consente l'edit (eccetto i metadati di audit, in sola lettura).
    /// </summary>
    [HttpGet("scadenza/{id}/dettaglio")]
    public async Task<IActionResult> DettaglioScadenza(string id)
    {
        try
        {
            var tid = _tenant.TenantId!;
            var s = await _mongo.ScadenzePagamento.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
            if (s is null) return NotFound();

            var fa = string.IsNullOrEmpty(s.FatturaId)
                ? null
                : await _mongo.Fatture.Find(x => x.Id == s.FatturaId && x.TenantId == tid).FirstOrDefaultAsync();
            var f = string.IsNullOrEmpty(s.FornitoreId)
                ? null
                : await _mongo.Fornitori.Find(x => x.Id == s.FornitoreId && x.TenantId == tid).FirstOrDefaultAsync();
            var c = string.IsNullOrEmpty(s.ClinicaId)
                ? null
                : await _mongo.Cliniche.Find(x => x.Id == s.ClinicaId && x.TenantId == tid).FirstOrDefaultAsync();
            var allClinics = await _mongo.Cliniche.Find(x => x.TenantId == tid).SortBy(x => x.Nome).ToListAsync();

            ViewData["Fattura"]  = fa;
            ViewData["Fornitore"] = f;
            ViewData["Clinica"]  = c;
            ViewData["Cliniche"] = allClinics;
            return PartialView("_DettaglioScadenza", s);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore caricamento dettaglio scadenza {ScadenzaId} (tenant {TenantId})", id, _tenant.TenantId);
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Content($"<div style=\"padding:24px; color:#b8442e;\"><strong>Errore caricamento dettaglio</strong><br/><small>{System.Net.WebUtility.HtmlEncode(ex.Message)}</small></div>", "text/html");
        }
    }

    /// <summary>
    /// Update bulk dei campi di una singola scadenza + della fattura associata.
    /// Tutti i campi del file Excel sono editabili.
    /// </summary>
    /// <remarks>
    /// I campi monetari arrivano come stringa e vengono parsati esplicitamente con
    /// CultureInfo.InvariantCulture per evitare il bug del binder MVC: con cultura
    /// it-IT, "1797.26" (inviato dall'input HTML5 type=number) veniva interpretato
    /// come 179726 perché il "." è il separatore migliaia in italiano.
    /// </remarks>
    [HttpPost("scadenza/{id}/modifica")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModificaScadenza(string id,
        // Scadenza
        DateTime dataScadenza, string totale, MetodoPagamento metodo, StatoScadenza stato,
        string? iban, string? note, string? clinicaId,
        DateTime? dataProgrammata, DateTime? dataPagamento, string? riferimentoPagamento,
        // Fattura
        string? fatturaId, string? numero, DateTime? meseCompetenza,
        string? imponibile, string? iva, CategoriaSpesa? categoria,
        TipoEmissioneFattura? tipoEmissione, bool bonificoMultiploCbi, string? noteFattura)
    {
        var tid = _tenant.TenantId!;

        // Lock contabile: una scadenza già Pagata è uno storico immutabile.
        // Chi vuole correggerla deve emettere nota di credito o creare una nuova
        // scadenza compensativa, non sovrascrivere il dato.
        var corrente = await _mongo.ScadenzePagamento.Find(s => s.Id == id && s.TenantId == tid).FirstOrDefaultAsync();
        if (corrente is null) return NotFound();
        if (corrente.Stato == StatoScadenza.Pagato)
        {
            TempData["flash"] = "Scadenza già pagata: lo storico contabile non è modificabile.";
            return RedirectToAction(nameof(Index));
        }

        // Parser robusto: accetta sia "1797,26" (IT) sia "1797.26" (EN/HTML5).
        // Rimuove spazi e separatori delle migliaia (come da fix bug binder culture).
        static decimal ParseDec(string? s, decimal fallback = 0m)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim();
            // Se ci sono sia ',' sia '.', l'ultimo è il decimal separator: l'altro è migliaia.
            int idxComma = s.LastIndexOf(',');
            int idxDot   = s.LastIndexOf('.');
            if (idxComma >= 0 && idxDot >= 0)
            {
                if (idxComma > idxDot) s = s.Replace(".", "").Replace(',', '.');
                else                   s = s.Replace(",", "");
            }
            else if (idxComma >= 0)
            {
                s = s.Replace(',', '.');
            }
            // Tolgo eventuali separatori di migliaia residui (spazi).
            s = s.Replace(" ", "");
            return decimal.TryParse(s, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
        decimal? ParseDecOpt(string? s) => string.IsNullOrWhiteSpace(s) ? null : ParseDec(s);

        var totaleDec     = ParseDec(totale);
        var imponibileDec = ParseDecOpt(imponibile);
        var ivaDec        = ParseDecOpt(iva);

        // ── Update scadenza ─────────────────────────────────────────
        var update = Builders<ScadenzaPagamento>.Update
            .Set(s => s.DataScadenza, DateTime.SpecifyKind(dataScadenza.Date, DateTimeKind.Utc))
            .Set(s => s.Importo, totaleDec)
            .Set(s => s.Metodo, metodo)
            .Set(s => s.Stato, stato)
            .Set(s => s.Iban, string.IsNullOrWhiteSpace(iban) ? null : iban.Trim())
            .Set(s => s.Note, string.IsNullOrWhiteSpace(note) ? null : note)
            .Set(s => s.DataProgrammata, dataProgrammata.HasValue ? DateTime.SpecifyKind(dataProgrammata.Value.Date, DateTimeKind.Utc) : (DateTime?)null)
            .Set(s => s.DataPagamento,   dataPagamento.HasValue   ? DateTime.SpecifyKind(dataPagamento.Value.Date,   DateTimeKind.Utc) : (DateTime?)null)
            .Set(s => s.RiferimentoPagamento, string.IsNullOrWhiteSpace(riferimentoPagamento) ? null : riferimentoPagamento.Trim())
            .Set(s => s.UpdatedAt, DateTime.UtcNow);
        if (!string.IsNullOrEmpty(clinicaId)) update = update.Set(s => s.ClinicaId, clinicaId);
        if (categoria.HasValue)               update = update.Set(s => s.Categoria, categoria.Value);

        await _mongo.ScadenzePagamento.UpdateOneAsync(s => s.Id == id && s.TenantId == tid, update);

        // ── Update fattura associata (se presente) ──────────────────
        if (!string.IsNullOrEmpty(fatturaId))
        {
            var fatturaUpdate = Builders<FatturaFornitore>.Update
                .Set(f => f.BonificoMultiploCbi, bonificoMultiploCbi)
                .Set(f => f.Note, string.IsNullOrWhiteSpace(noteFattura) ? null : noteFattura)
                .Set(f => f.Totale, totaleDec)
                .Set(f => f.UpdatedAt, DateTime.UtcNow);
            if (!string.IsNullOrWhiteSpace(numero))
                fatturaUpdate = fatturaUpdate.Set(f => f.Numero, numero.Trim());
            if (meseCompetenza.HasValue)
                fatturaUpdate = fatturaUpdate.Set(f => f.MeseCompetenza,
                    DateTime.SpecifyKind(new DateTime(meseCompetenza.Value.Year, meseCompetenza.Value.Month, 1), DateTimeKind.Utc));
            if (imponibileDec.HasValue) fatturaUpdate = fatturaUpdate.Set(f => f.Imponibile, imponibileDec.Value);
            if (ivaDec.HasValue)        fatturaUpdate = fatturaUpdate.Set(f => f.Iva, ivaDec.Value);
            if (categoria.HasValue)     fatturaUpdate = fatturaUpdate.Set(f => f.Categoria, categoria.Value);
            if (tipoEmissione.HasValue)
            {
                fatturaUpdate = fatturaUpdate.Set(f => f.TipoEmissione, tipoEmissione.Value);
                fatturaUpdate = fatturaUpdate.Set(f => f.FlagEM, tipoEmissione.Value switch {
                    TipoEmissioneFattura.Elettronica => "E",
                    TipoEmissioneFattura.Manuale     => "M",
                    _ => null
                });
            }
            if (!string.IsNullOrEmpty(clinicaId))
                fatturaUpdate = fatturaUpdate.Set(f => f.ClinicaId, clinicaId);

            await _mongo.Fatture.UpdateOneAsync(f => f.Id == fatturaId && f.TenantId == tid, fatturaUpdate);
        }

        TempData["flash"] = "Scadenza aggiornata.";
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
        var fornitori = await _mongo.Fornitori.Find(f => f.TenantId == tid && !f.IsDeleted).SortBy(f => f.RagioneSociale).ToListAsync();
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
            FatturePeriodoCorrente = fattureAnno.Count(x => x.FornitoreId == f.Id),
            Completezza = FornitoreCompletezza.Valuta(f)
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

        var codice = string.IsNullOrWhiteSpace(vm.Codice)
            ? await GenerateCodiceFornitoreAsync()
            : vm.Codice!.Trim();

        var ragioneSociale = vm.RagioneSociale.Trim();
        var ragioneSocialePagamento = string.IsNullOrWhiteSpace(vm.RagioneSocialePagamento)
            ? ragioneSociale
            : vm.RagioneSocialePagamento!.Trim();

        var f = new Fornitore
        {
            TenantId = _tenant.TenantId!,
            Codice = codice,
            RagioneSociale = ragioneSociale,
            RagioneSocialePagamento = ragioneSocialePagamento,
            PartitaIva = vm.PartitaIva,
            CodiceFiscale = vm.CodiceFiscale,
            CodiceSdi = vm.CodiceSdi,
            Pec = vm.Pec,
            EmailContatto = vm.EmailContatto,
            Telefono = vm.Telefono,
            Indirizzo = vm.Indirizzo,
            Localita = vm.Localita,
            Provincia = NormalizeProvincia(vm.Provincia),
            CodicePostale = vm.CodicePostale?.Trim(),
            Iban = vm.Iban,
            CategoriaDefault = vm.CategoriaDefault,
            CategoriaSecondaria = vm.CategoriaSecondaria,
            Stato = vm.Stato,
            Note = vm.Note,
            TerminiPagamentoGiorni = vm.TerminiPagamentoGiorni,
            BasePagamento = vm.BasePagamento,
            EmissioneFattura = vm.EmissioneFattura
        };
        await _mongo.Fornitori.InsertOneAsync(f);
        await _audit.LogAsync("Fornitore", f.Id, f.RagioneSociale, AuditAction.Created, actor: User);

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
            Codice = f.Codice,
            RagioneSociale = f.RagioneSociale,
            RagioneSocialePagamento = string.IsNullOrWhiteSpace(f.RagioneSocialePagamento) ? f.RagioneSociale : f.RagioneSocialePagamento,
            PartitaIva = f.PartitaIva,
            CodiceFiscale = f.CodiceFiscale,
            CodiceSdi = f.CodiceSdi,
            Pec = f.Pec,
            EmailContatto = f.EmailContatto,
            Telefono = f.Telefono,
            Indirizzo = f.Indirizzo,
            Localita = f.Localita,
            Provincia = f.Provincia,
            CodicePostale = f.CodicePostale,
            Iban = f.Iban,
            CategoriaDefault = f.CategoriaDefault,
            CategoriaSecondaria = f.CategoriaSecondaria,
            Stato = f.Stato,
            Note = f.Note,
            TerminiPagamentoGiorni = f.TerminiPagamentoGiorni,
            BasePagamento = f.BasePagamento,
            EmissioneFattura = f.EmissioneFattura,
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

        var codiceAggiornato = string.IsNullOrWhiteSpace(vm.Codice) ? f.Codice : vm.Codice!.Trim();
        var ragioneSociale = vm.RagioneSociale.Trim();
        var ragioneSocialePagamento = string.IsNullOrWhiteSpace(vm.RagioneSocialePagamento)
            ? ragioneSociale
            : vm.RagioneSocialePagamento!.Trim();
        var snapshot = CloneForAudit(f);
        await _mongo.Fornitori.UpdateOneAsync(x => x.Id == id,
            Builders<Fornitore>.Update
                .Set(x => x.Codice, codiceAggiornato)
                .Set(x => x.RagioneSociale, ragioneSociale)
                .Set(x => x.RagioneSocialePagamento, ragioneSocialePagamento)
                .Set(x => x.PartitaIva, vm.PartitaIva)
                .Set(x => x.CodiceFiscale, vm.CodiceFiscale)
                .Set(x => x.CodiceSdi, vm.CodiceSdi)
                .Set(x => x.Pec, vm.Pec)
                .Set(x => x.EmailContatto, vm.EmailContatto)
                .Set(x => x.Telefono, vm.Telefono)
                .Set(x => x.Indirizzo, vm.Indirizzo)
                .Set(x => x.Localita, vm.Localita)
                .Set(x => x.Provincia, NormalizeProvincia(vm.Provincia))
                .Set(x => x.CodicePostale, vm.CodicePostale?.Trim())
                .Set(x => x.Iban, vm.Iban)
                .Set(x => x.CategoriaDefault, vm.CategoriaDefault)
                .Set(x => x.CategoriaSecondaria, vm.CategoriaSecondaria)
                .Set(x => x.Stato, vm.Stato)
                .Set(x => x.Note, vm.Note)
                .Set(x => x.TerminiPagamentoGiorni, vm.TerminiPagamentoGiorni)
                .Set(x => x.BasePagamento, vm.BasePagamento)
                .Set(x => x.EmissioneFattura, vm.EmissioneFattura)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        var updated = await _mongo.Fornitori.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (updated is not null)
            await _audit.LogDiffAsync(snapshot, updated, "Fornitore", updated.RagioneSociale, AuditAction.Updated, actor: User);

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
    //  FORNITORI — INLINE EDIT / SOFT DELETE (griglia)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Aggiorna un singolo campo del fornitore dalla griglia (AJAX). Restituisce
    /// JSON con il valore formattato per la cella e il log audit della modifica.</summary>
    [HttpPost("fornitori/{id}/inline")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateFornitoreInline(string id, [FromForm] string field, [FromForm] string? value)
    {
        var tid = _tenant.TenantId!;
        var f = await _mongo.Fornitori.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (f is null) return NotFound();

        var trimmed = value?.Trim();
        UpdateDefinition<Fornitore>? update = null;
        string oldValue = "";
        string newValueDisplay = "";
        string fieldLabel = field;

        switch (field)
        {
            case "RagioneSociale":
                if (string.IsNullOrWhiteSpace(trimmed))
                    return BadRequest(new { ok = false, error = "La ragione sociale è obbligatoria." });
                oldValue = f.RagioneSociale;
                newValueDisplay = trimmed;
                update = Builders<Fornitore>.Update.Set(x => x.RagioneSociale, trimmed);
                fieldLabel = "Ragione sociale";
                break;

            case "RagioneSocialePagamento":
                var rsp = string.IsNullOrWhiteSpace(trimmed) ? f.RagioneSociale : trimmed!;
                oldValue = f.RagioneSocialePagamento;
                newValueDisplay = rsp;
                update = Builders<Fornitore>.Update.Set(x => x.RagioneSocialePagamento, rsp);
                fieldLabel = "Ragione sociale pagamento";
                break;

            case "CategoriaDefault":
                if (!Enum.TryParse<CategoriaSpesa>(trimmed, out var catPrimaria))
                    return BadRequest(new { ok = false, error = "Categoria non valida." });
                oldValue = f.CategoriaDefault.ToString();
                newValueDisplay = catPrimaria.ToString();
                update = Builders<Fornitore>.Update.Set(x => x.CategoriaDefault, catPrimaria);
                fieldLabel = "Categoria primaria";
                break;

            case "CategoriaSecondaria":
                CategoriaSpesa? catSec = null;
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    if (!Enum.TryParse<CategoriaSpesa>(trimmed, out var parsedSec))
                        return BadRequest(new { ok = false, error = "Categoria non valida." });
                    catSec = parsedSec;
                }
                oldValue = f.CategoriaSecondaria?.ToString() ?? "—";
                newValueDisplay = catSec?.ToString() ?? "—";
                update = Builders<Fornitore>.Update.Set(x => x.CategoriaSecondaria, catSec);
                fieldLabel = "Categoria secondaria";
                break;

            case "Stato":
                if (!Enum.TryParse<StatoFornitore>(trimmed, out var stato))
                    return BadRequest(new { ok = false, error = "Stato non valido." });
                oldValue = f.Stato.ToString();
                newValueDisplay = stato.ToString();
                update = Builders<Fornitore>.Update.Set(x => x.Stato, stato);
                fieldLabel = "Stato";
                break;

            default:
                return BadRequest(new { ok = false, error = "Campo non modificabile inline." });
        }

        if (oldValue == newValueDisplay)
            return Json(new { ok = true, display = newValueDisplay, unchanged = true });

        await _mongo.Fornitori.UpdateOneAsync(x => x.Id == id,
            update!.Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _audit.LogAsync("Fornitore", id, f.RagioneSociale, AuditAction.Updated,
            changes: new[] { new FieldChange { Field = fieldLabel, OldValue = oldValue, NewValue = newValueDisplay } },
            actor: User);

        return Json(new { ok = true, display = newValueDisplay });
    }

    /// <summary>Soft-delete del fornitore dalla griglia (AJAX). Marca IsDeleted=true e
    /// registra l'azione nell'audit log. Risponde JSON per evitare il full refresh.</summary>
    [HttpPost("fornitori/{id}/elimina")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaFornitore(string id)
    {
        var tid = _tenant.TenantId!;
        var f = await _mongo.Fornitori.Find(x => x.Id == id && x.TenantId == tid).FirstOrDefaultAsync();
        if (f is null) return NotFound(new { ok = false, error = "Fornitore non trovato." });

        await _mongo.Fornitori.UpdateOneAsync(x => x.Id == id,
            Builders<Fornitore>.Update
                .Set(x => x.IsDeleted, true)
                .Set(x => x.Stato, StatoFornitore.Dismesso)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _audit.LogAsync("Fornitore", id, f.RagioneSociale, AuditAction.Deleted,
            note: $"Eliminato dalla griglia (codice {f.Codice ?? "—"}).", actor: User);

        return Json(new { ok = true });
    }

    private static Fornitore CloneForAudit(Fornitore src) => new()
    {
        Id = src.Id,
        TenantId = src.TenantId,
        CreatedAt = src.CreatedAt,
        UpdatedAt = src.UpdatedAt,
        Codice = src.Codice,
        RagioneSociale = src.RagioneSociale,
        RagioneSocialePagamento = src.RagioneSocialePagamento,
        PartitaIva = src.PartitaIva,
        CodiceFiscale = src.CodiceFiscale,
        CodiceSdi = src.CodiceSdi,
        Pec = src.Pec,
        EmailContatto = src.EmailContatto,
        Telefono = src.Telefono,
        Indirizzo = src.Indirizzo,
        Localita = src.Localita,
        Provincia = src.Provincia,
        CodicePostale = src.CodicePostale,
        Iban = src.Iban,
        CategoriaDefault = src.CategoriaDefault,
        CategoriaSecondaria = src.CategoriaSecondaria,
        Stato = src.Stato,
        Note = src.Note,
        TerminiPagamentoGiorni = src.TerminiPagamentoGiorni,
        BasePagamento = src.BasePagamento,
        EmissioneFattura = src.EmissioneFattura,
        DottoreId = src.DottoreId,
        IsDeleted = src.IsDeleted
    };

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
                        BeneficiarioNome: f?.NomePerPagamento ?? "Fornitore",
                        BeneficiarioIban: ibanBenef,
                        BeneficiarioBic: null,
                        Causale: causale,
                        BeneficiarioCodiceFiscale: f?.CodiceFiscale,
                        BeneficiarioIndirizzo: f?.Indirizzo,
                        BeneficiarioCodicePostale: f?.CodicePostale,
                        BeneficiarioLocalita: f?.Localita,
                        BeneficiarioProvincia: f?.Provincia);
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

        // Le scadenze incluse in distinta NON cambiano stato: restano "Da pagare"
        // finché la banca non esegue il bonifico e il backoffice non clicca "✓ Pagato".
        // Lo stato "Programmato" è riservato alla programmazione manuale (promemoria).
        // Salviamo solo il riferimento alla distinta + la data esecuzione + l'IBAN ordinante.
        var ibanPerScadenza = ordinantiPerScadenza.ToDictionary(x => x.Scadenza.Id, x => x.Ordinante.Iban);
        var bulkOps = ammesse.Select(s =>
            new UpdateOneModel<ScadenzaPagamento>(
                Builders<ScadenzaPagamento>.Filter.Eq(x => x.Id, s.Id),
                Builders<ScadenzaPagamento>.Update
                    .Set(x => x.DistintaSepaId, distinta.Id)
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

    /// <summary>Genera il prossimo codice fornitore disponibile nel formato F#### (4 cifre).
    /// Si basa sul massimo numerico esistente con prefisso "F" per il tenant corrente.</summary>
    private async Task<string> GenerateCodiceFornitoreAsync()
    {
        var tid = _tenant.TenantId!;
        var esistenti = await _mongo.Fornitori
            .Find(f => f.TenantId == tid && f.Codice != null && f.Codice != "")
            .Project(f => f.Codice)
            .ToListAsync();
        var maxNum = 0;
        foreach (var c in esistenti)
        {
            if (string.IsNullOrEmpty(c) || c[0] != 'F') continue;
            if (int.TryParse(c.AsSpan(1), out var n) && n > maxNum) maxNum = n;
        }
        return $"F{maxNum + 1:D4}";
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

    private static string NormalizeIban(string iban) => FornitoreCompletezza.NormalizeIban(iban);

    /// <summary>Normalizza la sigla provincia a 2 lettere maiuscole (es. "mi  " → "MI"). Vuoto → null.</summary>
    private static string? NormalizeProvincia(string? provincia)
    {
        if (string.IsNullOrWhiteSpace(provincia)) return null;
        var letters = new string(provincia.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return letters.Length == 0 ? null : letters[..Math.Min(2, letters.Length)];
    }

    private static bool IsValidIban(string iban) => FornitoreCompletezza.IsValidIban(iban);

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

    // ─────────────────────────────────────────────────────────────
    //  IMPORT FATTURE PASSIVE (CCH/Ident — CSV o XLSX)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Lista cronologica dei batch di importazione (append-only).</summary>
    [HttpGet("import-fatture")]
    public async Task<IActionResult> ImportFatture()
    {
        var tid = _tenant.TenantId!;
        var batches = await _mongo.ImportFattureBatches
            .Find(b => b.TenantId == tid)
            .SortByDescending(b => b.DataCaricamento)
            .ToListAsync();

        // Conteggio proposte anagrafica in attesa (badge sul pulsante "Proposte anagrafica")
        var proposteInAttesa = await _mongo.ProposteAnagraficaFornitori
            .CountDocumentsAsync(p => p.TenantId == tid && p.Stato == StatoPropostaAnagrafica.InAttesa);

        var vm = new ImportFattureIndexViewModel
        {
            Batches = batches.Select(b => new ImportFattureBatchRow
            {
                Id = b.Id,
                DataCaricamento = b.DataCaricamento,
                CaricatoDa = string.IsNullOrEmpty(b.CaricatoDaNome) ? "—" : b.CaricatoDaNome,
                Tipo = b.Tipo.ToString(),
                Files = b.Files,
                TotaleRighe = b.TotaleRighe,
                RigheValide = b.RigheValide,
                RigheConErrore = b.RigheConErrore,
                Note = b.Note
            }).ToList(),
            TotaleBatch = batches.Count,
            TotaleRighe = batches.Sum(b => b.TotaleRighe),
            TotaleRigheConErrore = batches.Sum(b => b.RigheConErrore),
            UltimoCaricamento = batches.Count > 0 ? batches[0].DataCaricamento : null,
            ProposteAnagraficaInAttesa = (int)proposteInAttesa
        };

        ViewBag.Section = "tesoreria-import-fatture";
        return View("ImportFatture", vm);
    }

    /// <summary>Dettaglio batch: KPI e griglia righe per ciascun file/sheet.</summary>
    [HttpGet("import-fatture/{id}")]
    public async Task<IActionResult> ImportFattureDettaglio(string id)
    {
        var tid = _tenant.TenantId!;
        var batch = await _mongo.ImportFattureBatches
            .Find(b => b.Id == id && b.TenantId == tid)
            .FirstOrDefaultAsync();
        if (batch == null) return NotFound();

        var righe = await _mongo.ImportFattureRighe
            .Find(r => r.BatchId == id && r.TenantId == tid)
            .ToListAsync();

        var groups = batch.Files
            .Select(f => new ImportFattureFileGroup
            {
                Header = f,
                Righe = righe
                    .Where(r => r.NomeFile == f.NomeFile)
                    .OrderBy(r => r.NumeroRiga)
                    .ToList()
            })
            .ToList();

        var caricatoDa = batch.CaricatoDaNome;
        if (string.IsNullOrEmpty(caricatoDa) && !string.IsNullOrEmpty(batch.CaricatoDaUserId))
        {
            var u = await _mongo.Users.Find(x => x.Id == batch.CaricatoDaUserId).FirstOrDefaultAsync();
            caricatoDa = u?.FullName ?? "—";
        }

        var vm = new ImportFattureDettaglioViewModel
        {
            Batch = batch,
            CaricatoDaNome = string.IsNullOrEmpty(caricatoDa) ? "—" : caricatoDa,
            Files = groups
        };

        ViewBag.Section = "tesoreria-import-fatture";
        return View("ImportFattureDettaglio", vm);
    }

    /// <summary>
    /// POST upload: accetta 1 .xlsx, oppure 1-2 .csv, oppure una coppia CSV+PDF
    /// (la fattura passiva renderizzata FatturaPA/AssoSoftware). Il PDF arricchisce
    /// le righe CSV con anagrafica fornitore (P.IVA, CF, IBAN, indirizzo, recapiti),
    /// data scadenza, modalità di pagamento, mese e anno di competenza.
    /// Tutti i file originali vengono conservati nello storage e linkati al batch.
    /// </summary>
    [HttpPost("import-fatture")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> ImportFatturePost(List<IFormFile> files, string? note)
    {
        if (files == null || files.Count == 0)
        {
            TempData["flash-err"] = "Nessun file selezionato.";
            return RedirectToAction(nameof(ImportFatture));
        }
        if (files.Sum(f => f.Length) > MaxUploadBytes)
        {
            TempData["flash-err"] = "Dimensione totale dei file oltre il limite (25 MB).";
            return RedirectToAction(nameof(ImportFatture));
        }

        var tid = _tenant.TenantId!;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var userName = User.Identity?.Name ?? "—";

        var batch = new ImportFatturePassiveBatch
        {
            TenantId = tid,
            DataCaricamento = DateTime.UtcNow,
            CaricatoDaUserId = userId,
            CaricatoDaNome = userName,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

        var righeBatch = new List<ImportFatturaRiga>();
        var fatturePdf = new List<FattureImportPdfParser.PdfFattura>();
        // Buffer (fileName, bytes, contentType) per salvare tutti i file dopo
        // l'assegnazione del batch.Id (per organizzare lo storage).
        var bufferFiles = new List<(string Name, byte[] Bytes, string? ContentType, string TipoFile)>();

        try
        {
            var xlsx = files.FirstOrDefault(f => f.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            var csvs = files.Where(f => f.FileName.EndsWith(".csv",  StringComparison.OrdinalIgnoreCase)).ToList();
            var pdfs = files.Where(f => f.FileName.EndsWith(".pdf",  StringComparison.OrdinalIgnoreCase)).ToList();

            if (xlsx != null && (csvs.Count > 0 || pdfs.Count > 0))
            {
                TempData["flash-err"] = "Carica .xlsx da solo, oppure CSV (+ PDF facoltativo).";
                return RedirectToAction(nameof(ImportFatture));
            }
            if (xlsx == null && csvs.Count == 0 && pdfs.Count == 0)
            {
                TempData["flash-err"] = "Formati supportati: .xlsx, .csv (uno o due) o .pdf (uno).";
                return RedirectToAction(nameof(ImportFatture));
            }
            if (pdfs.Count > 1)
            {
                TempData["flash-err"] = "Carica un solo PDF alla volta.";
                return RedirectToAction(nameof(ImportFatture));
            }

            // ── XLSX ───────────────────────────────────────────────────
            if (xlsx != null)
            {
                batch.Tipo = TipoImportFatture.Xlsx;
                using var ms = new MemoryStream();
                await xlsx.CopyToAsync(ms);
                var bytes = ms.ToArray();
                bufferFiles.Add((xlsx.FileName, bytes, xlsx.ContentType, "xlsx"));
                var parsed = FattureImportParser.ParseXlsx(xlsx.FileName, bytes);
                if (parsed.Count == 0)
                {
                    TempData["flash-err"] = "Il file XLSX non contiene fogli leggibili.";
                    return RedirectToAction(nameof(ImportFatture));
                }
                foreach (var p in parsed) AppendParsedFile(batch, righeBatch, p);
            }
            // ── CSV (+ PDF facoltativo) ────────────────────────────────
            else
            {
                batch.Tipo = pdfs.Count > 0
                    ? (csvs.Count > 0 ? TipoImportFatture.CsvPdf : TipoImportFatture.Pdf)
                    : TipoImportFatture.Csv;

                foreach (var f in csvs)
                {
                    using var ms = new MemoryStream();
                    await f.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    bufferFiles.Add((f.FileName, bytes, f.ContentType, "csv"));
                    var parsed = FattureImportParser.ParseCsv(f.FileName, bytes);
                    AppendParsedFile(batch, righeBatch, parsed);
                }

                if (pdfs.Count == 1)
                {
                    var pdfFile = pdfs[0];
                    using var ms = new MemoryStream();
                    await pdfFile.CopyToAsync(ms);
                    var pdfBytes = ms.ToArray();
                    bufferFiles.Add((pdfFile.FileName, pdfBytes, pdfFile.ContentType ?? "application/pdf", "pdf"));

                    var parsedPdf = FattureImportPdfParser.Parse(pdfFile.FileName, pdfBytes);
                    fatturePdf.AddRange(parsedPdf.Fatture);

                    var hdr = new ImportFatturaFile
                    {
                        NomeFile = parsedPdf.NomeFile,
                        Sezione = "PDF",
                        DimensioneByte = parsedPdf.DimensioneByte,
                        ChecksumSha256 = parsedPdf.ChecksumSha256,
                        TipoFile = "pdf",
                        FattureEstrattePdf = parsedPdf.Fatture.Count,
                        RigheTotali = parsedPdf.Fatture.Count,
                        RigheValide = parsedPdf.Fatture.Count,
                        RigheConErrore = 0
                    };
                    batch.Files.Add(hdr);

                    // ── Match PDF ↔ CSV per (numero, data, fornitore) ────
                    // Quando la riga non ha CSV (solo PDF), creiamo una riga
                    // sintetica con i dati estratti dal PDF.
                    if (righeBatch.Count > 0)
                    {
                        ArricchisciRigheConPdf(righeBatch, fatturePdf);
                    }
                    else
                    {
                        foreach (var fp in fatturePdf)
                        {
                            righeBatch.Add(RigaDaPdf(fp, parsedPdf.NomeFile));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore parsing import fatture");
            TempData["flash-err"] = "Errore nel parsing del file: " + ex.Message;
            return RedirectToAction(nameof(ImportFatture));
        }

        if (righeBatch.Count == 0)
        {
            TempData["flash-err"] = "Nessuna riga importata: verifica intestazioni e contenuto del file.";
            return RedirectToAction(nameof(ImportFatture));
        }

        // ── Persistenza file originali nello storage (per audit/scarica) ──
        // L'XLSX produce N entry in batch.Files (una per sheet, nome "file.xlsx#Sheet"):
        // assegniamo lo stesso StoragePath a tutte le entry il cui nome inizia con il file fisico.
        var folder = $"tesoreria/import-fatture/{batch.Id}";
        foreach (var (name, bytes, ct, tipo) in bufferFiles)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var stored = await _storage.SaveAsync(tid, folder, name, ms, ct);
                var prefix = name; // es. "Scadenziario.xlsx" → match "Scadenziario.xlsx#Foglio1"
                var matched = batch.Files.Where(f =>
                    f.NomeFile.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    f.NomeFile.StartsWith(prefix + "#", StringComparison.OrdinalIgnoreCase));
                foreach (var fileHdr in matched)
                {
                    fileHdr.StoragePath = stored.RelativePath;
                    if (string.IsNullOrEmpty(fileHdr.TipoFile)) fileHdr.TipoFile = tipo;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossibile salvare il file originale {Name}", name);
            }
        }

        batch.TotaleRighe = righeBatch.Count;
        batch.RigheConErrore = righeBatch.Count(r => r.HaErrori);
        batch.RigheValide = batch.TotaleRighe - batch.RigheConErrore;

        await _mongo.ImportFattureBatches.InsertOneAsync(batch);
        foreach (var r in righeBatch)
        {
            r.TenantId = tid;
            r.BatchId = batch.Id;
        }
        await _mongo.ImportFattureRighe.InsertManyAsync(righeBatch);

        // ── Proposte di aggiornamento anagrafica fornitori ────────────
        int proposteCreate = 0;
        if (fatturePdf.Count > 0)
        {
            proposteCreate = await GeneraProposteAnagraficaAsync(tid, batch.Id, fatturePdf);
        }

        var pdfSummary = fatturePdf.Count > 0
            ? $" · {fatturePdf.Count} fatture estratte dal PDF · {proposteCreate} proposte anagrafica da rivedere"
            : "";
        TempData["flash"] =
            $"Importate {batch.TotaleRighe} righe (valide {batch.RigheValide}, con errori {batch.RigheConErrore}){pdfSummary}.";
        return RedirectToAction(nameof(ImportFattureDettaglio), new { id = batch.Id });
    }

    // ─────────────────────────────────────────────────────────────────
    //  Match PDF ↔ CSV + arricchimento riga
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per ogni riga CSV cerca la fattura PDF corrispondente per
    /// (NumeroDoc normalizzato, DataDoc, Fornitore normalizzato) e ne copia
    /// anagrafica, IBAN, modalità di pagamento, scadenza e competenza.
    /// </summary>
    private static void ArricchisciRigheConPdf(
        List<ImportFatturaRiga> righe,
        List<FattureImportPdfParser.PdfFattura> pdfFatture)
    {
        if (pdfFatture.Count == 0) return;

        // Indice per match veloce
        var byKey = new Dictionary<string, FattureImportPdfParser.PdfFattura>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in pdfFatture)
        {
            var k = KeyFattura(f.NumeroDoc, f.DataDoc, f.RagioneSociale);
            if (!string.IsNullOrEmpty(k) && !byKey.ContainsKey(k)) byKey[k] = f;
        }

        foreach (var r in righe)
        {
            var k = KeyFattura(r.Numero, r.DataDocumento, r.Fornitore);
            if (string.IsNullOrEmpty(k) || !byKey.TryGetValue(k, out var fp))
            {
                // tentativo di fallback per solo (Numero, Fornitore) — meno preciso
                var k2 = KeyFattura(r.Numero, null, r.Fornitore);
                fp = pdfFatture.FirstOrDefault(x =>
                    KeyFattura(x.NumeroDoc, null, x.RagioneSociale) == k2);
                if (fp == null) continue;
            }

            r.ArricchitaDaPdf = true;
            r.MeseCompetenza = fp.MeseCompetenza;
            r.AnnoCompetenza = fp.AnnoCompetenza;
            r.DataScadenzaPdf = fp.DataScadenza;
            r.ModalitaPagamentoPdf = string.IsNullOrEmpty(fp.CodiceModalitaPagamento)
                ? fp.DescrModalitaPagamento
                : $"{fp.CodiceModalitaPagamento} {fp.DescrModalitaPagamento}".Trim();

            r.PartitaIvaFornitore   = fp.PartitaIva;
            r.CodiceFiscaleFornitore = fp.CodiceFiscale;
            r.IbanFornitore         = fp.Iban;
            r.IndirizzoFornitore    = fp.Indirizzo;
            r.CapFornitore          = fp.Cap;
            r.LocalitaFornitore     = fp.Localita;
            r.ProvinciaFornitore    = fp.Provincia;
            r.PaeseFornitore        = fp.Paese;
            r.CodiceSdiFornitore    = fp.CodiceDestinatario;
            r.PecFornitore          = fp.Pec;
            r.EmailFornitore        = fp.Email;
            r.TelefonoFornitore     = fp.Telefono;
            r.PaginaPdf             = fp.PaginaStart;
        }
    }

    private static string KeyFattura(string? numero, DateTime? data, string? fornitore)
    {
        // Numero: rimuovo whitespace e uppercase. Lascio punteggiatura ("12/26"
        // resta distinto da "1226"). Questo neutralizza differenze tipo
        // "FPR 12/26" (CSV) vs "FPR12/26" (PDF) o spazi di troppo.
        var rawN = (numero ?? "").ToUpperInvariant();
        var sbN = new System.Text.StringBuilder(rawN.Length);
        foreach (var c in rawN) if (!char.IsWhiteSpace(c)) sbN.Append(c);
        var n = sbN.ToString();
        var f = NormalizzaRagioneSociale(fornitore ?? "");
        var d = data?.ToString("yyyyMMdd") ?? "";
        if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(f)) return "";
        return $"{n}|{d}|{f}";
    }

    /// <summary>Costruisce una riga import a partire da una fattura letta SOLO da PDF (senza CSV).</summary>
    private static ImportFatturaRiga RigaDaPdf(FattureImportPdfParser.PdfFattura fp, string nomeFile)
    {
        return new ImportFatturaRiga
        {
            NomeFile = nomeFile,
            Sezione = "PDF",
            NumeroRiga = fp.PaginaStart,
            Numero = fp.NumeroDoc,
            DataDocumento = fp.DataDoc,
            DataRegistrazione = fp.DataDoc,
            DataRicezione = fp.DataDoc,
            Fornitore = fp.RagioneSociale,
            TipoDocumento = fp.DescrTipo ?? fp.TipoDoc,
            Causale = fp.PeriodoRiferimentoRaw,
            Valuta = "EUR",
            ArricchitaDaPdf = true,
            MeseCompetenza = fp.MeseCompetenza,
            AnnoCompetenza = fp.AnnoCompetenza,
            DataScadenzaPdf = fp.DataScadenza,
            ModalitaPagamentoPdf = string.IsNullOrEmpty(fp.CodiceModalitaPagamento)
                ? fp.DescrModalitaPagamento
                : $"{fp.CodiceModalitaPagamento} {fp.DescrModalitaPagamento}".Trim(),
            PartitaIvaFornitore = fp.PartitaIva,
            CodiceFiscaleFornitore = fp.CodiceFiscale,
            IbanFornitore = fp.Iban,
            IndirizzoFornitore = fp.Indirizzo,
            CapFornitore = fp.Cap,
            LocalitaFornitore = fp.Localita,
            ProvinciaFornitore = fp.Provincia,
            PaeseFornitore = fp.Paese,
            CodiceSdiFornitore = fp.CodiceDestinatario,
            PecFornitore = fp.Pec,
            EmailFornitore = fp.Email,
            TelefonoFornitore = fp.Telefono,
            PaginaPdf = fp.PaginaStart
        };
    }

    // ─────────────────────────────────────────────────────────────────
    //  Proposte anagrafica fornitore (coda di revisione)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per ogni fattura PDF, confronta i dati anagrafici letti con il fornitore
    /// corrispondente in anagrafica (match per nome normalizzato) e crea una
    /// <see cref="PropostaAnagraficaFornitore"/> per ogni campo che diverge o
    /// risulta non popolato. Le proposte sono in stato InAttesa: il Backoffice
    /// le approva o le scarta dalla coda di revisione.
    /// Dedup: lo stesso (Fornitore, Campo, ValoreProposto) genera una sola proposta per batch.
    /// </summary>
    private async Task<int> GeneraProposteAnagraficaAsync(
        string tid, string batchId,
        List<FattureImportPdfParser.PdfFattura> pdfFatture)
    {
        var fornitori = await _mongo.Fornitori.Find(f => f.TenantId == tid).ToListAsync();
        var fornitoriByKey = new Dictionary<string, Fornitore>(StringComparer.Ordinal);
        foreach (var f in fornitori)
        {
            var k = NormalizzaRagioneSociale(f.RagioneSociale);
            if (!string.IsNullOrEmpty(k) && !fornitoriByKey.ContainsKey(k))
                fornitoriByKey[k] = f;
        }

        var proposte = new List<PropostaAnagraficaFornitore>();
        // Dedup per (RagSocKey + Campo + ValoreProposto)
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fp in pdfFatture)
        {
            var key = NormalizzaRagioneSociale(fp.RagioneSociale ?? "");
            if (string.IsNullOrEmpty(key)) continue;
            fornitoriByKey.TryGetValue(key, out var esistente);

            void Aggiungi(string campo, string? valoreAttuale, string? valorePdf)
            {
                if (string.IsNullOrWhiteSpace(valorePdf)) return;
                var attuale = (valoreAttuale ?? "").Trim();
                var nuovo = valorePdf.Trim();
                if (string.Equals(attuale, nuovo, StringComparison.OrdinalIgnoreCase)) return;
                var sig = $"{key}|{campo}|{nuovo.ToUpperInvariant()}";
                if (!seen.Add(sig)) return;
                proposte.Add(new PropostaAnagraficaFornitore
                {
                    TenantId = tid,
                    BatchId = batchId,
                    PaginaPdf = fp.PaginaStart,
                    FornitoreId = esistente?.Id,
                    RagioneSocialePdf = fp.RagioneSociale ?? "",
                    Campo = campo,
                    ValoreAttuale = string.IsNullOrEmpty(attuale) ? null : attuale,
                    ValoreProposto = nuovo,
                    FatturaRiferimento = fp.NumeroDoc,
                    Stato = StatoPropostaAnagrafica.InAttesa
                });
            }

            Aggiungi("PartitaIva",     esistente?.PartitaIva,     fp.PartitaIva);
            Aggiungi("CodiceFiscale",  esistente?.CodiceFiscale,  fp.CodiceFiscale);
            Aggiungi("Iban",           esistente?.Iban,           fp.Iban);
            Aggiungi("Indirizzo",      esistente?.Indirizzo,      fp.Indirizzo);
            Aggiungi("CodicePostale",  esistente?.CodicePostale,  fp.Cap);
            Aggiungi("Localita",       esistente?.Localita,       fp.Localita);
            Aggiungi("Provincia",      esistente?.Provincia,      fp.Provincia);
            Aggiungi("CodiceSdi",      esistente?.CodiceSdi,      fp.CodiceDestinatario);
            Aggiungi("Pec",            esistente?.Pec,            fp.Pec);
            Aggiungi("EmailContatto",  esistente?.EmailContatto,  fp.Email);
            Aggiungi("Telefono",       esistente?.Telefono,       fp.Telefono);
        }

        if (proposte.Count > 0)
            await _mongo.ProposteAnagraficaFornitori.InsertManyAsync(proposte);
        return proposte.Count;
    }

    // ─────────────────────────────────────────────────────────────────
    //  Coda di revisione: proposte anagrafica fornitori
    // ─────────────────────────────────────────────────────────────────

    [HttpGet("import-fatture/proposte")]
    public async Task<IActionResult> ProposteAnagrafica(string stato = "InAttesa")
    {
        var tid = _tenant.TenantId!;
        var parsedStato = Enum.TryParse<StatoPropostaAnagrafica>(stato, true, out var st)
            ? st : StatoPropostaAnagrafica.InAttesa;

        var proposte = await _mongo.ProposteAnagraficaFornitori
            .Find(p => p.TenantId == tid && p.Stato == parsedStato)
            .SortByDescending(p => p.CreatedAt)
            .ToListAsync();

        var conteggi = new Dictionary<string, long>();
        foreach (StatoPropostaAnagrafica s in Enum.GetValues(typeof(StatoPropostaAnagrafica)))
        {
            conteggi[s.ToString()] = await _mongo.ProposteAnagraficaFornitori
                .CountDocumentsAsync(p => p.TenantId == tid && p.Stato == s);
        }

        ViewBag.Section = "tesoreria-import-fatture";
        ViewBag.Conteggi = conteggi;
        ViewBag.StatoCorrente = parsedStato.ToString();
        return View("ProposteAnagrafica", proposte);
    }

    [HttpPost("import-fatture/proposte/{id}/decidi")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecidiPropostaAnagrafica(string id, string azione, string? nota = null)
    {
        var tid = _tenant.TenantId!;
        var p = await _mongo.ProposteAnagraficaFornitori
            .Find(x => x.Id == id && x.TenantId == tid)
            .FirstOrDefaultAsync();
        if (p == null) return NotFound();

        if (p.Stato != StatoPropostaAnagrafica.InAttesa)
        {
            TempData["flash-err"] = "Proposta già decisa in precedenza.";
            return RedirectToAction(nameof(ProposteAnagrafica));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var userName = User.Identity?.Name ?? "—";

        if (string.Equals(azione, "approva", StringComparison.OrdinalIgnoreCase))
        {
            // Applica il valore al fornitore (se esiste). Se il fornitore non
            // esiste ancora in anagrafica, lo creiamo on-the-fly.
            Fornitore? forn = null;
            if (!string.IsNullOrEmpty(p.FornitoreId))
                forn = await _mongo.Fornitori.Find(f => f.Id == p.FornitoreId && f.TenantId == tid).FirstOrDefaultAsync();
            if (forn == null && !string.IsNullOrWhiteSpace(p.RagioneSocialePdf))
            {
                // crea il fornitore con codice progressivo
                var fornitoriEsistenti = await _mongo.Fornitori.Find(f => f.TenantId == tid).Project(f => f.Codice).ToListAsync();
                var maxCod = 0;
                foreach (var c in fornitoriEsistenti)
                {
                    if (string.IsNullOrEmpty(c) || c[0] != 'F') continue;
                    if (int.TryParse(c.AsSpan(1), out var n) && n > maxCod) maxCod = n;
                }
                forn = new Fornitore
                {
                    TenantId = tid,
                    Codice = $"F{(maxCod + 1):D4}",
                    RagioneSociale = p.RagioneSocialePdf.Trim(),
                    Stato = StatoFornitore.Attivo
                };
                await _mongo.Fornitori.InsertOneAsync(forn);
                p.FornitoreId = forn.Id;
            }

            if (forn != null)
            {
                ApplicaPropostaSuFornitore(forn, p.Campo, p.ValoreProposto);
                forn.UpdatedAt = DateTime.UtcNow;
                await _mongo.Fornitori.ReplaceOneAsync(f => f.Id == forn.Id, forn);
            }
            p.Stato = StatoPropostaAnagrafica.Approvata;
        }
        else
        {
            p.Stato = StatoPropostaAnagrafica.Rifiutata;
        }

        p.DecisaIl = DateTime.UtcNow;
        p.DecisaDaUserId = userId;
        p.DecisaDaNome = userName;
        p.NotaDecisione = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        await _mongo.ProposteAnagraficaFornitori.ReplaceOneAsync(x => x.Id == p.Id, p);

        TempData["flash"] = p.Stato == StatoPropostaAnagrafica.Approvata
            ? $"✓ {p.Campo} di «{p.RagioneSocialePdf}» aggiornato."
            : $"Proposta su {p.Campo} rifiutata.";
        return RedirectToAction(nameof(ProposteAnagrafica));
    }

    /// <summary>
    /// Decisione massiva: approva o rifiuta in un colpo solo tutte le proposte
    /// in stato InAttesa per il tenant. Approvazione: aggrega le proposte per
    /// fornitore, applica tutte le modifiche in-memory, salva con un singolo
    /// ReplaceOne per fornitore + BulkWrite per le proposte. Crea fornitori
    /// nuovi al volo con codice F#### progressivo.
    /// </summary>
    [HttpPost("import-fatture/proposte/decidi-tutto")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DecidiTutteLeProposte(string azione)
    {
        var tid = _tenant.TenantId!;
        var proposte = await _mongo.ProposteAnagraficaFornitori
            .Find(p => p.TenantId == tid && p.Stato == StatoPropostaAnagrafica.InAttesa)
            .ToListAsync();

        if (proposte.Count == 0)
        {
            TempData["flash"] = "Nessuna proposta in attesa.";
            return RedirectToAction(nameof(ProposteAnagrafica));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var userName = User.Identity?.Name ?? "—";
        var now = DateTime.UtcNow;
        var isApprova = string.Equals(azione, "approva", StringComparison.OrdinalIgnoreCase);
        var statoFinale = isApprova ? StatoPropostaAnagrafica.Approvata : StatoPropostaAnagrafica.Rifiutata;

        var nuoviFornitori = new List<Fornitore>();
        var fornitoriDaAggiornare = new Dictionary<string, Fornitore>(StringComparer.Ordinal);
        int campiApplicati = 0;

        if (isApprova)
        {
            // Carica anagrafica fornitori una sola volta + indicizza per nome.
            var fornitori = await _mongo.Fornitori.Find(f => f.TenantId == tid).ToListAsync();
            var fornitoriById = fornitori.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);
            var fornitoriByName = new Dictionary<string, Fornitore>(StringComparer.Ordinal);
            foreach (var f in fornitori)
            {
                var k = NormalizzaRagioneSociale(f.RagioneSociale);
                if (!string.IsNullOrEmpty(k) && !fornitoriByName.ContainsKey(k))
                    fornitoriByName[k] = f;
            }

            int maxCod = 0;
            foreach (var f in fornitori)
            {
                if (string.IsNullOrEmpty(f.Codice) || f.Codice[0] != 'F') continue;
                if (int.TryParse(f.Codice.AsSpan(1), out var n) && n > maxCod) maxCod = n;
            }

            foreach (var p in proposte)
            {
                Fornitore? forn = null;
                if (!string.IsNullOrEmpty(p.FornitoreId))
                    fornitoriById.TryGetValue(p.FornitoreId, out forn);
                if (forn == null)
                {
                    var k = NormalizzaRagioneSociale(p.RagioneSocialePdf);
                    if (!string.IsNullOrEmpty(k))
                        fornitoriByName.TryGetValue(k, out forn);
                }
                if (forn == null && !string.IsNullOrWhiteSpace(p.RagioneSocialePdf))
                {
                    maxCod++;
                    forn = new Fornitore
                    {
                        TenantId = tid,
                        Codice = $"F{maxCod:D4}",
                        RagioneSociale = p.RagioneSocialePdf.Trim(),
                        Stato = StatoFornitore.Attivo
                    };
                    nuoviFornitori.Add(forn);
                    fornitoriById[forn.Id] = forn;
                    var k2 = NormalizzaRagioneSociale(forn.RagioneSociale);
                    if (!string.IsNullOrEmpty(k2)) fornitoriByName[k2] = forn;
                }

                if (forn != null)
                {
                    ApplicaPropostaSuFornitore(forn, p.Campo, p.ValoreProposto);
                    forn.UpdatedAt = now;
                    p.FornitoreId = forn.Id;
                    if (!nuoviFornitori.Contains(forn))
                        fornitoriDaAggiornare[forn.Id] = forn;
                    campiApplicati++;
                }

                p.Stato = statoFinale;
                p.DecisaIl = now;
                p.DecisaDaUserId = userId;
                p.DecisaDaNome = userName;
                p.NotaDecisione = "Approvazione massiva";
            }
        }
        else
        {
            foreach (var p in proposte)
            {
                p.Stato = statoFinale;
                p.DecisaIl = now;
                p.DecisaDaUserId = userId;
                p.DecisaDaNome = userName;
                p.NotaDecisione = "Rifiuto massivo";
            }
        }

        // Persistenza in bulk
        if (nuoviFornitori.Count > 0)
            await _mongo.Fornitori.InsertManyAsync(nuoviFornitori);
        foreach (var f in fornitoriDaAggiornare.Values)
            await _mongo.Fornitori.ReplaceOneAsync(x => x.Id == f.Id, f);

        var bulk = proposte.Select(p => (WriteModel<PropostaAnagraficaFornitore>)
            new ReplaceOneModel<PropostaAnagraficaFornitore>(
                Builders<PropostaAnagraficaFornitore>.Filter.Eq(x => x.Id, p.Id), p)).ToList();
        if (bulk.Count > 0)
            await _mongo.ProposteAnagraficaFornitori.BulkWriteAsync(bulk);

        // Audit (operazione massiva, vale la pena tracciarla)
        await _mongo.Audit.InsertOneAsync(new AuditEntry
        {
            TenantId = tid,
            UserId = userId,
            UserName = userName,
            Action = isApprova ? AuditAction.Updated : AuditAction.Dismissed,
            EntityType = "ProposteAnagraficaFornitore",
            EntityId = tid,
            EntityLabel = isApprova ? "Approvazione massiva proposte anagrafica" : "Rifiuto massivo proposte anagrafica",
            Note = isApprova
                ? $"{proposte.Count} proposte approvate · {campiApplicati} campi applicati · " +
                  $"{fornitoriDaAggiornare.Count} fornitori aggiornati · {nuoviFornitori.Count} fornitori creati"
                : $"{proposte.Count} proposte rifiutate"
        });

        _logger.LogInformation("Decisione massiva proposte anagrafica · tenant {Tid} · {Azione} · " +
            "proposte={N1} campi={N2} updated={N3} nuovi={N4}",
            tid, isApprova ? "APPROVA" : "RIFIUTA",
            proposte.Count, campiApplicati, fornitoriDaAggiornare.Count, nuoviFornitori.Count);

        TempData["flash"] = isApprova
            ? $"✓ Approvate {proposte.Count} proposte: {campiApplicati} campi applicati su " +
              $"{fornitoriDaAggiornare.Count} fornitori" +
              (nuoviFornitori.Count > 0 ? $", {nuoviFornitori.Count} nuovi fornitori creati" : "") + "."
            : $"Rifiutate {proposte.Count} proposte.";
        return RedirectToAction(nameof(ProposteAnagrafica));
    }

    /// <summary>Applica un singolo campo della proposta al fornitore.</summary>
    private static void ApplicaPropostaSuFornitore(Fornitore f, string campo, string? valore)
    {
        switch (campo)
        {
            case "PartitaIva":     f.PartitaIva     = valore; break;
            case "CodiceFiscale":  f.CodiceFiscale  = valore; break;
            case "Iban":           f.Iban           = valore; break;
            case "Indirizzo":      f.Indirizzo      = valore; break;
            case "CodicePostale":  f.CodicePostale  = valore; break;
            case "Localita":       f.Localita       = valore; break;
            case "Provincia":      f.Provincia      = valore; break;
            case "CodiceSdi":      f.CodiceSdi      = valore; break;
            case "Pec":            f.Pec            = valore; break;
            case "EmailContatto":  f.EmailContatto  = valore; break;
            case "Telefono":       f.Telefono       = valore; break;
        }
    }

    /// <summary>Scarica un file originale di un batch di import (CSV/XLSX/PDF).</summary>
    [HttpGet("import-fatture/{batchId}/file")]
    public async Task<IActionResult> ScaricaFileBatch(string batchId, string nome)
    {
        var tid = _tenant.TenantId!;
        var batch = await _mongo.ImportFattureBatches
            .Find(b => b.Id == batchId && b.TenantId == tid)
            .FirstOrDefaultAsync();
        if (batch == null) return NotFound();

        var file = batch.Files.FirstOrDefault(f =>
            f.NomeFile.Equals(nome, StringComparison.OrdinalIgnoreCase));
        if (file == null || string.IsNullOrEmpty(file.StoragePath)) return NotFound();

        // Lo storage espone i file via static files (wwwroot/uploads/...).
        // Per scaricarli con il nome originale ed evitare cache, redirigiamo
        // semplicemente all'URL pubblico — l'utente farà "salva con nome".
        return Redirect(_storage.GetPublicUrl(file.StoragePath));
    }

    /// <summary>Scansiona tutti i fornitori citati nelle righe importate da
    /// "Importa fatture" e crea quelli mancanti nell'anagrafica fornitori,
    /// assegnando un codice progressivo F####.</summary>
    [HttpPost("import-fatture/sync-fornitori")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncFornitoriDaImport()
    {
        var tid = _tenant.TenantId!;

        var fornitoriEsistenti = await _mongo.Fornitori
            .Find(f => f.TenantId == tid)
            .Project(f => new { f.RagioneSociale, f.Codice })
            .ToListAsync();

        var giaPresenti = new HashSet<string>(
            fornitoriEsistenti
                .Select(f => NormalizzaRagioneSociale(f.RagioneSociale))
                .Where(s => !string.IsNullOrEmpty(s)),
            StringComparer.Ordinal);

        var maxCodice = 0;
        foreach (var f in fornitoriEsistenti)
        {
            if (string.IsNullOrEmpty(f.Codice) || f.Codice[0] != 'F') continue;
            if (int.TryParse(f.Codice.AsSpan(1), out var n) && n > maxCodice) maxCodice = n;
        }

        var nomiDallImport = await _mongo.ImportFattureRighe
            .Find(r => r.TenantId == tid && r.Fornitore != null && r.Fornitore != "")
            .Project(r => r.Fornitore!)
            .ToListAsync();

        // Dedup mantenendo la prima occorrenza (più "pulita") tra varianti uguali normalizzate
        var daCreare = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var nome in nomiDallImport)
        {
            var raw = (nome ?? "").Trim();
            if (raw.Length == 0) continue;
            var key = NormalizzaRagioneSociale(raw);
            if (string.IsNullOrEmpty(key)) continue;
            if (giaPresenti.Contains(key)) continue;
            if (daCreare.ContainsKey(key)) continue;
            daCreare[key] = raw;
        }

        if (daCreare.Count == 0)
        {
            TempData["flash"] = "Anagrafica fornitori già allineata: nessun nuovo fornitore da creare.";
            return RedirectToAction(nameof(ImportFatture));
        }

        var nuovi = new List<Fornitore>();
        foreach (var ragSoc in daCreare.Values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            maxCodice++;
            nuovi.Add(new Fornitore
            {
                TenantId = tid,
                Codice = $"F{maxCodice:D4}",
                RagioneSociale = ragSoc,
                CategoriaDefault = CategoriaSpesa.AltreSpeseFisse,
                Stato = StatoFornitore.Attivo,
                TerminiPagamentoGiorni = 30,
                BasePagamento = BasePagamento.DataFattura,
                Note = "Creato automaticamente dall'import fatture passive."
            });
        }
        await _mongo.Fornitori.InsertManyAsync(nuovi);

        TempData["flash"] = $"Aggiunti {nuovi.Count} nuovi fornitori dall'anagrafica import fatture.";
        return RedirectToAction(nameof(ImportFatture));
    }

    /// <summary>Normalizza una ragione sociale per il confronto: lowercase,
    /// rimozione spazi/punteggiatura non significativa, collapse di whitespace.
    /// Sufficiente a riconoscere "AGESP SPA" == "AGESP S.P.A.".</summary>
    private static string NormalizzaRagioneSociale(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static void AppendParsedFile(
        ImportFatturePassiveBatch batch,
        List<ImportFatturaRiga> accumulator,
        FattureImportParser.ParsedFile parsed)
    {
        var righeConErr = parsed.Righe.Count(r => r.HaErrori);
        batch.Files.Add(new ImportFatturaFile
        {
            NomeFile = parsed.NomeFile,
            Sezione = parsed.Sezione,
            DimensioneByte = parsed.DimensioneByte,
            ChecksumSha256 = parsed.ChecksumSha256,
            RigheTotali = parsed.Righe.Count,
            RigheConErrore = righeConErr,
            RigheValide = parsed.Righe.Count - righeConErr
        });
        accumulator.AddRange(parsed.Righe);
    }

    // ─────────────────────────────────────────────────────────────
    //  GENERA SCADENZIARIO da fatture importate
    //  Applica le regole del file "Regole scadenzario.xlsx":
    //  - classificazione fornitore (medici / lab / Invisalign / CC / Compass / DB / generico)
    //  - termini di pagamento differenziati (30 gg fm / 60 gg fm / 150 gg DF)
    //  - snap dei bonifici al 10 o al 30/31 del mese
    //  - gestione ritenute (netto bonifico + ritenuta al 16 mese successivo)
    //  - note di credito (segno negativo, alert)
    //  - duplicati & scostamenti importo (Cristal/Infinity/Locazioni vs altri)
    //  - autocompletamento IBAN dalla fattura precedente dello stesso fornitore
    //  La pagina è una preview (dry-run): la generazione effettiva avviene su POST con conferma.
    // ─────────────────────────────────────────────────────────────

    [HttpGet("genera-scadenziario")]
    public async Task<IActionResult> GeneraScadenziario()
    {
        var vm = await BuildGeneraScadenziarioPreviewAsync(false);
        ViewBag.Section = "tesoreria";
        return View("GeneraScadenziario", vm);
    }

    /// <summary>
    /// Reset distruttivo: cancella TUTTI i dati di tesoreria del tenant —
    /// scadenze, fatture, distinte SEPA, batch e righe di import fatture.
    /// I fornitori sono mantenuti (anagrafica). Riservato a Owner+Management;
    /// richiede conferma esplicita.
    /// </summary>
    [HttpPost("reset-completo")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetScadenziarioCompleto(bool confermaReset = false, string? note = null)
    {
        if (!confermaReset)
        {
            TempData["flash-err"] = "Conferma esplicita richiesta: spunta la casella di sicurezza per azzerare lo scadenziario.";
            return RedirectToAction(nameof(ImportFatture));
        }

        var tid = _tenant.TenantId!;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var nScad      = await _mongo.ScadenzePagamento.CountDocumentsAsync(s => s.TenantId == tid);
        var nFatt      = await _mongo.Fatture.CountDocumentsAsync(f => f.TenantId == tid);
        var nDistinte  = await _mongo.DistinteSepa.CountDocumentsAsync(d => d.TenantId == tid);
        var nBatch     = await _mongo.ImportFattureBatches.CountDocumentsAsync(b => b.TenantId == tid);
        var nRighe     = await _mongo.ImportFattureRighe.CountDocumentsAsync(r => r.TenantId == tid);
        var nProposte  = await _mongo.ProposteAnagraficaFornitori.CountDocumentsAsync(p => p.TenantId == tid);

        await _mongo.ScadenzePagamento.DeleteManyAsync(s => s.TenantId == tid);
        await _mongo.Fatture.DeleteManyAsync(f => f.TenantId == tid);
        await _mongo.DistinteSepa.DeleteManyAsync(d => d.TenantId == tid);
        await _mongo.ImportFattureRighe.DeleteManyAsync(r => r.TenantId == tid);
        await _mongo.ImportFattureBatches.DeleteManyAsync(b => b.TenantId == tid);
        await _mongo.ProposteAnagraficaFornitori.DeleteManyAsync(p => p.TenantId == tid);

        await _mongo.Audit.InsertOneAsync(new AuditEntry
        {
            TenantId = tid,
            UserId = userId ?? "",
            UserName = User.Identity?.Name ?? "",
            Action = AuditAction.Deleted,
            EntityType = "Scadenziario",
            EntityId = tid,
            EntityLabel = "Reset completo tesoreria",
            Note = $"Cancellate {nScad} scadenze, {nFatt} fatture, {nDistinte} distinte SEPA, " +
                   $"{nBatch} batch, {nRighe} righe di import, {nProposte} proposte anagrafica." +
                   (string.IsNullOrWhiteSpace(note) ? "" : $" Motivo: {note}")
        });

        _logger.LogWarning("Reset completo tesoreria · tenant {Tid} · scad={N1} fatt={N2} distinte={N3} batch={N4} righe={N5} proposte={N6}",
            tid, nScad, nFatt, nDistinte, nBatch, nRighe, nProposte);

        TempData["flash"] = $"✓ Scadenziario azzerato: cancellate {nScad} scadenze, {nFatt} fatture, " +
                            $"{nDistinte} distinte SEPA, {nBatch} batch, {nRighe} righe di import e " +
                            $"{nProposte} proposte anagrafica. Anagrafica fornitori mantenuta.";
        return RedirectToAction(nameof(ImportFatture));
    }

    [HttpPost("genera-scadenziario")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneraScadenziarioApply(bool confermaCancellazione = false)
    {
        if (!confermaCancellazione)
        {
            TempData["flash-err"] = "Conferma esplicita richiesta: spunta la casella di sicurezza prima di rigenerare.";
            return RedirectToAction(nameof(GeneraScadenziario));
        }

        var tid = _tenant.TenantId!;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // 1) Wipe scadenziario corrente (scadenze + fatture). I batch di import restano,
        //    sono lo storico di provenienza e non vengono toccati.
        var oldScadCount = await _mongo.ScadenzePagamento.CountDocumentsAsync(s => s.TenantId == tid);
        var oldFattCount = await _mongo.Fatture.CountDocumentsAsync(f => f.TenantId == tid);
        await _mongo.ScadenzePagamento.DeleteManyAsync(s => s.TenantId == tid);
        await _mongo.Fatture.DeleteManyAsync(f => f.TenantId == tid);

        // 2) Carica fornitori e cliniche
        var fornitoriCorrenti = await _mongo.Fornitori.Find(f => f.TenantId == tid).ToListAsync();
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var dottori = await _mongo.Dottori.Find(d => d.TenantId == tid).ToListAsync();

        // 3) Carica TUTTE le righe importate (batch storici)
        var righe = await _mongo.ImportFattureRighe.Find(r => r.TenantId == tid).ToListAsync();

        var output = ScadenziarioGenerator.Genera(new ScadenziarioGenerator.Input
        {
            TenantId = tid,
            Righe = righe,
            Fornitori = fornitoriCorrenti,
            Cliniche = cliniche,
            Dottori = dottori,
            UserId = userId
        });

        // 4) Persisti nuovi fornitori (se ce ne sono) — assegna un codice F#### progressivo
        //    proseguendo la sequenza dei fornitori già esistenti.
        if (output.FornitoriNuovi.Count > 0)
        {
            var maxCodiceF = 0;
            foreach (var f in fornitoriCorrenti)
            {
                if (string.IsNullOrEmpty(f.Codice) || f.Codice[0] != 'F') continue;
                if (int.TryParse(f.Codice.AsSpan(1), out var n) && n > maxCodiceF) maxCodiceF = n;
            }
            foreach (var f in output.FornitoriNuovi)
            {
                if (string.IsNullOrWhiteSpace(f.Codice))
                {
                    maxCodiceF++;
                    f.Codice = $"F{maxCodiceF:D4}";
                }
            }
            await _mongo.Fornitori.InsertManyAsync(output.FornitoriNuovi);
        }

        // 5) Persisti fatture e scadenze
        if (output.Fatture.Count > 0)
            await _mongo.Fatture.InsertManyAsync(output.Fatture);
        if (output.Scadenze.Count > 0)
            await _mongo.ScadenzePagamento.InsertManyAsync(output.Scadenze);

        // 6) Audit (per tracciare l'operazione distruttiva)
        await _mongo.Audit.InsertOneAsync(new AuditEntry
        {
            TenantId = tid,
            UserId = userId ?? "",
            UserName = User.Identity?.Name ?? "",
            Action = AuditAction.Updated,
            EntityType = "Scadenziario",
            EntityId = tid,
            EntityLabel = "Rigenerazione scadenziario da fatture importate",
            Note = $"Cancellate {oldScadCount} scadenze e {oldFattCount} fatture. " +
                   $"Generate {output.Fatture.Count} fatture, {output.Scadenze.Count} scadenze, " +
                   $"{output.FornitoriNuovi.Count} nuovi fornitori. " +
                   $"Alert: {output.Alerts.Count}."
        });

        TempData["flash"] = $"✓ Scadenziario rigenerato: {output.Scadenze.Count} scadenze su {output.Fatture.Count} fatture. " +
                            $"{output.Alerts.Count} alert da verificare.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<GeneraScadenziarioViewModel> BuildGeneraScadenziarioPreviewAsync(bool isApplied)
    {
        var tid = _tenant.TenantId!;
        var oggi = DateTime.UtcNow.Date;

        var fornitoriCorrenti = await _mongo.Fornitori.Find(f => f.TenantId == tid).ToListAsync();
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tid).ToListAsync();
        var dottori = await _mongo.Dottori.Find(d => d.TenantId == tid).ToListAsync();
        var righe = await _mongo.ImportFattureRighe.Find(r => r.TenantId == tid).ToListAsync();
        var scadenzeAttuali = await _mongo.ScadenzePagamento.CountDocumentsAsync(s => s.TenantId == tid);
        var pagateAttuali = await _mongo.ScadenzePagamento.CountDocumentsAsync(s => s.TenantId == tid && s.Stato == StatoScadenza.Pagato);
        var fattureAttuali = await _mongo.Fatture.CountDocumentsAsync(f => f.TenantId == tid);

        var output = ScadenziarioGenerator.Genera(new ScadenziarioGenerator.Input
        {
            TenantId = tid,
            Righe = righe,
            Fornitori = fornitoriCorrenti,
            Cliniche = cliniche,
            Dottori = dottori,
            UserId = null
        });

        var fornByIdAll = fornitoriCorrenti.Concat(output.FornitoriNuovi).ToDictionary(f => f.Id, f => f);
        var clinByIdAll = cliniche.ToDictionary(c => c.Id, c => c);

        var anteprima = output.Scadenze
            .OrderBy(s => s.DataScadenza)
            .Take(200)
            .Select(s =>
            {
                var fatt = output.Fatture.FirstOrDefault(f => f.Id == s.FatturaId);
                var forn = fornByIdAll.GetValueOrDefault(s.FornitoreId);
                var cli  = !string.IsNullOrEmpty(s.ClinicaId) ? clinByIdAll.GetValueOrDefault(s.ClinicaId) : null;
                return new AnteprimaScadenza
                {
                    Fornitore = forn?.NomePerPagamento ?? "—",
                    NumeroDoc = fatt?.Numero ?? "—",
                    DataDoc = fatt?.DataEmissione ?? s.DataScadenza,
                    DataScadenza = s.DataScadenza,
                    Importo = s.Importo,
                    Metodo = s.Metodo.ToString(),
                    Stato = s.Stato.ToString(),
                    Categoria = s.Categoria.ToString(),
                    LOC = SiglaSede(cli),
                    Iban = s.Iban,
                    Note = s.Note
                };
            })
            .ToList();

        var alertRows = output.Alerts.Select(a => new AlertRow
        {
            Severita = a.Severita.ToString().ToLowerInvariant(),
            Regola = a.Regola,
            Messaggio = a.Messaggio,
            Fornitore = a.FornitoreNome,
            NumeroDoc = a.NumeroDoc,
            Data = a.DataDocumento,
            Riga = a.RigaSorgente
        }).ToList();

        return new GeneraScadenziarioViewModel
        {
            RigheImportateTotali = righe.Count,
            RigheElaborate = output.RigheElaborate,
            RigheSaltate = output.RigheSaltate,
            FattureGenerate = output.Fatture.Count,
            ScadenzeGenerate = output.Scadenze.Count,
            FornitoriNuovi = output.FornitoriNuovi.Count,
            ScadenzeAttuali = (int)scadenzeAttuali,
            ScadenzePagateAttuali = (int)pagateAttuali,
            FattureAttuali = (int)fattureAttuali,
            Alerts = alertRows,
            Anteprima = anteprima,
            IsApplied = isApplied,
            AlertsPerCategoria = alertRows.GroupBy(a => a.Regola).ToDictionary(g => g.Key, g => g.Count()),
            AlertsPerSeverita = alertRows.GroupBy(a => a.Severita).ToDictionary(g => g.Key, g => g.Count())
        };
    }
}
