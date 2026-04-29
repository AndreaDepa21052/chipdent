using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Insights;

/// <summary>
/// Predittore di assenze deterministico (no LLM, no ML training): aggrega lo storico timbrature
/// e calcola un score di rischio per i prossimi 7 giorni in base a pattern osservabili:
/// - giorno della settimana storicamente "saltato" dal singolo dipendente
/// - frequenza media di assenze nelle ultime 8 settimane
/// - "ponte" (martedì/giovedì subito dopo/prima di un weekend)
/// - prossimità a un periodo di ferie già richieste
/// Il risultato è un ranking spiegabile, non un'opinione: ogni score è scomponibile nei suoi fattori.
/// </summary>
public class AssenzePredictor
{
    private readonly MongoContext _mongo;

    public AssenzePredictor(MongoContext mongo) => _mongo = mongo;

    public async Task<IReadOnlyList<RischioAssenza>> CalcolaAsync(string tenantId, DateTime daGiorno, int orizzonteGiorni = 7, IReadOnlyList<string>? clinicaScope = null)
    {
        var inizioStorico = daGiorno.AddDays(-56); // 8 settimane

        var dipFilter = Builders<Dipendente>.Filter.Eq(d => d.TenantId, tenantId)
                      & Builders<Dipendente>.Filter.Eq(d => d.Stato, StatoDipendente.Attivo);
        if (clinicaScope is { Count: > 0 })
            dipFilter &= Builders<Dipendente>.Filter.In(d => d.ClinicaId, clinicaScope);
        var dipendenti = await _mongo.Dipendenti.Find(dipFilter).ToListAsync();
        if (dipendenti.Count == 0) return Array.Empty<RischioAssenza>();

        var dipIds = dipendenti.Select(d => d.Id).ToList();

        var turniStorici = await _mongo.Turni
            .Find(t => t.TenantId == tenantId && t.TipoPersona == TipoPersona.Dipendente
                       && dipIds.Contains(t.PersonaId)
                       && t.Data >= inizioStorico && t.Data < daGiorno)
            .ToListAsync();
        var timbrStorici = await _mongo.Timbrature
            .Find(t => t.TenantId == tenantId && dipIds.Contains(t.DipendenteId)
                       && t.Timestamp >= inizioStorico && t.Timestamp < daGiorno)
            .ToListAsync();
        var turniFuturi = await _mongo.Turni
            .Find(t => t.TenantId == tenantId && t.TipoPersona == TipoPersona.Dipendente
                       && dipIds.Contains(t.PersonaId)
                       && t.Data >= daGiorno && t.Data < daGiorno.AddDays(orizzonteGiorni))
            .ToListAsync();
        var ferieFuture = await _mongo.RichiesteFerie
            .Find(f => f.TenantId == tenantId && dipIds.Contains(f.DipendenteId)
                       && f.Stato == StatoRichiestaFerie.Approvata
                       && f.DataFine >= daGiorno)
            .ToListAsync();

        var risk = new List<RischioAssenza>();
        foreach (var dip in dipendenti)
        {
            // Per ogni giorno futuro pianificato, valuta il rischio.
            var miei = turniFuturi.Where(t => t.PersonaId == dip.Id).ToList();
            if (miei.Count == 0) continue;

            // ── Storico personale ──
            var mieiTurniPassati = turniStorici.Where(t => t.PersonaId == dip.Id).ToList();
            var mieiTimbrature = timbrStorici.Where(t => t.DipendenteId == dip.Id).GroupBy(t => t.Timestamp.Date).ToDictionary(g => g.Key, g => g.Count());
            // Conteggio assenze: turni pianificati senza alcuna timbratura quel giorno.
            var assenzeStoriche = mieiTurniPassati.Count(t => !mieiTimbrature.ContainsKey(t.Data.Date));
            var tassoAssenza = mieiTurniPassati.Count == 0 ? 0.0 : (double)assenzeStoriche / mieiTurniPassati.Count;
            // Pattern per giorno settimana (DOW)
            var assenzePerDow = Enumerable.Range(0, 7).ToDictionary(d => d, _ => 0);
            var turniPerDow   = Enumerable.Range(0, 7).ToDictionary(d => d, _ => 0);
            foreach (var t in mieiTurniPassati)
            {
                var dow = (int)t.Data.DayOfWeek;
                turniPerDow[dow]++;
                if (!mieiTimbrature.ContainsKey(t.Data.Date)) assenzePerDow[dow]++;
            }

            foreach (var turno in miei.OrderBy(t => t.Data))
            {
                var fattori = new List<string>();
                double score = 0;

                // Fattore 1: tasso di assenza generale (peso 40)
                if (tassoAssenza > 0)
                {
                    var f1 = tassoAssenza * 40;
                    score += f1;
                    if (f1 > 5) fattori.Add($"storico assenze {(tassoAssenza * 100):F0}%");
                }

                // Fattore 2: giorno settimana sfavorevole (peso fino 25)
                var dow = (int)turno.Data.DayOfWeek;
                if (turniPerDow[dow] >= 3)
                {
                    var rateDow = (double)assenzePerDow[dow] / turniPerDow[dow];
                    if (rateDow > tassoAssenza + 0.10)
                    {
                        var f2 = (rateDow - tassoAssenza) * 100;
                        score += Math.Min(25, f2);
                        fattori.Add($"{turno.Data:dddd} storicamente saltato ({rateDow * 100:F0}%)");
                    }
                }

                // Fattore 3: ponte (martedì dopo lunedì festivo, oppure venerdì prima di sabato dove non si lavora; semplifico: mar/gio adiacenti a weekend in cui ho avuto assenze)
                if (turno.Data.DayOfWeek == DayOfWeek.Monday || turno.Data.DayOfWeek == DayOfWeek.Friday)
                {
                    score += 8;
                    fattori.Add($"{(turno.Data.DayOfWeek == DayOfWeek.Monday ? "lunedì" : "venerdì")} adiacente al weekend");
                }

                // Fattore 4: ferie nel range (rientro / partenza)
                var feriePost = ferieFuture.FirstOrDefault(f => f.DipendenteId == dip.Id
                    && f.DataInizio.Date <= turno.Data.Date.AddDays(2)
                    && f.DataFine.Date >= turno.Data.Date.AddDays(-2));
                if (feriePost is not null)
                {
                    score += 10;
                    fattori.Add("a ridosso di ferie approvate");
                }

                // Cap a 100
                score = Math.Min(100, Math.Round(score));

                if (score >= 15)
                {
                    risk.Add(new RischioAssenza(
                        DipendenteId: dip.Id,
                        DipendenteNome: $"{dip.Nome} {dip.Cognome}",
                        Data: turno.Data,
                        ClinicaId: turno.ClinicaId,
                        Score: (int)score,
                        Fattori: fattori));
                }
            }
        }
        return risk.OrderByDescending(r => r.Score).ThenBy(r => r.Data).ToList();
    }
}

public record RischioAssenza(
    string DipendenteId,
    string DipendenteNome,
    DateTime Data,
    string ClinicaId,
    int Score,
    IReadOnlyList<string> Fattori);
