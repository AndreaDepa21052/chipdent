using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Dipendente : TenantEntity
{
    // Identità
    public string Nome { get; set; } = string.Empty;
    public string Cognome { get; set; } = string.Empty;
    public Sesso? Sesso { get; set; }
    public DateTime? DataNascita { get; set; }
    public string? LuogoNascita { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? Nazionalita { get; set; }
    public string? FotoUrl { get; set; }

    // Contatti
    public string Email { get; set; } = string.Empty;
    public string? EmailPersonale { get; set; }
    public string? Telefono { get; set; }
    public string? Cellulare { get; set; }

    // Residenza (split granulare)
    public string? IndirizzoResidenza { get; set; }
    public string? CittaResidenza { get; set; }
    public string? CapResidenza { get; set; }

    // Documenti d'identità
    public string? DocumentoNumero { get; set; }
    public DateTime? DocumentoScadenza { get; set; }                // generico (legacy)
    public DateTime? ScadenzaCartaIdentita { get; set; }
    public DateTime? ScadenzaTesseraSanitaria { get; set; }
    public DateTime? ScadenzaPermessoSoggiorno { get; set; }

    // Maternità
    public DateTime? InizioMaternita { get; set; }
    public DateTime? ScadenzaDocumentiMaternita { get; set; }

    // Professione
    public RuoloDipendente Ruolo { get; set; } = RuoloDipendente.ASO;
    public string? MansioneSpecifica { get; set; }
    public string? Reparto { get; set; }
    public string? ManagerId { get; set; }
    public string? IBAN { get; set; }
    public string? TitoloStudio { get; set; }
    public bool AutocertificazioneTitolo { get; set; }

    // Contratto
    public TipoContratto TipoContratto { get; set; } = TipoContratto.Dipendente;
    public string? LivelloContratto { get; set; }
    public string Ccnl { get; set; } = "Studi professionali";
    public string ClinicaId { get; set; } = string.Empty;

    /// <summary>Data del PRIMO rapporto in azienda (può differire da DataAssunzione se ci sono stati interruzioni/riassunzioni).</summary>
    public DateTime? DataPrimoRapporto { get; set; }

    /// <summary>Data inizio del rapporto corrente.</summary>
    public DateTime DataAssunzione { get; set; } = DateTime.UtcNow;

    public DateTime? DataFinePeriodoProva { get; set; }

    /// <summary>Scadenza naturale del contratto a tempo determinato (null = indeterminato).</summary>
    public DateTime? DataScadenzaContratto { get; set; }
    /// <summary>Scadenza dell'eventuale proroga del contratto a TD.</summary>
    public DateTime? DataScadenzaProroga { get; set; }

    /// <summary>Data trasformazione contratto (es. da TD → TI).</summary>
    public DateTime? DataTrasformazioneContratto { get; set; }

    /// <summary>Etichetta libera mese/anno CCNL (es. "Mar 2025") usata in alcuni report sindacali.</summary>
    public string? MeseAnnoCcnl { get; set; }

    public DateTime? DataDimissioni { get; set; }
    public string? MotivoDimissioni { get; set; }
    public string? NoteInfortunio { get; set; }

    public bool ExTirocinante { get; set; }
    public bool BeneficioTicket { get; set; }
    public int? MonteOreSettimanale { get; set; }
    public DateTime? DataAumentoLivelli { get; set; }
    public string? AvanzamentoCarriera { get; set; }

    // Riconsegna materiale alla cessazione
    public bool RiconsegnaMaterialeStudio { get; set; }
    public bool RiconsegnaMaterialeSede { get; set; }

    public int GiorniFerieResidui { get; set; } = 26;
    public StatoDipendente Stato { get; set; } = StatoDipendente.Attivo;

    /// <summary>PIN numerico (4-6 cifre) per la timbratura presenze da kiosk di sede. Null = nessuna timbratura PIN abilitata.</summary>
    public string? PinTimbratura { get; set; }

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

    /// <summary>Mesi di rapporto totali calcolati dalla data di assunzione (rapporto corrente).</summary>
    public int? MesiRapportoCorrente =>
        DataDimissioni is null
            ? (int)Math.Floor((DateTime.UtcNow - DataAssunzione).TotalDays / 30.44)
            : (int)Math.Floor((DataDimissioni.Value - DataAssunzione).TotalDays / 30.44);

    public bool IsCessato => Stato == StatoDipendente.Cessato || DataDimissioni is not null;
}

public enum Sesso
{
    M,
    F,
    Altro
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
