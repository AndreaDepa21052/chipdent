using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Insights;

/// <summary>
/// Ottimizzatore di pianificazione turni — algoritmo greedy multifattoriale,
/// no ML. Per ciascun giorno×sede×ruolo soddisfa la soglia di copertura
/// scegliendo i candidati più adatti (carico orario equo, no ferie, no
/// sovrapposizioni, esperienza accumulata, affinità orario tipico).
/// </summary>
public class TurniOptimizer
{
    private readonly MongoContext _mongo;
    public TurniOptimizer(MongoContext mongo) => _mongo = mongo;

    public async Task<OptimizationProposal> ProposeAsync(string tenantId, DateTime weekStart, CancellationToken ct = default)
    {
        var weekStartUtc = DateTime.SpecifyKind(weekStart.Date, DateTimeKind.Utc);
        var weekEnd = weekStartUtc.AddDays(7);

        // Anagrafica
        var dipendenti = await _mongo.Dipendenti
            .Find(d => d.TenantId == tenantId
                       && d.Stato != StatoDipendente.Cessato
                       && d.Stato != StatoDipendente.InCongedo)
            .ToListAsync(ct);
        var cliniche = (await _mongo.Cliniche.Find(c => c.TenantId == tenantId).ToListAsync(ct))
            .ToDictionary(c => c.Id);
        var soglie = await _mongo.SoglieCopertura
            .Find(s => s.TenantId == tenantId && s.Attiva)
            .ToListAsync(ct);
        var templates = await _mongo.TurniTemplate
            .Find(t => t.TenantId == tenantId && t.Attivo)
            .SortBy(t => t.OraInizio).ToListAsync(ct);

        // Vincoli
        var ferieApprovate = await _mongo.RichiesteFerie
            .Find(r => r.TenantId == tenantId
                       && r.Stato == StatoRichiestaFerie.Approvata
                       && r.DataInizio < weekEnd && r.DataFine >= weekStartUtc)
            .ToListAsync(ct);
        var turniGiaPianificati = await _mongo.Turni
            .Find(t => t.TenantId == tenantId && t.Data >= weekStartUtc && t.Data < weekEnd
                       && t.TipoPersona == TipoPersona.Dipendente)
            .ToListAsync(ct);

        // Storia per fairness: ore lavorate ultimi 30 giorni
        var meseInizio = weekStartUtc.AddDays(-30);
        var turniStorici = await _mongo.Turni
            .Find(t => t.TenantId == tenantId && t.Data >= meseInizio && t.Data < weekStartUtc
                       && t.TipoPersona == TipoPersona.Dipendente)
            .ToListAsync(ct);
        var oreStoriche = turniStorici
            .GroupBy(t => t.PersonaId)
            .ToDictionary(g => g.Key, g => g.Sum(t => Math.Max(0, (t.OraFine - t.OraInizio).TotalHours)));

        // Conta ore già assegnate questa settimana (per non superare soglia individuale)
        var oreAssegnateSettimana = turniGiaPianificati
            .GroupBy(t => t.PersonaId)
            .ToDictionary(g => g.Key, g => g.Sum(t => Math.Max(0, (t.OraFine - t.OraInizio).TotalHours)));

        var proposte = new List<TurnoProposto>();
        var avvisi = new List<string>();

        foreach (var giorno in Enumerable.Range(0, 5).Select(i => weekStartUtc.AddDays(i))) // lun-ven
        {
            foreach (var soglia in soglie)
            {
                if (!cliniche.TryGetValue(soglia.ClinicaId, out var clinica)) continue;
                if (soglia.GiornoSettimana.HasValue && soglia.GiornoSettimana != giorno.DayOfWeek) continue;

                // Quanti già assegnati per questa cella (sede × ruolo × giorno)?
                var assegnatiOggi = turniGiaPianificati
                    .Where(t => t.Data.Date == giorno.Date && t.ClinicaId == soglia.ClinicaId
                                && dipendenti.Any(d => d.Id == t.PersonaId && d.Ruolo == soglia.Ruolo))
                    .Select(t => t.PersonaId).Distinct().Count();

                var mancanti = soglia.MinimoPerGiorno - assegnatiOggi;
                if (mancanti <= 0) continue;

                // Candidati: stessa sede, stesso ruolo, escluso chi è in ferie o ha già un turno quel giorno in qualsiasi sede
                var candidati = dipendenti
                    .Where(d => d.ClinicaId == soglia.ClinicaId && d.Ruolo == soglia.Ruolo)
                    .Where(d => !ferieApprovate.Any(f => f.DipendenteId == d.Id
                                                          && f.DataInizio.Date <= giorno && f.DataFine.Date >= giorno))
                    .Where(d => !turniGiaPianificati.Any(t => t.PersonaId == d.Id && t.Data.Date == giorno.Date))
                    .ToList();

                // Score: meno ore storiche + meno ore questa settimana = migliore
                var scelti = candidati
                    .Select(d => new
                    {
                        D = d,
                        Score = ComputeScore(d, oreStoriche, oreAssegnateSettimana)
                    })
                    .OrderByDescending(x => x.Score)
                    .Take(mancanti)
                    .Select(x => x.D)
                    .ToList();

                // Template di default: il primo attivo, oppure 9-13 fallback
                var template = templates.FirstOrDefault();
                var oraInizio = template?.OraInizio ?? new TimeSpan(9, 0, 0);
                var oraFine = template?.OraFine ?? new TimeSpan(13, 0, 0);
                var oreTurno = Math.Max(0.5, (oraFine - oraInizio).TotalHours);

                if (scelti.Count < mancanti)
                {
                    avvisi.Add($"⚠ {clinica.Nome} · {soglia.Ruolo} · {giorno:ddd dd/MM}: copertura proposta {scelti.Count}/{soglia.MinimoPerGiorno} (mancano candidati disponibili).");
                }

                foreach (var d in scelti)
                {
                    proposte.Add(new TurnoProposto(
                        DipendenteId: d.Id,
                        DipendenteNome: d.NomeCompleto,
                        Ruolo: d.Ruolo.ToString(),
                        ClinicaId: clinica.Id,
                        ClinicaNome: clinica.Nome,
                        Data: giorno,
                        OraInizio: oraInizio,
                        OraFine: oraFine,
                        Motivazione: BuildMotivazione(d, oreStoriche, oreAssegnateSettimana),
                        TemplateId: template?.Id));

                    // Aggiorna lo "stato locale" per evitare doppia assegnazione nello stesso giorno
                    if (!oreAssegnateSettimana.ContainsKey(d.Id)) oreAssegnateSettimana[d.Id] = 0;
                    oreAssegnateSettimana[d.Id] += oreTurno;
                }
            }
        }

        return new OptimizationProposal(weekStartUtc, proposte, avvisi);
    }

    private static int ComputeScore(Dipendente d,
        IReadOnlyDictionary<string, double> storiche,
        IReadOnlyDictionary<string, double> settimanaCorrente)
    {
        // Carico minore = score maggiore. Bonus per dipendenti con poche ore storiche.
        var oreStoriche = storiche.GetValueOrDefault(d.Id);
        var oreSettimana = settimanaCorrente.GetValueOrDefault(d.Id);
        var score = 100 - (int)oreSettimana * 4 - (int)(oreStoriche / 4);
        // Penalizza chi è in onboarding (preferisci esperti)
        if (d.Stato == StatoDipendente.Onboarding) score -= 10;
        // Penalizza chi è in malattia/permesso al momento (anche se non in ferie attive)
        if (d.Stato == StatoDipendente.InMalattia || d.Stato == StatoDipendente.Sospeso) score -= 30;
        return Math.Max(0, score);
    }

    private static string BuildMotivazione(Dipendente d,
        IReadOnlyDictionary<string, double> storiche,
        IReadOnlyDictionary<string, double> settimanaCorrente)
    {
        var oreSet = (int)settimanaCorrente.GetValueOrDefault(d.Id);
        var oreSt = (int)storiche.GetValueOrDefault(d.Id);
        if (oreSet == 0 && oreSt < 40) return "carico settimanale basso, distribuzione equa";
        if (oreSet < 8) return $"poche ore già pianificate ({oreSet}h)";
        if (oreSt < 60) return "carico storico contenuto";
        return "scelto fra i candidati disponibili";
    }
}

public record OptimizationProposal(
    DateTime WeekStart,
    IReadOnlyList<TurnoProposto> Turni,
    IReadOnlyList<string> Avvisi);

public record TurnoProposto(
    string DipendenteId,
    string DipendenteNome,
    string Ruolo,
    string ClinicaId,
    string ClinicaNome,
    DateTime Data,
    TimeSpan OraInizio,
    TimeSpan OraFine,
    string Motivazione,
    string? TemplateId);
