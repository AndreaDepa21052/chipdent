using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class VisitaMedica : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public DateTime Data { get; set; }
    public EsitoVisita Esito { get; set; } = EsitoVisita.Idoneo;
    public DateTime? ScadenzaIdoneita { get; set; }

    /// <summary>Periodicità della visita (mesi). Default suggerito da ruolo (ASO/Igienista 12, altri 60).</summary>
    public int? MesiPeriodicita { get; set; }

    public string? Note { get; set; }
    public string? AllegatoNome { get; set; }
    public string? AllegatoPath { get; set; }
    public long? AllegatoSize { get; set; }

    /// <summary>Restituisce la periodicità (mesi) di default per il ruolo, sovrascrivibile manualmente.
    /// Convenzione AGENAS: profili sanitari ad alta esposizione → 12 mesi, altri profili → 60 mesi.</summary>
    public static int PeriodicitaDefault(RuoloDipendente ruolo) => ruolo switch
    {
        RuoloDipendente.ASO => 12,
        RuoloDipendente.Igienista => 12,
        _ => 60
    };
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

    /// <summary>Numero/riferimento del verbale di nomina (usato per RLS). Opzionale.</summary>
    public string? VerbaleNomina { get; set; }

    public string? AttestatoNome { get; set; }
    public string? AttestatoPath { get; set; }
    public long? AttestatoSize { get; set; }
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
    /// <summary>Formazione generale sicurezza lavoratori (4 ore, una tantum).</summary>
    FormazioneGeneraleSicurezza,
    /// <summary>Formazione specifica rischio basso (4h, valida 5 anni).</summary>
    FormazioneSpecificaRischioBasso,
    /// <summary>Formazione specifica rischio alto - seconda parte ASO (12h, valida 5 anni).</summary>
    FormazioneSpecificaRischioAltoASO,
    /// <summary>Aggiornamento ASO 10 ore annuale (corso a settembre/ottobre).</summary>
    AggiornamentoASO10H,
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
