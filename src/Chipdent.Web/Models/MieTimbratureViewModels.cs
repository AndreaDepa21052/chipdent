using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Insights;

namespace Chipdent.Web.Models;

public class MieTimbratureViewModel
{
    public bool HasLinkedDipendente { get; set; }
    public string DipendenteNome { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;

    /// <summary>Stato corrente del dipendente per i pulsanti del web punch.</summary>
    public StatoTimbraturaCorrente StatoCorrente { get; set; }

    public DateTime Mese { get; set; } = DateTime.Today;

    public MeseLavorato? Aggregato { get; set; }
    public IReadOnlyList<TimbraturaGiorno> Giorni { get; set; } = Array.Empty<TimbraturaGiorno>();

    public int CorrezioniPendenti { get; set; }
}

public record TimbraturaGiorno(
    DateTime Data,
    GiornoLavorato Aggregato,
    IReadOnlyList<Timbratura> Timbrature,
    IReadOnlyList<Turno> Turni);

public enum StatoTimbraturaCorrente
{
    /// <summary>Non ha ancora timbrato l'ingresso, o ha già fatto check-out.</summary>
    Fuori,
    /// <summary>Ha timbrato il check-in, è "al lavoro".</summary>
    AlLavoro,
    /// <summary>Ha timbrato l'inizio pausa.</summary>
    InPausa
}
