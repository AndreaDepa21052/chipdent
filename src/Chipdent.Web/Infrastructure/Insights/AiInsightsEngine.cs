using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Insights;

/// <summary>
/// Motore di "AI insights" deterministico — calcola previsioni e anomalie
/// con euristiche multifattoriali su dati reali. Non usa modelli ML né LLM.
/// L'output è presentato all'utente come raccomandazione con score di confidenza.
/// </summary>
public class AiInsightsEngine
{
    private readonly MongoContext _mongo;
    public AiInsightsEngine(MongoContext mongo) => _mongo = mongo;

    public async Task<AiInsightsSnapshot> ComputeAsync(string tenantId, CancellationToken ct = default)
    {
        var oggi = DateTime.UtcNow.Date;
        var dodiciMesiFa = oggi.AddMonths(-12);
        var trentaGiorniFa = oggi.AddDays(-30);

        var dipendenti = await _mongo.Dipendenti.Find(d => d.TenantId == tenantId).ToListAsync(ct);
        var attivi = dipendenti.Where(d => d.Stato != StatoDipendente.Cessato).ToList();
        var cliniche = await _mongo.Cliniche.Find(c => c.TenantId == tenantId).ToListAsync(ct);
        var clinicheById = cliniche.ToDictionary(c => c.Id);

        var ferie = await _mongo.RichiesteFerie.Find(r => r.TenantId == tenantId).ToListAsync(ct);
        var cambi = await _mongo.RichiesteCambioTurno.Find(r => r.TenantId == tenantId).ToListAsync(ct);
        var timbrature = await _mongo.Timbrature.Find(t => t.TenantId == tenantId && t.Timestamp >= dodiciMesiFa).ToListAsync(ct);
        var turni12m = await _mongo.Turni.Find(t => t.TenantId == tenantId && t.Data >= dodiciMesiFa).ToListAsync(ct);

        // ─── Risk turnover ───
        var risks = new List<TurnoverRisk>();
        foreach (var d in attivi)
        {
            var anniServizio = d.AnniServizio ?? 0;
            var ferieDip = ferie.Where(f => f.DipendenteId == d.Id).ToList();
            var cambiDip = cambi.Where(c => c.PersonaIdRichiedente == d.Id).ToList();
            var timbDip = timbrature.Where(t => t.DipendenteId == d.Id).ToList();

            int score = 0;
            var fattori = new List<string>();

            // anzianità: i pattern storici mostrano picchi di turnover a 2-3 e 5-7 anni
            if (anniServizio is >= 2 and <= 3) { score += 15; fattori.Add("seniority a 2-3 anni (cluster di turnover storico)"); }
            else if (anniServizio is >= 5 and <= 7) { score += 12; fattori.Add("seniority a 5-7 anni (saturation point)"); }

            // ferie residue insolite (consumate troppo presto = potrebbe sganciarsi)
            if (d.GiorniFerieResidui <= 3 && oggi.Month <= 9)
            {
                score += 10; fattori.Add($"saldo ferie già a {d.GiorniFerieResidui}g a metà anno");
            }

            // cambi turno frequenti
            var cambiUltimi90 = cambiDip.Count(c => c.CreatedAt >= oggi.AddDays(-90));
            if (cambiUltimi90 >= 3) { score += 18; fattori.Add($"{cambiUltimi90} richieste cambio turno negli ultimi 90g"); }

            // ritardi cronici nelle timbrature ultimi 30g
            var ritardiUltimi30 = ContaRitardi(timbDip.Where(t => t.Timestamp >= trentaGiorniFa), turni12m.Where(t => t.PersonaId == d.Id));
            if (ritardiUltimi30 >= 4) { score += 12; fattori.Add($"{ritardiUltimi30} ritardi negli ultimi 30g"); }

            // dipendente in onboarding: rischio fisiologico
            if (d.Stato == StatoDipendente.Onboarding) { score += 8; fattori.Add("ancora in onboarding"); }

            // periodo prova ancora aperto
            if (d.DataFinePeriodoProva is not null && d.DataFinePeriodoProva > oggi)
            {
                score += 6; fattori.Add("periodo di prova in corso");
            }

            // contratto a termine in scadenza
            // (facoltativo: in mancanza dati contratti, si riduce)

            if (score == 0) continue; // include solo a partire da almeno 1 fattore

            var livello = score switch
            {
                >= 35 => "Alto",
                >= 20 => "Medio",
                _     => "Basso"
            };
            risks.Add(new TurnoverRisk(
                DipendenteId: d.Id,
                Nome: d.NomeCompleto,
                Ruolo: d.Ruolo.ToString(),
                ClinicaNome: clinicheById.TryGetValue(d.ClinicaId, out var c) ? c.Nome : "—",
                Score: Math.Min(100, score),
                Livello: livello,
                Fattori: fattori));
        }
        risks = risks.OrderByDescending(r => r.Score).Take(20).ToList();

        // ─── Forecast organico (3 mesi) ───
        // Trend lineare semplice: media netto (assunti-cessati) nei 12 mesi → estrapolata su 3 mesi.
        var nettoMese = new List<int>();
        for (var i = 11; i >= 0; i--)
        {
            var mese = new DateTime(oggi.Year, oggi.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            var fineMese = mese.AddMonths(1);
            var ass = dipendenti.Count(d => d.DataAssunzione >= mese && d.DataAssunzione < fineMese);
            var ces = dipendenti.Count(d => d.DataDimissioni is not null && d.DataDimissioni >= mese && d.DataDimissioni < fineMese);
            nettoMese.Add(ass - ces);
        }
        var mediaNetta = nettoMese.Count > 0 ? nettoMese.Average() : 0;
        var trend = nettoMese.Count >= 3 ? nettoMese.TakeLast(3).Average() - nettoMese.Take(3).Average() : 0;

        var forecast = new List<HeadcountForecast>();
        var organicoCorrente = attivi.Count;
        for (var m = 1; m <= 3; m++)
        {
            // forecast = corrente + (mediaNetta + trendParziale) cumulato
            var deltaPredetto = (decimal)(mediaNetta + (trend * 0.3 * m));
            var organicoStimato = organicoCorrente + (int)Math.Round(deltaPredetto * m);
            var confidenza = nettoMese.Count >= 6 ? 75 - (m * 10) : 50 - (m * 8);
            forecast.Add(new HeadcountForecast(
                Mese: oggi.AddMonths(m),
                OrganicoStimato: Math.Max(0, organicoStimato),
                DeltaStimato: (int)Math.Round(deltaPredetto * m),
                Confidenza: Math.Max(20, confidenza)));
        }

        // ─── Anomalie presenze (ritardi anomali rispetto a media personale) ───
        var anomalie = new List<PresenzeAnomaly>();
        foreach (var d in attivi)
        {
            var timbDip = timbrature.Where(t => t.DipendenteId == d.Id).OrderBy(t => t.Timestamp).ToList();
            if (timbDip.Count < 5) continue;
            var turniDip = turni12m.Where(t => t.PersonaId == d.Id && t.TipoPersona == TipoPersona.Dipendente).ToList();

            var ritardiMin = new List<double>();
            foreach (var t in timbDip.Where(x => x.Tipo == TipoTimbratura.CheckIn))
            {
                var turnoOdierno = turniDip.FirstOrDefault(tu => tu.Data.Date == t.Timestamp.Date);
                if (turnoOdierno is null) continue;
                var atteso = turnoOdierno.Data.Date.Add(turnoOdierno.OraInizio);
                var diff = (t.Timestamp - atteso).TotalMinutes;
                if (diff > 0) ritardiMin.Add(diff);
            }
            if (ritardiMin.Count < 3) continue;

            var media = ritardiMin.Average();
            var stddev = StdDev(ritardiMin);
            // Cerca ritardi >2σ negli ultimi 14 giorni
            var recentiOutliers = timbDip.Where(t => t.Tipo == TipoTimbratura.CheckIn && t.Timestamp >= oggi.AddDays(-14))
                .Select(t => new
                {
                    T = t,
                    Atteso = turniDip.FirstOrDefault(tu => tu.Data.Date == t.Timestamp.Date) is { } tu
                        ? tu.Data.Date.Add(tu.OraInizio) : (DateTime?)null
                })
                .Where(x => x.Atteso.HasValue && (x.T.Timestamp - x.Atteso!.Value).TotalMinutes > media + 2 * stddev)
                .ToList();

            if (recentiOutliers.Count >= 1 && stddev > 0)
            {
                var pegg = recentiOutliers.MaxBy(x => (x.T.Timestamp - x.Atteso!.Value).TotalMinutes);
                var minRitardo = pegg is null ? 0 : (int)(pegg.T.Timestamp - pegg.Atteso!.Value).TotalMinutes;
                anomalie.Add(new PresenzeAnomaly(
                    DipendenteId: d.Id,
                    Nome: d.NomeCompleto,
                    ClinicaNome: clinicheById.TryGetValue(d.ClinicaId, out var c) ? c.Nome : "—",
                    Quando: pegg?.T.Timestamp ?? DateTime.UtcNow,
                    RitardoMinuti: minRitardo,
                    MediaPersonale: (int)Math.Round(media),
                    Descrizione: $"Ritardo di {minRitardo} min, oltre {Math.Round((minRitardo - media) / Math.Max(1, stddev), 1)}σ dalla media personale di {(int)media} min."));
            }
        }
        anomalie = anomalie.OrderByDescending(a => a.Quando).Take(10).ToList();

        // ─── Smart staffing: previsione fabbisogno sede × ruolo prossimi 30g ───
        // Compara turni pianificati prossimi 30g vs media storica per giorno della settimana × ruolo × sede.
        var prossimi30Inizio = oggi;
        var prossimi30Fine = oggi.AddDays(30);
        var turniProssimi = await _mongo.Turni.Find(t => t.TenantId == tenantId && t.Data >= prossimi30Inizio && t.Data < prossimi30Fine
                                                          && t.TipoPersona == TipoPersona.Dipendente).ToListAsync(ct);

        var staffing = new List<StaffingForecast>();
        foreach (var c in cliniche)
        {
            foreach (var ruolo in Enum.GetValues<RuoloDipendente>())
            {
                var dipendentiSede = attivi.Where(d => d.ClinicaId == c.Id && d.Ruolo == ruolo).Select(d => d.Id).ToHashSet();
                if (dipendentiSede.Count == 0) continue;

                // turni storici per questo ruolo+sede ultimi 12m
                var storici = turni12m.Where(t => t.ClinicaId == c.Id && dipendentiSede.Contains(t.PersonaId)).ToList();
                var futuri = turniProssimi.Where(t => t.ClinicaId == c.Id && dipendentiSede.Contains(t.PersonaId)).ToList();

                // media turni/giorno feriale storica
                var giorniFeriali = Enumerable.Range(0, 365).Select(i => dodiciMesiFa.AddDays(i))
                    .Count(g => g.DayOfWeek != DayOfWeek.Saturday && g.DayOfWeek != DayOfWeek.Sunday);
                var mediaTurniStorici = giorniFeriali > 0 ? storici.Count / (double)giorniFeriali : 0;

                var giorniFeriali30 = Enumerable.Range(0, 30).Select(i => prossimi30Inizio.AddDays(i))
                    .Count(g => g.DayOfWeek != DayOfWeek.Saturday && g.DayOfWeek != DayOfWeek.Sunday);
                var attesiProssimi = mediaTurniStorici * giorniFeriali30;
                var pianificati = futuri.Count;
                var gap = pianificati - attesiProssimi;
                var gapPct = attesiProssimi > 0 ? (gap / attesiProssimi) * 100 : 0;

                if (Math.Abs(gapPct) < 15) continue; // rumore: scostamenti trascurabili

                var raccomandazione = gap < 0
                    ? $"⚠ Pianificazione sotto media storica del {Math.Abs(gapPct):0}% — valuta assunzione o rinforzi temporanei."
                    : $"💡 Pianificazione sopra media del {gapPct:0}% — sovra-copertura, possibile risparmio.";

                staffing.Add(new StaffingForecast(
                    ClinicaNome: c.Nome,
                    Ruolo: ruolo.ToString(),
                    TurniPianificati: pianificati,
                    TurniAttesiStorico: (int)Math.Round(attesiProssimi),
                    GapPercentuale: (int)Math.Round(gapPct),
                    Raccomandazione: raccomandazione));
            }
        }
        staffing = staffing.OrderByDescending(s => Math.Abs(s.GapPercentuale)).Take(8).ToList();

        return new AiInsightsSnapshot(
            CalcolatoIl: DateTime.UtcNow,
            TurnoverRisks: risks,
            Forecast: forecast,
            Anomalie: anomalie,
            Staffing: staffing);
    }

    /// <summary>Score 0-100 per un candidato sostituzione (vedi SostituzioniController).</summary>
    public async Task<IReadOnlyDictionary<string, AiCandidateScore>> ScoreCandidatiAsync(
        string tenantId, IReadOnlyList<string> dipendenteIds, DateTime data, TimeSpan oraInizio, TimeSpan oraFine, CancellationToken ct = default)
    {
        var settimanaInizio = data.Date.AddDays(-7);
        var meseFa = data.Date.AddDays(-30);
        var turniRecenti = await _mongo.Turni.Find(t => t.TenantId == tenantId
                                                         && t.TipoPersona == TipoPersona.Dipendente
                                                         && t.Data >= meseFa
                                                         && dipendenteIds.Contains(t.PersonaId)).ToListAsync(ct);
        var cambiAccettati = await _mongo.RichiesteCambioTurno.Find(r => r.TenantId == tenantId
                                                                          && r.Stato == StatoCambioTurno.ApprovataDirettore
                                                                          && r.PersonaIdCollegaAccettante != null
                                                                          && dipendenteIds.Contains(r.PersonaIdCollegaAccettante)).ToListAsync(ct);

        var result = new Dictionary<string, AiCandidateScore>();
        foreach (var did in dipendenteIds)
        {
            var oreSettimanaCorrente = turniRecenti
                .Where(t => t.PersonaId == did && t.Data >= settimanaInizio && t.Data < settimanaInizio.AddDays(7))
                .Sum(t => Math.Max(0, (t.OraFine - t.OraInizio).TotalHours));
            var oreMese = turniRecenti.Where(t => t.PersonaId == did)
                .Sum(t => Math.Max(0, (t.OraFine - t.OraInizio).TotalHours));
            var nAccettatiPassato = cambiAccettati.Count(c => c.PersonaIdCollegaAccettante == did);

            // Score: meno carico = più disponibile (componente 0-50)
            var caricoScore = Math.Max(0, 50 - (int)(oreSettimanaCorrente * 1.2));
            // Storico cooperativo (componente 0-30)
            var historyScore = Math.Min(30, nAccettatiPassato * 8);
            // Bonus orario simile a quanto già fa (componente 0-20)
            var oraTipica = turniRecenti.Where(t => t.PersonaId == did).Select(t => t.OraInizio).ToList();
            var oraTipicaScore = oraTipica.Any() && Math.Abs((oraTipica.Average(t => t.TotalMinutes) - oraInizio.TotalMinutes)) < 90 ? 20 : 5;

            var total = Math.Min(100, caricoScore + historyScore + oraTipicaScore);
            var motivo = total switch
            {
                >= 75 => "ottimo candidato — basso carico, storico cooperativo",
                >= 50 => "buon candidato",
                >= 30 => "candidato accettabile",
                _     => "candidato meno indicato"
            };

            result[did] = new AiCandidateScore(total, motivo,
                CarichoOreSettimana: (int)Math.Round(oreSettimanaCorrente),
                StoricoAccettati: nAccettatiPassato);
        }
        return result;
    }

    private static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sum = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sum / (values.Count - 1));
    }

    private static int ContaRitardi(IEnumerable<Timbratura> timb, IEnumerable<Turno> turni)
    {
        var turniList = turni.ToList();
        var ritardi = 0;
        foreach (var t in timb.Where(x => x.Tipo == TipoTimbratura.CheckIn))
        {
            var turnoOdierno = turniList.FirstOrDefault(tu => tu.Data.Date == t.Timestamp.Date);
            if (turnoOdierno is null) continue;
            var atteso = turnoOdierno.Data.Date.Add(turnoOdierno.OraInizio);
            if (t.Timestamp > atteso.AddMinutes(10)) ritardi++;
        }
        return ritardi;
    }
}

public record AiInsightsSnapshot(
    DateTime CalcolatoIl,
    IReadOnlyList<TurnoverRisk> TurnoverRisks,
    IReadOnlyList<HeadcountForecast> Forecast,
    IReadOnlyList<PresenzeAnomaly> Anomalie,
    IReadOnlyList<StaffingForecast> Staffing);

public record TurnoverRisk(string DipendenteId, string Nome, string Ruolo, string ClinicaNome,
    int Score, string Livello, IReadOnlyList<string> Fattori);

public record HeadcountForecast(DateTime Mese, int OrganicoStimato, int DeltaStimato, int Confidenza);

public record PresenzeAnomaly(string DipendenteId, string Nome, string ClinicaNome,
    DateTime Quando, int RitardoMinuti, int MediaPersonale, string Descrizione);

public record StaffingForecast(string ClinicaNome, string Ruolo, int TurniPianificati, int TurniAttesiStorico,
    int GapPercentuale, string Raccomandazione);

public record AiCandidateScore(int Score, string Motivazione, int CarichoOreSettimana, int StoricoAccettati);
