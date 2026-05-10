using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Documento del fascicolo personale del dipendente.
/// Funge da estensione della checklist documentale: ogni riga è una tipologia
/// di documento (es. C2 storico, UNILAV, codice etico) in stato "presente" o
/// "mancante", con allegato opzionale e data di acquisizione.
/// </summary>
public class DocumentoDipendente : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public TipoDocumentoDipendente Tipo { get; set; } = TipoDocumentoDipendente.Altro;

    /// <summary>Etichetta libera quando Tipo = Altro.</summary>
    public string? EtichettaLibera { get; set; }

    public DateTime? DataAcquisizione { get; set; }
    public DateTime? Scadenza { get; set; }

    public string? Note { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

/// <summary>
/// Categorie del frontespizio fascicolo dipendente (Confident).
/// Mantenute allineate al template "002 - IDENT VARESE SRL - Frontespizio ASO".
/// </summary>
public enum TipoDocumentoDipendente
{
    PropostaAssunzioneFirmata,
    ModuloRichiestaDati,
    CartaIdentita,
    CodiceFiscale,
    PermessoSoggiorno,
    C2Storico,
    LetteraAssunzioneFirmata,
    DestinazioneTfr,
    DetrazioniImposta,
    Unilav,
    LetteraDistaccoFirmata,
    UnilavDistacco,
    DecretoTrasparenza,
    CodiceEtico,
    Factorial,
    Autocertificazione,
    Altro
}
