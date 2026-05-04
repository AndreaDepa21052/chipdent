using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Intervento/manutenzione/contratto ricorrente legato a una clinica
/// (tracciato sul "Calendario interventi" — registro antincendio, controlli
/// elettrici, radiografico, smaltimento rifiuti, ecc.).
/// </summary>
public class InterventoClinica : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public TipoIntervento Tipo { get; set; }

    /// <summary>Fornitore di riferimento (CVZ Antincendi, Ecologia Ambiente, NOLOMEDICAL...).</summary>
    public string Fornitore { get; set; } = string.Empty;

    /// <summary>Frequenza descrittiva ("a 6 mesi", "biennale", "annuale"...).</summary>
    public string? Frequenza { get; set; }

    public DateTime? DataUltimoIntervento { get; set; }
    public DateTime? ProssimaScadenza { get; set; }

    /// <summary>Archiviato in faldone ATS (sì/no).</summary>
    public bool ArchiviatoFaldoneAts { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Dettagli specifici per tipologia (es. tariffe Ecologia Ambiente, codici RENTRI…).
    /// Chiave libera, valore stringa per flessibilità senza schema rigido.
    /// </summary>
    public Dictionary<string, string> Dettagli { get; set; } = new();
}

public enum TipoIntervento
{
    RegistroAntincendio,
    PuliziaFiltriCondizionatori,
    MessaATerra,
    ImpiantoElettricoAnnuale,
    ElettromedicaliBiennale,
    Radiografico,
    BombolaOssigeno,
    Nolomedical,
    EcologiaAmbienteContratto,
    EcologiaAmbienteRentri
}
