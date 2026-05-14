using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Documento del fascicolo del dottore (RC professionale, documenti di identità,
/// iscrizione albo, diplomi, ecc.). Ogni voce può avere un allegato e una
/// scadenza che alimenta gli alert in profilo.
/// </summary>
public class DocumentoDottore : TenantEntity
{
    public string DottoreId { get; set; } = string.Empty;
    public TipoDocumentoDottore Tipo { get; set; } = TipoDocumentoDottore.Altro;

    /// <summary>Etichetta libera quando Tipo = Altro o per distinguere più documenti dello stesso tipo (es. due polizze RC).</summary>
    public string? EtichettaLibera { get; set; }

    public string? NumeroDocumento { get; set; }
    public string? Compagnia { get; set; }   // valido per RC professionale

    public DateTime? DataEmissione { get; set; }
    public DateTime? Scadenza { get; set; }

    public string? Note { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

public enum TipoDocumentoDottore
{
    RcProfessionale,
    CartaIdentita,
    CodiceFiscale,
    PartitaIVA,
    IscrizioneAlbo,
    Diploma,
    Specializzazione,
    Curriculum,
    Altro
}
