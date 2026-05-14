using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class DottoreProfileViewModel
{
    public Dottore Dottore { get; set; } = new();
    public string? ClinicaPrincipaleNome { get; set; }
    public IReadOnlyList<Trasferimento> Storico { get; set; } = Array.Empty<Trasferimento>();
    public IReadOnlyList<AuditEntry> Audit { get; set; } = Array.Empty<AuditEntry>();
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string Tab { get; set; } = "anagrafica";

    /// <summary>Riepilogo Tesoreria del fornitore-ombra collegato al dottore. Null se non presente.</summary>
    public DottoreTesoreriaSnapshot? Tesoreria { get; set; }

    public IReadOnlyList<CollaborazioneClinica> Collaborazioni { get; set; } = Array.Empty<CollaborazioneClinica>();
    public IReadOnlyList<DocumentoDottore> Documenti { get; set; } = Array.Empty<DocumentoDottore>();
    public IReadOnlyList<AttestatoEcm> AttestatiEcm { get; set; } = Array.Empty<AttestatoEcm>();
    public IReadOnlyList<DottoreAlert> Alerts { get; set; } = Array.Empty<DottoreAlert>();
}

/// <summary>
/// Alert derivato calcolato in tempo reale dai dati del dottore.
/// Es. RC professionale in scadenza, documento scaduto, ECM sotto soglia.
/// </summary>
public record DottoreAlert(
    string Titolo,
    string Descrizione,
    AlertLivello Livello,
    DateTime? Scadenza,
    string? DocumentoId = null,
    string? Categoria = null)
{
    public string Icona => Livello switch
    {
        AlertLivello.Critico => "🔴",
        AlertLivello.Avviso  => "🟡",
        _ => "🟢"
    };
    public string Badge => Livello switch
    {
        AlertLivello.Critico => "badge--danger",
        AlertLivello.Avviso  => "badge--warning",
        _ => "badge--success"
    };
}

public enum AlertLivello
{
    Ok,
    Avviso,
    Critico
}

/// <summary>Riga della lista dottori con collaborazioni e alert pre-calcolati.</summary>
public class DottoreListItem
{
    public Dottore Dottore { get; set; } = new();
    public IReadOnlyList<CollaborazioneClinica> Collaborazioni { get; set; } = Array.Empty<CollaborazioneClinica>();
    public int AlertCritici { get; set; }
    public int AlertAvvisi { get; set; }

    public DateTime DataInizioCollaborazione =>
        Collaborazioni.Count == 0 ? Dottore.DataAssunzione : Collaborazioni.Min(c => c.DataInizio);

    public DateTime? DataFineCollaborazione
    {
        get
        {
            if (Collaborazioni.Count == 0) return Dottore.DataDimissioni;
            if (Collaborazioni.Any(c => c.DataFine is null)) return null;
            return Collaborazioni.Max(c => c.DataFine);
        }
    }
}

public class DottoreTesoreriaSnapshot
{
    public string FornitoreId { get; set; } = string.Empty;
    public decimal EspostoAperto { get; set; }
    public decimal FatturatoYTD { get; set; }
    public int FattureInApprovazione { get; set; }
    public int ScadenzeAperte { get; set; }
    public int ScadenzeScadute { get; set; }
}

public class DipendenteProfileViewModel
{
    public Dipendente Dipendente { get; set; } = new();
    public string? ClinicaCorrenteNome { get; set; }
    public string? ManagerNome { get; set; }
    public IReadOnlyList<Trasferimento> Storico { get; set; } = Array.Empty<Trasferimento>();
    public IReadOnlyList<AuditEntry> Audit { get; set; } = Array.Empty<AuditEntry>();
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string Tab { get; set; } = "anagrafica";

    public IReadOnlyList<DistaccoDipendente> Distacchi { get; set; } = Array.Empty<DistaccoDipendente>();
    public IReadOnlyList<VisitaMedica> VisiteMediche { get; set; } = Array.Empty<VisitaMedica>();
    public IReadOnlyList<Corso> Corsi { get; set; } = Array.Empty<Corso>();
    public IReadOnlyList<ProcedimentoDisciplinare> Disciplinari { get; set; } = Array.Empty<ProcedimentoDisciplinare>();
    public IReadOnlyList<PremioDipendente> Premi { get; set; } = Array.Empty<PremioDipendente>();
    public IReadOnlyList<SchedaValutazione> Valutazioni { get; set; } = Array.Empty<SchedaValutazione>();
    public IReadOnlyList<DocumentoDipendente> Documenti { get; set; } = Array.Empty<DocumentoDipendente>();
    public IReadOnlyList<CambioLivelloRetribuzione> CambiLivello { get; set; } = Array.Empty<CambioLivelloRetribuzione>();
    public IReadOnlyList<CambioMansioneReparto> CambiMansione { get; set; } = Array.Empty<CambioMansioneReparto>();
}

/// <summary>
/// Stato compliance per un singolo requisito (visita medica o corso) — usato
/// dalla checklist del profilo dipendente per i bollini verde/giallo/rosso.
/// </summary>
public record ComplianceCheck(
    string Etichetta,
    DateTime? DataUltima,
    DateTime? Scadenza,
    ComplianceStato Stato,
    string? Riferimento)
{
    public string Icona => Stato switch
    {
        ComplianceStato.InRegola => "✅",
        ComplianceStato.InScadenza => "🟡",
        ComplianceStato.Scaduto => "🔴",
        _ => "⬜"
    };
    public string ColoreBordo => Stato switch
    {
        ComplianceStato.InRegola => "#1f6e3a",
        ComplianceStato.InScadenza => "#a86010",
        ComplianceStato.Scaduto => "#a83a3a",
        _ => "#aaa"
    };
}

public enum ComplianceStato
{
    InRegola,
    InScadenza,
    Scaduto,
    NonRegistrato
}

public class TransferViewModel
{
    public string PersonaId { get; set; } = string.Empty;
    public TipoPersona PersonaTipo { get; set; }
    public string PersonaNome { get; set; } = string.Empty;
    public string? ClinicaAttualeId { get; set; }
    public string? ClinicaAttualeNome { get; set; }

    [Required]
    public string ClinicaAId { get; set; } = string.Empty;

    [Required]
    public DateTime DataEffetto { get; set; } = DateTime.Today;

    public MotivoTrasferimento Motivo { get; set; } = MotivoTrasferimento.Riorganizzazione;

    public string? Note { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public class DismissViewModel
{
    public string PersonaId { get; set; } = string.Empty;
    public TipoPersona PersonaTipo { get; set; }
    public string PersonaNome { get; set; } = string.Empty;

    [Required]
    public DateTime DataDimissioni { get; set; } = DateTime.Today;

    public string? Motivo { get; set; }
}
