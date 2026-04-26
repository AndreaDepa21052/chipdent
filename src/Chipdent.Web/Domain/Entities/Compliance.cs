using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class VisitaMedica : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public DateTime Data { get; set; }
    public EsitoVisita Esito { get; set; } = EsitoVisita.Idoneo;
    public DateTime? ScadenzaIdoneita { get; set; }
    public string? Note { get; set; }
    public string? AllegatoNome { get; set; }
}

public enum EsitoVisita
{
    Idoneo,
    IdoneoConPrescrizioni,
    NonIdoneo,
    Sospesa
}

public class Corso : TenantEntity
{
    public string DestinatarioId { get; set; } = string.Empty;
    public DestinatarioCorso DestinatarioTipo { get; set; }
    public TipoCorso Tipo { get; set; }
    public DateTime DataConseguimento { get; set; }
    public DateTime? Scadenza { get; set; }
    public string? Note { get; set; }
}

public enum DestinatarioCorso
{
    Dottore,
    Dipendente,
    Clinica
}

public enum TipoCorso
{
    Antincendio,
    PrimoSoccorso,
    RSPP,
    RLS,
    Privacy,
    Sicurezza81_08,
    Radioprotezione,
    Anticorruzione,
    Altro
}

public class DVR : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public string Versione { get; set; } = "1.0";
    public DateTime DataApprovazione { get; set; }
    public DateTime? ProssimaRevisione { get; set; }
    public StatoDVR Stato { get; set; } = StatoDVR.Bozza;
    public string? Note { get; set; }
    public string? AllegatoNome { get; set; }
}

public enum StatoDVR
{
    Bozza,
    Approvato,
    DaRivedere,
    Scaduto
}
