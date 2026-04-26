using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Dipendente : TenantEntity
{
    // Identità
    public string Nome { get; set; } = string.Empty;
    public string Cognome { get; set; } = string.Empty;
    public DateTime? DataNascita { get; set; }
    public string? LuogoNascita { get; set; }
    public string? CodiceFiscale { get; set; }
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
    public RuoloDipendente Ruolo { get; set; } = RuoloDipendente.ASO;
    public string? MansioneSpecifica { get; set; }
    public string? Reparto { get; set; }
    public string? ManagerId { get; set; }
    public string? IBAN { get; set; }

    // Contratto
    public TipoContratto TipoContratto { get; set; } = TipoContratto.Dipendente;
    public string? LivelloContratto { get; set; }
    public string ClinicaId { get; set; } = string.Empty;
    public DateTime DataAssunzione { get; set; } = DateTime.UtcNow;
    public DateTime? DataFinePeriodoProva { get; set; }
    public DateTime? DataDimissioni { get; set; }
    public string? MotivoDimissioni { get; set; }
    public int GiorniFerieResidui { get; set; } = 26;
    public StatoDipendente Stato { get; set; } = StatoDipendente.Attivo;

    // Annotazioni
    public string? Note { get; set; }

    public string NomeCompleto => $"{Nome} {Cognome}".Trim();
    public string Iniziali =>
        (string.IsNullOrEmpty(Nome) ? "?" : Nome[..1]) +
        (string.IsNullOrEmpty(Cognome) ? "" : Cognome[..1]);
    public int? AnniServizio =>
        Stato != StatoDipendente.Cessato && DataDimissioni is null
            ? (int)Math.Floor((DateTime.UtcNow - DataAssunzione).TotalDays / 365)
            : DataDimissioni is null ? null
              : (int)Math.Floor((DataDimissioni.Value - DataAssunzione).TotalDays / 365);
    public bool IsCessato => Stato == StatoDipendente.Cessato || DataDimissioni is not null;
}

public enum RuoloDipendente
{
    ASO,
    Igienista,
    Segreteria,
    ResponsabileSede,
    Amministrazione,
    Direzione,
    Marketing,
    IT
}

public enum StatoDipendente
{
    Attivo,
    Onboarding,
    InMalattia,
    InFerie,
    InCongedo,
    Sospeso,
    Cessato
}
