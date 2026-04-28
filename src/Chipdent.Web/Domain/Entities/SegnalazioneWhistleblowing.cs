using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Segnalazione whistleblowing ai sensi del D.Lgs. 24/2023.
/// Può essere completamente anonima (nessuna identità collegata) oppure firmata.
/// È identificata da un <see cref="CodiceTracciamento"/> univoco condiviso col segnalante,
/// che gli permette di seguire lo stato senza dover loggarsi.
/// </summary>
public class SegnalazioneWhistleblowing : TenantEntity
{
    /// <summary>Codice univoco per il tracciamento da parte del segnalante.</summary>
    public string CodiceTracciamento { get; set; } = string.Empty;

    /// <summary>Hash del codice di accesso scelto dal segnalante (PIN/parola d'ordine), per autenticare follow-up successivi.</summary>
    public string? CodiceAccessoHash { get; set; }

    public TipoViolazioneWhistleblowing Tipo { get; set; } = TipoViolazioneWhistleblowing.Altro;

    public string Oggetto { get; set; } = string.Empty;
    public string Descrizione { get; set; } = string.Empty;
    public string? FattiESoggetti { get; set; }

    /// <summary>Sede coinvolta. Null se la segnalazione riguarda l'intero tenant.</summary>
    public string? ClinicaId { get; set; }

    /// <summary>True se il segnalante ha scelto di rimanere anonimo. Nessun campo identificativo è valorizzato.</summary>
    public bool Anonima { get; set; } = true;

    /// <summary>Identità del segnalante (solo se Anonima = false).</summary>
    public string? FirmatarioNome { get; set; }
    public string? FirmatarioEmail { get; set; }
    public string? FirmatarioRuolo { get; set; }

    public StatoWhistleblowing Stato { get; set; } = StatoWhistleblowing.Aperta;

    /// <summary>Cronologia conversazione segnalante↔compliance officer.</summary>
    public List<MessaggioWhistleblowing> Conversazione { get; set; } = new();

    /// <summary>UserId del Compliance Officer / Owner che gestisce il caso.</summary>
    public string? GestitoDaUserId { get; set; }
    public string? GestitoDaNome { get; set; }
    public DateTime? PresoInCaricoIl { get; set; }

    public DateTime? DataChiusura { get; set; }
    public string? EsitoFinale { get; set; }

    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }
}

public class MessaggioWhistleblowing
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    /// <summary>True se il messaggio è del segnalante (via codice tracciamento), false se del compliance officer.</summary>
    public bool DalSegnalante { get; set; }
    public string? AutoreNome { get; set; }
    public string Testo { get; set; } = string.Empty;
}

public enum TipoViolazioneWhistleblowing
{
    CorruzioneEAbusi,
    FrodeFiscaleEContabile,
    DiscriminazioneEMolestie,
    SaluteESicurezzaSulLavoro,
    ProtezioneDati,
    TutelaAmbientale,
    ViolazioneNormativaSanitaria,
    Altro
}

public enum StatoWhistleblowing
{
    Aperta,
    InEsame,
    InfoRichiesti,
    Risolta,
    Archiviata,
    NonAmmissibile
}
