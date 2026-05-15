using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

/// <summary>
/// Riepilogo "fascia orizzontale" della modale di modifica clinica.
/// Ogni criticità ha un'etichetta breve, l'icona, un anchor verso il campo
/// del form (jump-to) oppure un link esterno (es. Calendario interventi).
/// </summary>
public class ClinicaEditModalViewModel
{
    public Clinica Clinica { get; set; } = new();

    /// <summary>Numero interventi scaduti dal Calendario interventi.</summary>
    public int InterventiScaduti { get; set; }
    /// <summary>Numero interventi con scadenza ≤ 30 gg.</summary>
    public int InterventiImminenti { get; set; }

    public int Dottori { get; set; }
    public int Dipendenti { get; set; }

    /// <summary>Percentuale di completezza anagrafica (0-100).</summary>
    public int Completezza { get; set; }

    public List<ClinicaCriticita> Critiche { get; set; } = new();
    public List<ClinicaCriticita> Avvisi { get; set; } = new();

    public bool NessunaCriticita => Critiche.Count == 0 && Avvisi.Count == 0;
}

public record ClinicaCriticita(
    string Etichetta,
    string Icona,
    string? JumpToFieldId = null,
    string? JumpToSectionId = null,
    string? Href = null,
    string? Tooltip = null);
