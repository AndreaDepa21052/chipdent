using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Segnalazione operativa aperta dallo Staff: guasto attrezzatura, problema di sicurezza, ticket generico.
/// Workflow: Aperta → InLavorazione (presa in carico) → Risolta. Annullata se ritirata.
/// </summary>
public class Segnalazione : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public string MittenteUserId { get; set; } = string.Empty;
    public string MittenteNome { get; set; } = string.Empty;

    public TipoSegnalazione Tipo { get; set; } = TipoSegnalazione.Altro;
    public PrioritaSegnalazione Priorita { get; set; } = PrioritaSegnalazione.Media;

    public string Titolo { get; set; } = string.Empty;
    public string Descrizione { get; set; } = string.Empty;

    public StatoSegnalazione Stato { get; set; } = StatoSegnalazione.Aperta;

    public string? AssegnatoAUserId { get; set; }
    public string? AssegnatoANome { get; set; }
    public DateTime? DataPresaInCarico { get; set; }

    public DateTime? DataRisoluzione { get; set; }
    public string? NoteRisoluzione { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

public enum TipoSegnalazione
{
    GuastoAttrezzatura,
    ProblemaSicurezza,
    Approvvigionamento,
    PuliziaIgiene,
    InfrastrutturaIT,
    Altro
}

public enum PrioritaSegnalazione
{
    Bassa,
    Media,
    Alta,
    Urgente
}

public enum StatoSegnalazione
{
    Aperta,
    InLavorazione,
    Risolta,
    Annullata
}
