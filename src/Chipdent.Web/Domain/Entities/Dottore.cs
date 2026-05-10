using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Dottore : TenantEntity
{
    /// <summary>Codice anagrafico interno, univoco per tenant. Es. "D0001".</summary>
    public string? Codice { get; set; }

    // Identità
    public string Nome { get; set; } = string.Empty;
    public string Cognome { get; set; } = string.Empty;
    public DateTime? DataNascita { get; set; }
    public string? LuogoNascita { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? PartitaIVA { get; set; }
    public string? FotoUrl { get; set; }

    // Contatti
    public string Email { get; set; } = string.Empty;
    public string? EmailPersonale { get; set; }
    public string? Telefono { get; set; }
    public string? Cellulare { get; set; }
    public string? IndirizzoResidenza { get; set; }

    // Documenti
    public string? DocumentoNumero { get; set; }
    public DateTime? DocumentoScadenza { get; set; }

    // Professione
    public string Specializzazione { get; set; } = string.Empty;
    public string NumeroAlbo { get; set; } = string.Empty;
    public DateTime? ScadenzaAlbo { get; set; }
    public int? AnniEsperienza { get; set; }
    public string? IBAN { get; set; }

    // Contratto
    public TipoContratto TipoContratto { get; set; } = TipoContratto.Collaborazione;
    public DateTime DataAssunzione { get; set; } = DateTime.UtcNow;
    public DateTime? DataDimissioni { get; set; }
    public string? MotivoDimissioni { get; set; }
    public string? ClinicaPrincipaleId { get; set; }
    public bool Attivo { get; set; } = true;

    // ECM (Educazione Continua in Medicina)
    /// <summary>Crediti ECM acquisiti nel triennio in corso.</summary>
    public int CreditiEcmTriennio { get; set; }
    /// <summary>Anno (4 cifre) in cui scade il triennio ECM corrente. Es. 2026.</summary>
    public int? AnnoFineTriennioEcm { get; set; }
    /// <summary>Soglia minima crediti ECM nel triennio (default 150 — normativa AGENAS).</summary>
    public int CreditiEcmRichiestiTriennio { get; set; } = 150;

    // Annotazioni
    public string? Note { get; set; }

    public string NomeCompleto => $"Dr. {Nome} {Cognome}".Trim();
    public string Iniziali =>
        (string.IsNullOrEmpty(Nome) ? "?" : Nome[..1]) +
        (string.IsNullOrEmpty(Cognome) ? "" : Cognome[..1]);
    public int? AnniServizio =>
        Attivo ? (int)Math.Floor((DateTime.UtcNow - DataAssunzione).TotalDays / 365)
               : DataDimissioni is null ? null
                 : (int)Math.Floor((DataDimissioni.Value - DataAssunzione).TotalDays / 365);
    public bool IsCessato => !Attivo || DataDimissioni is not null;
}

public enum TipoContratto
{
    Collaborazione,
    Dipendente,
    LiberoProfessionista,
    PartTime,
    Stage,
    Apprendistato
}
