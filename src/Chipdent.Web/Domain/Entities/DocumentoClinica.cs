using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class DocumentoClinica : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public TipoDocumento Tipo { get; set; }
    public string Titolo { get; set; } = string.Empty;
    public string? Numero { get; set; }
    public DateTime? DataEmissione { get; set; }
    public DateTime? DataScadenza { get; set; }
    public string? EnteEmittente { get; set; }
    public string? Note { get; set; }
    public string? AllegatoNome { get; set; }
    /// <summary>Path relativo a wwwroot, es. uploads/{tenant}/documenti/abc-foo.pdf.</summary>
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }

    public StatoDocumento StatoCalcolato
    {
        get
        {
            if (DataScadenza is null) return StatoDocumento.Valido;
            if (DataScadenza.Value.Date < DateTime.UtcNow.Date) return StatoDocumento.Scaduto;
            if (DataScadenza.Value.Date < DateTime.UtcNow.AddMonths(3).Date) return StatoDocumento.InScadenza;
            return StatoDocumento.Valido;
        }
    }
}

public enum TipoDocumento
{
    CPI,
    AutorizzazioneSanitaria,
    ContrattoAffitto,
    PolizzaAssicurativa,
    LicenzaCommerciale,
    AgibilitaACI,
    SCIA,
    PianoEvacuazione,
    AccreditamentoRegionale,
    HACCP,
    Altro
}

public enum StatoDocumento
{
    Valido,
    InScadenza,
    Scaduto
}
