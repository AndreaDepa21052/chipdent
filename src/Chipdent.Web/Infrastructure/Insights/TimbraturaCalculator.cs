using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Infrastructure.Insights;

/// <summary>
/// Calcoli derivati dalle timbrature: ore nette (al netto delle pause), ritardi,
/// ore extra/banca ore mensile rispetto al pianificato.
/// </summary>
public static class TimbraturaCalculator
{
    public const int RitardoToleranceMinutes = 10;

    /// <summary>
    /// Coppie aperte/chiuse di check-in/check-out con sottrazione delle pause.
    /// Ritorna il dettaglio per giorno + totale.
    /// </summary>
    public static GiornoLavorato AggregaGiorno(IEnumerable<Timbratura> timbratureGiornoOrdinate, IEnumerable<Turno> turniGiorno)
    {
        var ordered = timbratureGiornoOrdinate.OrderBy(t => t.Timestamp).ToList();
        var ore = TimeSpan.Zero;
        var pause = TimeSpan.Zero;
        DateTime? lastIn = null;
        DateTime? lastPauseStart = null;
        var ritardo = false;
        var uscitaAnticipata = false;
        var remoto = false;

        var turnoMain = turniGiorno.OrderBy(t => t.OraInizio).FirstOrDefault();
        var turnoEnd = turniGiorno.OrderByDescending(t => t.OraFine).FirstOrDefault();

        foreach (var t in ordered)
        {
            switch (t.Tipo)
            {
                case TipoTimbratura.CheckIn:
                    lastIn = t.Timestamp;
                    if (t.Remoto) remoto = true;
                    if (turnoMain is not null)
                    {
                        var atteso = turnoMain.Data.Date.Add(turnoMain.OraInizio);
                        if (t.Timestamp > atteso.AddMinutes(RitardoToleranceMinutes)) ritardo = true;
                    }
                    break;

                case TipoTimbratura.PauseStart:
                    if (lastIn.HasValue) lastPauseStart = t.Timestamp;
                    break;

                case TipoTimbratura.PauseEnd:
                    if (lastPauseStart.HasValue)
                    {
                        pause += t.Timestamp - lastPauseStart.Value;
                        lastPauseStart = null;
                    }
                    break;

                case TipoTimbratura.CheckOut:
                    if (lastIn.HasValue)
                    {
                        // Se c'è una pausa aperta non chiusa, la chiudo qui
                        if (lastPauseStart.HasValue)
                        {
                            pause += t.Timestamp - lastPauseStart.Value;
                            lastPauseStart = null;
                        }
                        ore += t.Timestamp - lastIn.Value;
                        lastIn = null;

                        if (turnoEnd is not null)
                        {
                            var atteso = turnoEnd.Data.Date.Add(turnoEnd.OraFine);
                            if (t.Timestamp < atteso.AddMinutes(-RitardoToleranceMinutes)) uscitaAnticipata = true;
                        }
                    }
                    break;
            }
        }

        // Sottrai le pause dal totale ore
        var oreNette = ore - pause;
        if (oreNette < TimeSpan.Zero) oreNette = TimeSpan.Zero;

        var pianificate = TimeSpan.FromHours(turniGiorno.Sum(t => Math.Max(0, (t.OraFine - t.OraInizio).TotalHours)));
        var ancoraAperto = lastIn.HasValue;

        return new GiornoLavorato(
            OreLavorate: oreNette,
            OrePausa: pause,
            OrePianificate: pianificate,
            Ritardo: ritardo,
            UsciteAnticipate: uscitaAnticipata,
            Remoto: remoto,
            AncoraAperto: ancoraAperto);
    }

    /// <summary>Banca ore = ore lavorate - ore pianificate, su un range mensile.</summary>
    public static MeseLavorato AggregaMese(string dipendenteId,
        IEnumerable<Timbratura> timbrature, IEnumerable<Turno> turni,
        DateTime primoDelMese, DateTime fineMese)
    {
        var giorni = new List<GiornoLavorato>();
        for (var d = primoDelMese.Date; d < fineMese.Date; d = d.AddDays(1))
        {
            var tg = timbrature.Where(t => t.DipendenteId == dipendenteId && t.Timestamp.Date == d).ToList();
            var trg = turni.Where(t => t.PersonaId == dipendenteId && t.TipoPersona == TipoPersona.Dipendente && t.Data.Date == d).ToList();
            if (tg.Count == 0 && trg.Count == 0) continue;
            giorni.Add(AggregaGiorno(tg, trg));
        }
        var oreL = giorni.Aggregate(TimeSpan.Zero, (acc, g) => acc + g.OreLavorate);
        var oreP = giorni.Aggregate(TimeSpan.Zero, (acc, g) => acc + g.OrePianificate);
        var pause = giorni.Aggregate(TimeSpan.Zero, (acc, g) => acc + g.OrePausa);
        var ritardi = giorni.Count(g => g.Ritardo);
        var uscite = giorni.Count(g => g.UsciteAnticipate);
        var giorniRemoto = giorni.Count(g => g.Remoto);
        var giorniLav = giorni.Count(g => g.OreLavorate > TimeSpan.Zero);
        var saldo = oreL - oreP;
        return new MeseLavorato(oreL, oreP, pause, saldo, ritardi, uscite, giorniLav, giorniRemoto);
    }
}

public record GiornoLavorato(
    TimeSpan OreLavorate,
    TimeSpan OrePausa,
    TimeSpan OrePianificate,
    bool Ritardo,
    bool UsciteAnticipate,
    bool Remoto,
    bool AncoraAperto);

public record MeseLavorato(
    TimeSpan OreLavorate,
    TimeSpan OrePianificate,
    TimeSpan OrePausa,
    TimeSpan SaldoBancaOre,
    int Ritardi,
    int UsciteAnticipate,
    int GiorniLavorati,
    int GiorniInRemoto);
