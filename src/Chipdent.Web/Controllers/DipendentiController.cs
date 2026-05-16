using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Audit;
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
[Route("dipendenti")]
public class DipendentiController : Controller
{
    private const long MaxAllegatoBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllegatoEstensioniAmmesse = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx" };

    private readonly MongoContext _mongo;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly IFileStorage _storage;

    public DipendentiController(MongoContext mongo, ITenantContext tenant, IAuditService audit, IFileStorage storage)
    {
        _mongo = mongo;
        _tenant = tenant;
        _audit = audit;
        _storage = storage;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var items = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId)
            .SortBy(d => d.Cognome)
            .ToListAsync();

        var cliniche = await CliniceLookupAsync();

        var corsiNomina = await _mongo.Corsi
            .Find(c => c.TenantId == _tenant.TenantId
                && c.DestinatarioTipo == DestinatarioCorso.Dipendente
                && (c.Tipo == TipoCorso.RLS || c.Tipo == TipoCorso.Antincendio))
            .ToListAsync();
        var nomine = Chipdent.Web.Infrastructure.Rls.RlsAggregator
            .NomineAttivePerDipendente(corsiNomina, DateTime.UtcNow);

        ViewData["Section"] = "dipendenti";
        ViewData["Cliniche"] = cliniche;
        ViewData["Nomine"] = nomine;
        return View(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id, string tab = "anagrafica")
    {
        var d = await Load(id);
        if (d is null) return NotFound();

        var cliniche = await CliniceListAsync();
        var clinicaNome = cliniche.FirstOrDefault(c => c.Id == d.ClinicaId)?.Nome;

        string? managerNome = null;
        if (!string.IsNullOrEmpty(d.ManagerId))
        {
            var mgr = await _mongo.Dipendenti.Find(x => x.Id == d.ManagerId && x.TenantId == _tenant.TenantId).FirstOrDefaultAsync();
            managerNome = mgr?.NomeCompleto;
        }

        var storico = await _mongo.Trasferimenti
            .Find(t => t.TenantId == _tenant.TenantId && t.PersonaId == id && t.PersonaTipo == TipoPersona.Dipendente)
            .SortByDescending(t => t.DataEffetto)
            .ToListAsync();

        var audit = await _mongo.Audit
            .Find(a => a.TenantId == _tenant.TenantId && a.EntityType == "Dipendente" && a.EntityId == id)
            .SortByDescending(a => a.CreatedAt)
            .Limit(50)
            .ToListAsync();

        var distacchi = await _mongo.Distacchi
            .Find(x => x.TenantId == _tenant.TenantId && x.DipendenteId == id)
            .SortByDescending(x => x.DataInizio).ToListAsync();
        var visite = await _mongo.VisiteMediche
            .Find(v => v.TenantId == _tenant.TenantId && v.DipendenteId == id)
            .SortByDescending(v => v.Data).ToListAsync();
        var corsi = await _mongo.Corsi
            .Find(c => c.TenantId == _tenant.TenantId
                       && c.DestinatarioId == id
                       && c.DestinatarioTipo == DestinatarioCorso.Dipendente)
            .SortByDescending(c => c.DataConseguimento).ToListAsync();

        var disciplinari = await _mongo.Disciplinari
            .Find(p => p.TenantId == _tenant.TenantId && p.DipendenteId == id)
            .SortByDescending(p => p.DataApertura).ToListAsync();
        var premi = await _mongo.Premi
            .Find(p => p.TenantId == _tenant.TenantId && p.DipendenteId == id)
            .SortByDescending(p => p.Data).ToListAsync();
        var valutazioni = await _mongo.Valutazioni
            .Find(p => p.TenantId == _tenant.TenantId && p.DipendenteId == id)
            .SortByDescending(p => p.Data).ToListAsync();
        var documenti = await _mongo.DocumentiDipendente
            .Find(p => p.TenantId == _tenant.TenantId && p.DipendenteId == id)
            .ToListAsync();
        var cambiLivello = await _mongo.CambiLivello
            .Find(c => c.TenantId == _tenant.TenantId && c.DipendenteId == id)
            .SortByDescending(c => c.DataEffetto).ToListAsync();
        var cambiMansione = await _mongo.CambiMansione
            .Find(c => c.TenantId == _tenant.TenantId && c.DipendenteId == id)
            .SortByDescending(c => c.DataEffetto).ToListAsync();

        ViewData["Section"] = "dipendenti";
        return View("Profile", new DipendenteProfileViewModel
        {
            Dipendente = d,
            ClinicaCorrenteNome = clinicaNome,
            ManagerNome = managerNome,
            Storico = storico,
            Audit = audit,
            Cliniche = cliniche,
            Tab = tab,
            Distacchi = distacchi,
            VisiteMediche = visite,
            Corsi = corsi,
            Disciplinari = disciplinari,
            Premi = premi,
            Valutazioni = valutazioni,
            Documenti = documenti,
            CambiLivello = cambiLivello,
            CambiMansione = cambiMansione
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  ORGANIGRAMMA OPERATIVO
    // ─────────────────────────────────────────────────────────────
    [HttpGet("organigramma")]
    public async Task<IActionResult> Organigramma(string? clinicaId = null)
    {
        var attivi = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId && d.Stato != StatoDipendente.Cessato
                       && (clinicaId == null || d.ClinicaId == clinicaId))
            .SortBy(d => d.Cognome).ToListAsync();

        var cliniche = await CliniceListAsync();
        ViewData["Section"] = "dipendenti";
        ViewData["Cliniche"] = cliniche;
        ViewData["ClinicaIdFilter"] = clinicaId;
        return View("Organigramma", attivi);
    }

    // ─────────────────────────────────────────────────────────────
    //  EXPORT mensile movimenti (assunzioni/cessazioni/proroghe/distacchi)
    // ─────────────────────────────────────────────────────────────
    [HttpGet("movimenti")]
    public async Task<IActionResult> Movimenti(int? anno = null, int? mese = null)
    {
        var oggi = DateTime.UtcNow;
        var a = anno ?? oggi.Year;
        var m = mese ?? oggi.Month;

        var report = await BuildMovimentiAsync(a, m);

        ViewData["Section"] = "dipendenti";
        ViewData["Anno"] = a;
        ViewData["Mese"] = m;
        return View("Movimenti", report);
    }

    [HttpGet("movimenti/csv")]
    public async Task<IActionResult> MovimentiCsv(int anno, int mese)
    {
        var report = await BuildMovimentiAsync(anno, mese);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Mese/Anno;Società;N. assunzioni;N. annullamento assunzioni;N. cessazioni anticipate;N. contratti non rinnovati;N. contratti non rinnovati prossimo mese;N. proroghe;N. distacchi;N. rettifiche/annullamento distacchi;N. trasformazioni/aumento livello;N. trasferimenti sede;N. cambi mansione/reparto;Note");
        foreach (var r in report.Righe)
        {
            sb.Append(report.MeseAnnoLabel).Append(';');
            sb.Append(Csv(r.ClinicaNome)).Append(';');
            sb.Append(r.NumeroAssunzioni).Append(';');
            sb.Append(r.NumeroAnnullamentiAssunzione).Append(';');
            sb.Append(r.NumeroCessazioniAnticipate).Append(';');
            sb.Append(r.NumeroContrattiNonRinnovati).Append(';');
            sb.Append(r.NumeroContrattiNonRinnovatiProssimoMese).Append(';');
            sb.Append(r.NumeroProroghe).Append(';');
            sb.Append(r.NumeroDistacchi).Append(';');
            sb.Append(r.NumeroRettificheDistacchi).Append(';');
            sb.Append(r.NumeroTrasformazioniLivello).Append(';');
            sb.Append(r.NumeroTrasferimentiSede).Append(';');
            sb.Append(r.NumeroCambiMansione).Append(';');
            sb.Append(Csv(string.Join(" | ", r.Note))).AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"movimenti-dipendenti-{anno:0000}-{mese:00}.csv";
        return File(bytes, "text/csv", fileName);

        static string Csv(string? s) => s is null ? "" : (s.Contains(';') || s.Contains('"') || s.Contains('\n')) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    [HttpGet("movimenti/xlsx")]
    public async Task<IActionResult> MovimentiXlsx(int anno, int mese)
    {
        var report = await BuildMovimentiAsync(anno, mese);
        var bytes = Chipdent.Web.Infrastructure.Export.SimpleXlsxWriter.BuildMovimentiMensili(report);
        var fileName = $"movimenti-dipendenti-{anno:0000}-{mese:00}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private async Task<MovimentiMensiliReport> BuildMovimentiAsync(int anno, int mese)
    {
        var inizio = new DateTime(anno, mese, 1, 0, 0, 0, DateTimeKind.Utc);
        var fine = inizio.AddMonths(1);
        var inizioMeseSucc = fine;
        var fineMeseSucc = fine.AddMonths(1);

        var cliniche = await CliniceListAsync();
        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId)
            .ToListAsync();
        var distacchi = await _mongo.Distacchi
            .Find(x => x.TenantId == _tenant.TenantId)
            .ToListAsync();
        var trasferimenti = await _mongo.Trasferimenti
            .Find(t => t.TenantId == _tenant.TenantId
                       && t.PersonaTipo == TipoPersona.Dipendente
                       && t.DataEffetto >= inizio && t.DataEffetto < fine)
            .ToListAsync();
        var cambiLivelloMese = await _mongo.CambiLivello
            .Find(c => c.TenantId == _tenant.TenantId
                       && c.DataEffetto >= inizio && c.DataEffetto < fine)
            .ToListAsync();
        var cambiMansioneMese = await _mongo.CambiMansione
            .Find(c => c.TenantId == _tenant.TenantId
                       && c.DataEffetto >= inizio && c.DataEffetto < fine)
            .ToListAsync();

        var righe = new List<MovimentoMensileRiga>();
        foreach (var c in cliniche)
        {
            var dipClin = dipendenti.Where(d => d.ClinicaId == c.Id).ToList();
            var note = new List<string>();

            int assunzioni = dipClin.Count(d => d.DataAssunzione >= inizio && d.DataAssunzione < fine);
            int cessazioniAnticipate = dipClin.Count(d => d.DataDimissioni.HasValue
                && d.DataDimissioni.Value >= inizio && d.DataDimissioni.Value < fine
                && d.DataScadenzaContratto.HasValue
                && d.DataDimissioni.Value < d.DataScadenzaContratto.Value);
            int nonRinnovati = dipClin.Count(d => d.DataScadenzaContratto.HasValue
                && d.DataScadenzaContratto.Value >= inizio && d.DataScadenzaContratto.Value < fine
                && d.IsCessato);
            int nonRinnovatiProssimo = dipClin.Count(d => d.DataScadenzaContratto.HasValue
                && d.DataScadenzaContratto.Value >= inizioMeseSucc && d.DataScadenzaContratto.Value < fineMeseSucc
                && d.IsCessato);
            int proroghe = dipClin.Count(d => d.DataScadenzaProroga.HasValue
                && d.DataScadenzaProroga.Value >= inizio && d.DataScadenzaProroga.Value < fine);
            int trasformazioniContratto = dipClin.Count(d => d.DataTrasformazioneContratto.HasValue
                && d.DataTrasformazioneContratto.Value >= inizio && d.DataTrasformazioneContratto.Value < fine);
            int aumentiLivelloEntita = cambiLivelloMese.Count(x => x.ClinicaId == c.Id);
            int trasformazioniLivelloTotale = trasformazioniContratto + aumentiLivelloEntita;

            var nuoviDistacchi = distacchi.Count(x => x.ClinicaDistaccoId == c.Id
                && x.DataInizio >= inizio && x.DataInizio < fine);
            var rettificheDistacchi = distacchi.Count(x => x.ClinicaDistaccoId == c.Id
                && x.DataFine.HasValue && x.DataFine.Value >= inizio && x.DataFine.Value < fine);

            // Trasferimenti permanenti VERSO questa clinica (esclusa l'assegnazione iniziale che ha ClinicaDaId == null)
            var trasferimentiSede = trasferimenti.Count(t => t.ClinicaAId == c.Id
                && !string.IsNullOrEmpty(t.ClinicaDaId)
                && t.ClinicaDaId != c.Id);
            var cambiMansioneClinica = cambiMansioneMese.Count(x => x.ClinicaId == c.Id);

            // Annotazioni nominative
            foreach (var x in cambiLivelloMese.Where(x => x.ClinicaId == c.Id))
            {
                var label = x.Tipo switch
                {
                    TipoCambioLivello.AumentoLivello => "aumento livello",
                    TipoCambioLivello.AumentoRetributivo => "aumento retributivo",
                    TipoCambioLivello.AumentoLivelloERetributivo => "aumento livello + retributivo",
                    _ => "cambio livello"
                };
                if (!string.IsNullOrEmpty(x.LivelloDa) && !string.IsNullOrEmpty(x.LivelloA))
                    label += $" {x.LivelloDa}→{x.LivelloA}";
                note.Add($"{x.DipendenteNome}: {label}");
            }
            foreach (var d in dipClin.Where(d => d.DataTrasformazioneContratto.HasValue
                && d.DataTrasformazioneContratto.Value >= inizio && d.DataTrasformazioneContratto.Value < fine))
            {
                note.Add($"{d.NomeCompleto}: trasformazione contratto");
            }
            foreach (var x in cambiMansioneMese.Where(x => x.ClinicaId == c.Id))
            {
                var parts = new List<string>();
                if (x.RuoloDa != x.RuoloA) parts.Add($"ruolo {x.RuoloDa}→{x.RuoloA}");
                if (!string.IsNullOrEmpty(x.MansioneSpecificaA) && x.MansioneSpecificaA != x.MansioneSpecificaDa)
                    parts.Add($"mansione {(x.MansioneSpecificaDa ?? "—")}→{x.MansioneSpecificaA}");
                if (!string.IsNullOrEmpty(x.RepartoA) && x.RepartoA != x.RepartoDa)
                    parts.Add($"reparto {(x.RepartoDa ?? "—")}→{x.RepartoA}");
                var detail = parts.Count > 0 ? string.Join(", ", parts) : "cambio mansione";
                note.Add($"{x.DipendenteNome}: {detail}");
            }
            foreach (var t in trasferimenti.Where(t => t.ClinicaAId == c.Id
                && !string.IsNullOrEmpty(t.ClinicaDaId) && t.ClinicaDaId != c.Id))
            {
                note.Add($"{t.PersonaNome}: trasferimento da {t.ClinicaDaNome}");
            }

            righe.Add(new MovimentoMensileRiga
            {
                ClinicaId = c.Id,
                ClinicaNome = c.Nome,
                NumeroAssunzioni = assunzioni,
                NumeroAnnullamentiAssunzione = 0,
                NumeroCessazioniAnticipate = cessazioniAnticipate,
                NumeroContrattiNonRinnovati = nonRinnovati,
                NumeroContrattiNonRinnovatiProssimoMese = nonRinnovatiProssimo,
                NumeroProroghe = proroghe,
                NumeroDistacchi = nuoviDistacchi,
                NumeroRettificheDistacchi = rettificheDistacchi,
                NumeroTrasformazioniLivello = trasformazioniLivelloTotale,
                NumeroTrasferimentiSede = trasferimentiSede,
                NumeroCambiMansione = cambiMansioneClinica,
                Note = note
            });
        }

        return new MovimentiMensiliReport
        {
            Anno = anno,
            Mese = mese,
            Righe = righe
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  DISTACCHI: storicizzato per dipendente
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/distacchi/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovoDistacco(string id, string clinicaDistaccoId, DateTime dataInizio, DateTime? dataFine, string? motivo)
    {
        if (string.IsNullOrEmpty(clinicaDistaccoId))
        {
            TempData["flash"] = "Seleziona la clinica di destinazione.";
            return RedirectToAction(nameof(Details), new { id, tab = "distacchi" });
        }
        await _mongo.Distacchi.InsertOneAsync(new DistaccoDipendente
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            ClinicaDistaccoId = clinicaDistaccoId,
            DataInizio = DateTime.SpecifyKind(dataInizio.Date, DateTimeKind.Utc),
            DataFine = dataFine.HasValue ? DateTime.SpecifyKind(dataFine.Value.Date, DateTimeKind.Utc) : null,
            Motivo = motivo
        });
        TempData["flash"] = "Distacco registrato.";
        return RedirectToAction(nameof(Details), new { id, tab = "distacchi" });
    }

    [HttpPost("{id}/distacchi/{distaccoId}/chiudi")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChiudiDistacco(string id, string distaccoId, DateTime dataFine)
    {
        await _mongo.Distacchi.UpdateOneAsync(
            d => d.Id == distaccoId && d.TenantId == _tenant.TenantId && d.DipendenteId == id,
            Builders<DistaccoDipendente>.Update
                .Set(d => d.DataFine, DateTime.SpecifyKind(dataFine.Date, DateTimeKind.Utc))
                .Set(d => d.UpdatedAt, DateTime.UtcNow));
        TempData["flash"] = "Distacco chiuso.";
        return RedirectToAction(nameof(Details), new { id, tab = "distacchi" });
    }

    [HttpPost("{id}/distacchi/{distaccoId}/elimina")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaDistacco(string id, string distaccoId)
    {
        await _mongo.Distacchi.DeleteOneAsync(d => d.Id == distaccoId && d.TenantId == _tenant.TenantId && d.DipendenteId == id);
        TempData["flash"] = "Distacco rimosso.";
        return RedirectToAction(nameof(Details), new { id, tab = "distacchi" });
    }

    // ─────────────────────────────────────────────────────────────
    //  CAMBI LIVELLO / RETRIBUZIONE — storicizzato per dipendente
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/cambio-livello/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovoCambioLivello(string id, DateTime dataEffetto, TipoCambioLivello tipo,
        string? livelloA, decimal? retribuzioneA, string? motivo, string? note)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        if (string.IsNullOrWhiteSpace(livelloA) && !retribuzioneA.HasValue)
        {
            TempData["flash"] = "Indica almeno il nuovo livello o la nuova retribuzione.";
            return RedirectToAction(nameof(Details), new { id, tab = "carriera" });
        }

        var cambio = new CambioLivelloRetribuzione
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            DipendenteNome = dip.NomeCompleto,
            ClinicaId = dip.ClinicaId,
            DataEffetto = DateTime.SpecifyKind(dataEffetto.Date, DateTimeKind.Utc),
            Tipo = tipo,
            LivelloDa = dip.LivelloContratto,
            LivelloA = string.IsNullOrWhiteSpace(livelloA) ? dip.LivelloContratto : livelloA.Trim(),
            RetribuzioneA = retribuzioneA,
            Motivo = motivo,
            Note = note,
            DecisoDaUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            DecisoDaNome = User.Identity?.Name ?? "system"
        };
        await _mongo.CambiLivello.InsertOneAsync(cambio);

        // Aggiorna i campi corrispondenti sull'anagrafica per restare in sync con il report mensile
        var update = Builders<Dipendente>.Update
            .Set(d => d.UpdatedAt, DateTime.UtcNow)
            .Set(d => d.DataAumentoLivelli, cambio.DataEffetto);
        if (!string.IsNullOrWhiteSpace(livelloA))
            update = update.Set(d => d.LivelloContratto, livelloA.Trim());
        await _mongo.Dipendenti.UpdateOneAsync(
            d => d.Id == id && d.TenantId == _tenant.TenantId, update);

        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated,
            new[] { new FieldChange { Field = "LivelloContratto",
                                       OldValue = cambio.LivelloDa ?? "—",
                                       NewValue = cambio.LivelloA ?? "—" } },
            note: motivo, actor: User);
        TempData["flash"] = "Cambio livello/retribuzione registrato.";
        return RedirectToAction(nameof(Details), new { id, tab = "carriera" });
    }

    [HttpPost("{id}/cambio-livello/{cambioId}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaCambioLivello(string id, string cambioId)
    {
        await _mongo.CambiLivello.DeleteOneAsync(x => x.Id == cambioId && x.TenantId == _tenant.TenantId && x.DipendenteId == id);
        TempData["flash"] = "Cambio livello rimosso.";
        return RedirectToAction(nameof(Details), new { id, tab = "carriera" });
    }

    // ─────────────────────────────────────────────────────────────
    //  CAMBI MANSIONE / REPARTO — storicizzato per dipendente
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/cambio-mansione/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovoCambioMansione(string id, DateTime dataEffetto,
        RuoloDipendente ruoloA, string? mansioneSpecificaA, string? repartoA, string? motivo, string? note)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        var cambio = new CambioMansioneReparto
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            DipendenteNome = dip.NomeCompleto,
            ClinicaId = dip.ClinicaId,
            DataEffetto = DateTime.SpecifyKind(dataEffetto.Date, DateTimeKind.Utc),
            RuoloDa = dip.Ruolo,
            RuoloA = ruoloA,
            MansioneSpecificaDa = dip.MansioneSpecifica,
            MansioneSpecificaA = string.IsNullOrWhiteSpace(mansioneSpecificaA) ? dip.MansioneSpecifica : mansioneSpecificaA.Trim(),
            RepartoDa = dip.Reparto,
            RepartoA = string.IsNullOrWhiteSpace(repartoA) ? dip.Reparto : repartoA.Trim(),
            Motivo = motivo,
            Note = note,
            DecisoDaUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            DecisoDaNome = User.Identity?.Name ?? "system"
        };

        if (cambio.RuoloDa == cambio.RuoloA
            && cambio.MansioneSpecificaDa == cambio.MansioneSpecificaA
            && cambio.RepartoDa == cambio.RepartoA)
        {
            TempData["flash"] = "Nessun cambiamento rispetto ai dati attuali.";
            return RedirectToAction(nameof(Details), new { id, tab = "carriera" });
        }

        await _mongo.CambiMansione.InsertOneAsync(cambio);

        var update = Builders<Dipendente>.Update
            .Set(d => d.UpdatedAt, DateTime.UtcNow)
            .Set(d => d.Ruolo, ruoloA);
        if (!string.IsNullOrWhiteSpace(mansioneSpecificaA))
            update = update.Set(d => d.MansioneSpecifica, mansioneSpecificaA.Trim());
        if (!string.IsNullOrWhiteSpace(repartoA))
            update = update.Set(d => d.Reparto, repartoA.Trim());
        await _mongo.Dipendenti.UpdateOneAsync(
            d => d.Id == id && d.TenantId == _tenant.TenantId, update);

        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated,
            new[] { new FieldChange { Field = "Ruolo",
                                       OldValue = cambio.RuoloDa?.ToString() ?? "—",
                                       NewValue = cambio.RuoloA?.ToString() ?? "—" } },
            note: motivo, actor: User);
        TempData["flash"] = "Cambio mansione/reparto registrato.";
        return RedirectToAction(nameof(Details), new { id, tab = "carriera" });
    }

    [HttpPost("{id}/cambio-mansione/{cambioId}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaCambioMansione(string id, string cambioId)
    {
        await _mongo.CambiMansione.DeleteOneAsync(x => x.Id == cambioId && x.TenantId == _tenant.TenantId && x.DipendenteId == id);
        TempData["flash"] = "Cambio mansione rimosso.";
        return RedirectToAction(nameof(Details), new { id, tab = "carriera" });
    }

    // ─────────────────────────────────────────────────────────────
    //  COMPLIANCE: registra visite mediche e corsi dal profilo
    //  (anziché passare per il modulo RLS)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/visita-medica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> RegistraVisitaMedica(string id, DateTime data, EsitoVisita esito,
        DateTime? scadenza, int? mesiPeriodicita, string? note, IFormFile? allegato)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        var periodicita = mesiPeriodicita ?? VisitaMedica.PeriodicitaDefault(dip.Ruolo);
        var scadenzaCalc = scadenza ?? data.AddMonths(periodicita);

        var visita = new VisitaMedica
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            Data = DateTime.SpecifyKind(data.Date, DateTimeKind.Utc),
            Esito = esito,
            ScadenzaIdoneita = DateTime.SpecifyKind(scadenzaCalc.Date, DateTimeKind.Utc),
            MesiPeriodicita = periodicita,
            Note = note
        };

        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "visite",
                (path, name, size) => { visita.AllegatoNome = name; visita.AllegatoPath = path; visita.AllegatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "compliance" });
            }
        }

        await _mongo.VisiteMediche.InsertOneAsync(visita);
        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = "Visita medica registrata.";
        return RedirectToAction(nameof(Details), new { id, tab = "compliance" });
    }

    [HttpPost("{id}/corso")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> RegistraCorso(string id, TipoCorso tipo, DateTime dataConseguimento,
        DateTime? scadenza, string? verbaleNomina, string? note, IFormFile? allegato)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        var corso = new Corso
        {
            TenantId = _tenant.TenantId!,
            DestinatarioId = id,
            DestinatarioTipo = DestinatarioCorso.Dipendente,
            Tipo = tipo,
            DataConseguimento = DateTime.SpecifyKind(dataConseguimento.Date, DateTimeKind.Utc),
            Scadenza = scadenza.HasValue ? DateTime.SpecifyKind(scadenza.Value.Date, DateTimeKind.Utc) : null,
            VerbaleNomina = verbaleNomina,
            Note = note
        };

        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "corsi",
                (path, name, size) => { corso.AttestatoNome = name; corso.AttestatoPath = path; corso.AttestatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "compliance" });
            }
        }

        await _mongo.Corsi.InsertOneAsync(corso);
        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = $"Corso «{tipo}» registrato.";
        return RedirectToAction(nameof(Details), new { id, tab = "compliance" });
    }

    // ─────────────────────────────────────────────────────────────
    //  DOCUMENTI fascicolo dipendente (frontespizio ASO)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/documenti/upsert")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> UpsertDocumento(string id, TipoDocumentoDipendente tipo,
        string? etichettaLibera, DateTime? dataAcquisizione, DateTime? scadenza, string? note, IFormFile? allegato)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        var existing = await _mongo.DocumentiDipendente
            .Find(x => x.TenantId == _tenant.TenantId && x.DipendenteId == id && x.Tipo == tipo
                       && (tipo != TipoDocumentoDipendente.Altro || x.EtichettaLibera == etichettaLibera))
            .FirstOrDefaultAsync();

        var doc = existing ?? new DocumentoDipendente
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            Tipo = tipo,
            EtichettaLibera = etichettaLibera
        };
        doc.DataAcquisizione = dataAcquisizione.HasValue
            ? DateTime.SpecifyKind(dataAcquisizione.Value.Date, DateTimeKind.Utc)
            : DateTime.UtcNow.Date;
        doc.Scadenza = scadenza.HasValue ? DateTime.SpecifyKind(scadenza.Value.Date, DateTimeKind.Utc) : null;
        doc.Note = note;
        doc.UpdatedAt = DateTime.UtcNow;

        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "documenti-dipendente",
                (path, name, size) => { doc.AllegatoNome = name; doc.AllegatoPath = path; doc.AllegatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "documenti" });
            }
        }

        if (existing is null)
        {
            await _mongo.DocumentiDipendente.InsertOneAsync(doc);
        }
        else
        {
            await _mongo.DocumentiDipendente.ReplaceOneAsync(x => x.Id == doc.Id, doc);
        }

        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = "Documento aggiornato.";
        return RedirectToAction(nameof(Details), new { id, tab = "documenti" });
    }

    [HttpPost("{id}/documenti/{docId}/elimina")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaDocumento(string id, string docId)
    {
        await _mongo.DocumentiDipendente.DeleteOneAsync(x => x.Id == docId && x.TenantId == _tenant.TenantId && x.DipendenteId == id);
        TempData["flash"] = "Documento rimosso.";
        return RedirectToAction(nameof(Details), new { id, tab = "documenti" });
    }

    // ─────────────────────────────────────────────────────────────
    //  PROCEDIMENTI DISCIPLINARI (4 step)
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/disciplinari/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovoDisciplinare(string id, string oggetto, DateTime dataApertura, string? note)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        if (string.IsNullOrWhiteSpace(oggetto))
        {
            TempData["flash"] = "Indica l'oggetto del procedimento.";
            return RedirectToAction(nameof(Details), new { id, tab = "disciplinari" });
        }

        var p = new ProcedimentoDisciplinare
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            DipendenteNome = dip.NomeCompleto,
            Oggetto = oggetto.Trim(),
            DataApertura = DateTime.SpecifyKind(dataApertura.Date, DateTimeKind.Utc),
            Note = note,
            Stato = StatoProcedimento.Aperto
        };
        await _mongo.Disciplinari.InsertOneAsync(p);
        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = "Procedimento disciplinare aperto.";
        return RedirectToAction(nameof(Details), new { id, tab = "disciplinari" });
    }

    [HttpPost("{id}/disciplinari/{procId}/step")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> AggiornaStepDisciplinare(string id, string procId, int step,
        DateTime data, string? note, EsitoProcedimento? esito, IFormFile? allegato)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        var proc = await _mongo.Disciplinari.Find(x => x.Id == procId && x.TenantId == _tenant.TenantId && x.DipendenteId == id).FirstOrDefaultAsync();
        if (proc is null) return NotFound();

        if (step is < 1 or > 4) return BadRequest();

        string? allegatoPath = null, allegatoNome = null; long? allegatoSize = null;
        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "disciplinari",
                (path, name, size) => { allegatoPath = path; allegatoNome = name; allegatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "disciplinari" });
            }
        }

        var dt = DateTime.SpecifyKind(data.Date, DateTimeKind.Utc);
        var update = Builders<ProcedimentoDisciplinare>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow);
        switch (step)
        {
            case 1:
                update = update.Set(p => p.Step1LetteraData, dt).Set(p => p.Step1Note, note);
                if (allegatoPath != null) update = update.Set(p => p.Step1AllegatoPath, allegatoPath).Set(p => p.Step1AllegatoNome, allegatoNome).Set(p => p.Step1AllegatoSize, allegatoSize);
                update = update.Set(p => p.Stato, StatoProcedimento.InCorso);
                break;
            case 2:
                update = update.Set(p => p.Step2FirmataData, dt).Set(p => p.Step2Note, note);
                if (allegatoPath != null) update = update.Set(p => p.Step2AllegatoPath, allegatoPath).Set(p => p.Step2AllegatoNome, allegatoNome).Set(p => p.Step2AllegatoSize, allegatoSize);
                break;
            case 3:
                update = update.Set(p => p.Step3RispostaData, dt).Set(p => p.Step3Note, note);
                if (allegatoPath != null) update = update.Set(p => p.Step3AllegatoPath, allegatoPath).Set(p => p.Step3AllegatoNome, allegatoNome).Set(p => p.Step3AllegatoSize, allegatoSize);
                break;
            case 4:
                update = update.Set(p => p.Step4ConclusioneData, dt).Set(p => p.Step4Note, note).Set(p => p.Step4Esito, esito).Set(p => p.Stato, StatoProcedimento.Concluso);
                if (allegatoPath != null) update = update.Set(p => p.Step4AllegatoPath, allegatoPath).Set(p => p.Step4AllegatoNome, allegatoNome).Set(p => p.Step4AllegatoSize, allegatoSize);
                break;
        }

        await _mongo.Disciplinari.UpdateOneAsync(x => x.Id == procId, update);
        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = $"Step {step} registrato.";
        return RedirectToAction(nameof(Details), new { id, tab = "disciplinari" });
    }

    [HttpPost("{id}/disciplinari/{procId}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaDisciplinare(string id, string procId)
    {
        await _mongo.Disciplinari.DeleteOneAsync(x => x.Id == procId && x.TenantId == _tenant.TenantId && x.DipendenteId == id);
        TempData["flash"] = "Procedimento eliminato.";
        return RedirectToAction(nameof(Details), new { id, tab = "disciplinari" });
    }

    // ─────────────────────────────────────────────────────────────
    //  PREMI / BONUS / WELFARE
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/premi/nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> NuovoPremio(string id, TipoPremio tipo, string descrizione,
        DateTime data, decimal? importo, string? motivazione, string? note, IFormFile? allegato)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        if (string.IsNullOrWhiteSpace(descrizione))
        {
            TempData["flash"] = "Indica una descrizione.";
            return RedirectToAction(nameof(Details), new { id, tab = "premi" });
        }

        var premio = new PremioDipendente
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            DipendenteNome = dip.NomeCompleto,
            Tipo = tipo,
            Descrizione = descrizione.Trim(),
            Data = DateTime.SpecifyKind(data.Date, DateTimeKind.Utc),
            Importo = importo,
            Motivazione = motivazione,
            Note = note
        };

        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "premi",
                (path, name, size) => { premio.AllegatoNome = name; premio.AllegatoPath = path; premio.AllegatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "premi" });
            }
        }

        await _mongo.Premi.InsertOneAsync(premio);
        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = "Premio registrato.";
        return RedirectToAction(nameof(Details), new { id, tab = "premi" });
    }

    [HttpPost("{id}/premi/{premioId}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaPremio(string id, string premioId)
    {
        await _mongo.Premi.DeleteOneAsync(x => x.Id == premioId && x.TenantId == _tenant.TenantId && x.DipendenteId == id);
        TempData["flash"] = "Premio rimosso.";
        return RedirectToAction(nameof(Details), new { id, tab = "premi" });
    }

    // ─────────────────────────────────────────────────────────────
    //  SCHEDE DI VALUTAZIONE
    // ─────────────────────────────────────────────────────────────
    [HttpPost("{id}/valutazioni/nuova")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAllegatoBytes)]
    public async Task<IActionResult> NuovaValutazione(string id, string periodo, DateTime data,
        string? valutatoreNome, int? competenze, int? comportamento, int? teamwork, int? puntualita,
        int? obiettivi, int? finale, DateTime? dataColloquio, bool firmaDipendente, bool firmaValutatore,
        string? obiettiviTesto, string? puntiForza, string? areeMiglioramento, string? commenti,
        StatoSchedaValutazione stato, IFormFile? allegato)
    {
        var dip = await Load(id);
        if (dip is null) return NotFound();

        if (string.IsNullOrWhiteSpace(periodo))
        {
            TempData["flash"] = "Indica il periodo di riferimento (es. 2026 H1).";
            return RedirectToAction(nameof(Details), new { id, tab = "valutazioni" });
        }

        var sch = new SchedaValutazione
        {
            TenantId = _tenant.TenantId!,
            DipendenteId = id,
            DipendenteNome = dip.NomeCompleto,
            Periodo = periodo.Trim(),
            Data = DateTime.SpecifyKind(data.Date, DateTimeKind.Utc),
            ValutatoreNome = valutatoreNome ?? User.Identity?.Name,
            PunteggioCompetenze = competenze,
            PunteggioComportamento = comportamento,
            PunteggioTeamwork = teamwork,
            PunteggioPuntualita = puntualita,
            PunteggioObiettivi = obiettivi,
            PunteggioFinale = finale,
            DataColloquio = dataColloquio.HasValue ? DateTime.SpecifyKind(dataColloquio.Value.Date, DateTimeKind.Utc) : null,
            FirmaDipendente = firmaDipendente,
            FirmaValutatore = firmaValutatore,
            Obiettivi = obiettiviTesto,
            PuntiDiForza = puntiForza,
            AreeDiMiglioramento = areeMiglioramento,
            Commenti = commenti,
            Stato = stato
        };

        if (allegato is { Length: > 0 })
        {
            var err = await TryAttachAsync(allegato, "valutazioni",
                (path, name, size) => { sch.AllegatoNome = name; sch.AllegatoPath = path; sch.AllegatoSize = size; });
            if (err is not null)
            {
                TempData["flash"] = err;
                return RedirectToAction(nameof(Details), new { id, tab = "valutazioni" });
            }
        }

        await _mongo.Valutazioni.InsertOneAsync(sch);
        await _audit.LogAsync("Dipendente", id, dip.NomeCompleto, AuditAction.Updated, actor: User);
        TempData["flash"] = "Scheda di valutazione registrata.";
        return RedirectToAction(nameof(Details), new { id, tab = "valutazioni" });
    }

    [HttpPost("{id}/valutazioni/{schedaId}/elimina")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaValutazione(string id, string schedaId)
    {
        await _mongo.Valutazioni.DeleteOneAsync(x => x.Id == schedaId && x.TenantId == _tenant.TenantId && x.DipendenteId == id);
        TempData["flash"] = "Scheda di valutazione rimossa.";
        return RedirectToAction(nameof(Details), new { id, tab = "valutazioni" });
    }

    private async Task<string?> TryAttachAsync(IFormFile file, string folder, Action<string, string, long> apply)
    {
        if (file.Length > MaxAllegatoBytes) return $"File troppo grande (max {MaxAllegatoBytes / (1024 * 1024)}MB).";
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllegatoEstensioniAmmesse.Contains(ext)) return $"Estensione non consentita: {ext}";

        await using var stream = file.OpenReadStream();
        var stored = await _storage.SaveAsync(_tenant.TenantId!, folder, file.FileName, stream, file.ContentType);
        apply(stored.RelativePath, file.FileName, stored.SizeBytes);
        return null;
    }

    [HttpGet("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Create()
    {
        ViewData["Section"] = "dipendenti";
        ViewData["IsNew"] = true;
        ViewData["Cliniche"] = await CliniceListAsync();
        ViewData["Managers"] = await ManagerCandidatesAsync();
        return View("Form", new Dipendente { DataAssunzione = DateTime.Today });
    }

    [HttpPost("nuovo")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Dipendente model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "dipendenti";
            ViewData["IsNew"] = true;
            ViewData["Cliniche"] = await CliniceListAsync();
            ViewData["Managers"] = await ManagerCandidatesAsync();
            return View("Form", model);
        }
        model.TenantId = _tenant.TenantId!;
        model.CreatedAt = DateTime.UtcNow;
        await _mongo.Dipendenti.InsertOneAsync(model);

        await _audit.LogAsync("Dipendente", model.Id, model.NomeCompleto, AuditAction.Created, actor: User);

        if (!string.IsNullOrEmpty(model.ClinicaId))
        {
            var cliniche = await CliniceListAsync();
            var clinica = cliniche.FirstOrDefault(c => c.Id == model.ClinicaId);
            await CreateTransferAsync(model.Id, TipoPersona.Dipendente, model.NomeCompleto,
                null, null, clinica?.Id ?? "", clinica?.Nome ?? "",
                MotivoTrasferimento.Riorganizzazione, "Assegnazione iniziale", model.DataAssunzione);
        }

        TempData["flash"] = $"Dipendente «{model.NomeCompleto}» creato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> Edit(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dipendenti";
        ViewData["IsNew"] = false;
        ViewData["Cliniche"] = await CliniceListAsync();
        ViewData["Managers"] = await ManagerCandidatesAsync(excludeId: id);
        return View("Form", d);
    }

    /// <summary>Restituisce il partial della modale di modifica rapida con dati e criticità.</summary>
    [HttpGet("{id}/edit-modal")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    public async Task<IActionResult> EditModal(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();

        var cliniche = await CliniceListAsync();
        var managers = await ManagerCandidatesAsync(excludeId: id);
        var visite = await _mongo.VisiteMediche
            .Find(v => v.TenantId == _tenant.TenantId && v.DipendenteId == id)
            .ToListAsync();
        var (critiche, avvisi, completezza) = Chipdent.Web.Infrastructure.Compliance
            .DipendenteCriticitaCalculator.Calcola(d, visite, DateTime.UtcNow);

        return PartialView("_EditModal", new DipendenteEditModalViewModel
        {
            Dipendente = d,
            Cliniche = cliniche,
            Managers = managers,
            Critiche = critiche,
            Avvisi = avvisi,
            Completezza = completezza
        });
    }

    [HttpPost("{id}/modifica")]
    [Authorize(Policy = Policies.RequireBackoffice)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Dipendente model, [FromHeader(Name = "X-Edit-Modal")] string? modal)
    {
        if (id != model.Id) return BadRequest();
        var isModal = modal == "1";
        if (!ModelState.IsValid)
        {
            if (isModal)
            {
                var errors = ModelState
                    .Where(e => e.Value!.Errors.Count > 0)
                    .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                return BadRequest(new { errors });
            }
            ViewData["Section"] = "dipendenti";
            ViewData["IsNew"] = false;
            ViewData["Cliniche"] = await CliniceListAsync();
            ViewData["Managers"] = await ManagerCandidatesAsync(excludeId: id);
            return View("Form", model);
        }
        var existing = await Load(id);
        if (existing is null) return NotFound();
        model.TenantId = existing.TenantId;
        model.CreatedAt = existing.CreatedAt;
        model.UpdatedAt = DateTime.UtcNow;
        await _mongo.Dipendenti.ReplaceOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId, model);

        await _audit.LogDiffAsync(existing, model, "Dipendente", model.NomeCompleto,
            AuditAction.Updated, User, ignoreFields: nameof(Dipendente.UpdatedAt));

        if (existing.ClinicaId != model.ClinicaId)
        {
            var cliniche = await CliniceListAsync();
            var fromC = cliniche.FirstOrDefault(c => c.Id == existing.ClinicaId);
            var toC = cliniche.FirstOrDefault(c => c.Id == model.ClinicaId);
            await CreateTransferAsync(model.Id, TipoPersona.Dipendente, model.NomeCompleto,
                fromC?.Id, fromC?.Nome, toC?.Id ?? "", toC?.Nome ?? "",
                MotivoTrasferimento.Riorganizzazione, "Aggiornamento sede", DateTime.Today);
        }

        if (isModal) return Json(new { ok = true, name = model.NomeCompleto });

        TempData["flash"] = $"Dipendente «{model.NomeCompleto}» aggiornato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/elimina")]
    [Authorize(Policy = Policies.RequireManagement)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var existing = await Load(id);
        if (existing is null) return NotFound();
        await _mongo.Dipendenti.DeleteOneAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);
        await _audit.LogAsync("Dipendente", id, existing.NomeCompleto, AuditAction.Deleted, actor: User);
        TempData["flash"] = "Dipendente eliminato.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/trasferisci")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Transfer(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        var cliniche = await CliniceListAsync();
        var current = cliniche.FirstOrDefault(c => c.Id == d.ClinicaId);
        ViewData["Section"] = "dipendenti";
        return View("Transfer", new TransferViewModel
        {
            PersonaId = d.Id,
            PersonaTipo = TipoPersona.Dipendente,
            PersonaNome = d.NomeCompleto,
            ClinicaAttualeId = current?.Id,
            ClinicaAttualeNome = current?.Nome,
            Cliniche = cliniche
        });
    }

    [HttpPost("{id}/trasferisci")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(string id, TransferViewModel vm)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        if (!ModelState.IsValid)
        {
            vm.PersonaNome = d.NomeCompleto;
            vm.Cliniche = await CliniceListAsync();
            ViewData["Section"] = "dipendenti";
            return View("Transfer", vm);
        }
        if (vm.ClinicaAId == d.ClinicaId)
        {
            ModelState.AddModelError(nameof(vm.ClinicaAId), "Il dipendente è già assegnato a questa sede.");
            vm.PersonaNome = d.NomeCompleto;
            vm.Cliniche = await CliniceListAsync();
            ViewData["Section"] = "dipendenti";
            return View("Transfer", vm);
        }

        var cliniche = await CliniceListAsync();
        var fromC = cliniche.FirstOrDefault(c => c.Id == d.ClinicaId);
        var toC = cliniche.FirstOrDefault(c => c.Id == vm.ClinicaAId);

        await _mongo.Dipendenti.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dipendente>.Update
                .Set(x => x.ClinicaId, vm.ClinicaAId)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await CreateTransferAsync(id, TipoPersona.Dipendente, d.NomeCompleto,
            fromC?.Id, fromC?.Nome, toC?.Id ?? "", toC?.Nome ?? "",
            vm.Motivo, vm.Note, vm.DataEffetto);

        await _audit.LogAsync("Dipendente", id, d.NomeCompleto, AuditAction.Transferred,
            new[] { new FieldChange { Field = "ClinicaId",
                                       OldValue = fromC?.Nome ?? "—",
                                       NewValue = toC?.Nome ?? "—" } },
            note: vm.Note, actor: User);

        TempData["flash"] = $"{d.NomeCompleto} trasferito a {toC?.Nome}.";
        return RedirectToAction(nameof(Details), new { id, tab = "storico" });
    }

    [HttpGet("{id}/dimetti")]
    [Authorize(Policy = Policies.RequireDirettore)]
    public async Task<IActionResult> Dismiss(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        ViewData["Section"] = "dipendenti";
        return View("Dismiss", new DismissViewModel
        {
            PersonaId = d.Id,
            PersonaTipo = TipoPersona.Dipendente,
            PersonaNome = d.NomeCompleto
        });
    }

    [HttpPost("{id}/dimetti")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(string id, DismissViewModel vm)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        if (!ModelState.IsValid)
        {
            vm.PersonaNome = d.NomeCompleto;
            ViewData["Section"] = "dipendenti";
            return View("Dismiss", vm);
        }
        await _mongo.Dipendenti.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dipendente>.Update
                .Set(x => x.Stato, StatoDipendente.Cessato)
                .Set(x => x.DataDimissioni, vm.DataDimissioni)
                .Set(x => x.MotivoDimissioni, vm.Motivo)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));

        await _audit.LogAsync("Dipendente", id, d.NomeCompleto, AuditAction.Dismissed,
            new[] { new FieldChange { Field = "DataDimissioni", OldValue = "—", NewValue = vm.DataDimissioni.ToString("yyyy-MM-dd") } },
            note: vm.Motivo, actor: User);

        TempData["flash"] = $"{d.NomeCompleto} dimesso il {vm.DataDimissioni:dd/MM/yyyy}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/riattiva")]
    [Authorize(Policy = Policies.RequireDirettore)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(string id)
    {
        var d = await Load(id);
        if (d is null) return NotFound();
        await _mongo.Dipendenti.UpdateOneAsync(
            x => x.Id == id && x.TenantId == _tenant.TenantId,
            Builders<Dipendente>.Update
                .Set(x => x.Stato, StatoDipendente.Attivo)
                .Set(x => x.DataDimissioni, (DateTime?)null)
                .Set(x => x.MotivoDimissioni, (string?)null)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
        await _audit.LogAsync("Dipendente", id, d.NomeCompleto, AuditAction.Reactivated, actor: User);
        TempData["flash"] = $"{d.NomeCompleto} riattivato.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task CreateTransferAsync(string personaId, TipoPersona tipo, string personaNome,
                                           string? fromId, string? fromName,
                                           string toId, string toName,
                                           MotivoTrasferimento motivo, string? note, DateTime data)
    {
        var trasferimento = new Trasferimento
        {
            TenantId = _tenant.TenantId!,
            PersonaId = personaId,
            PersonaTipo = tipo,
            PersonaNome = personaNome,
            ClinicaDaId = fromId,
            ClinicaDaNome = fromName,
            ClinicaAId = toId,
            ClinicaANome = toName,
            DataEffetto = data,
            Motivo = motivo,
            Note = note,
            DecisoDaUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            DecisoDaNome = User.Identity?.Name ?? "system"
        };
        await _mongo.Trasferimenti.InsertOneAsync(trasferimento);
    }

    private async Task<Dipendente?> Load(string id)
        => await _mongo.Dipendenti
            .Find(d => d.Id == id && d.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();

    private Task<List<Clinica>> CliniceListAsync()
        => _mongo.Cliniche.Find(c => c.TenantId == _tenant.TenantId).SortBy(c => c.Nome).ToListAsync();

    private async Task<Dictionary<string, string>> CliniceLookupAsync()
        => (await CliniceListAsync()).ToDictionary(c => c.Id, c => c.Nome);

    private async Task<List<Dipendente>> ManagerCandidatesAsync(string? excludeId = null)
        => await _mongo.Dipendenti
            .Find(d => d.TenantId == _tenant.TenantId
                       && d.Stato != StatoDipendente.Cessato
                       && (excludeId == null || d.Id != excludeId))
            .SortBy(d => d.Cognome).ToListAsync();
}
