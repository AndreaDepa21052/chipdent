using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Richiesta di prodotto raccolta dal portale, formulata in agile mode
/// ("Come {ruolo} vorrei {obiettivo} così che {beneficio}").
/// Tutta la rete può votare; l'Owner promuove le richieste a backlog (InLavorazione)
/// o le scarta. Le richieste votate guidano la roadmap di piattaforma.
/// </summary>
public class ProductBacklogItem : TenantEntity
{
    public string AutoreUserId { get; set; } = string.Empty;
    public string AutoreNome { get; set; } = string.Empty;
    public string AutoreRuolo { get; set; } = string.Empty;

    // ─── Story strutturata (agile mode) ──────────────────────────────
    /// <summary>"Come ___" — ruolo del beneficiario (Staff, Direttore, Backoffice, Management).</summary>
    public string ComeRuolo { get; set; } = string.Empty;
    /// <summary>"vorrei ___" — funzionalità richiesta.</summary>
    public string Vorrei { get; set; } = string.Empty;
    /// <summary>"così che ___" — beneficio atteso.</summary>
    public string CosiChe { get; set; } = string.Empty;

    // ─── Classificazione ──────────────────────────────────────────────
    public CategoriaBacklog Categoria { get; set; } = CategoriaBacklog.Altro;
    public AreaBacklog Area { get; set; } = AreaBacklog.Operativita;
    public ImpattoBacklog Impatto { get; set; } = ImpattoBacklog.Medio;

    // ─── Commento aggiuntivo ─────────────────────────────────────────
    public string? Commento { get; set; }

    // ─── Voti ─────────────────────────────────────────────────────────
    public List<string> VotantiUserIds { get; set; } = new();

    // ─── Workflow Owner ──────────────────────────────────────────────
    public StatoBacklog Stato { get; set; } = StatoBacklog.Proposta;
    public string? NotaOwner { get; set; }
    public string? GestitaDaUserId { get; set; }
    public string? GestitaDaNome { get; set; }
    public DateTime? DataDecisione { get; set; }
    /// <summary>Compilato quando l'Owner promuove la richiesta a sprint (es. "Sprint 7").</summary>
    public string? SprintTarget { get; set; }
}

public enum CategoriaBacklog
{
    NuovaFunzionalita,
    Miglioramento,
    Bug,
    Integrazione,
    Performance,
    UX,
    Reportistica,
    Compliance,
    Altro
}

public enum AreaBacklog
{
    Operativita,
    Anagrafiche,
    Compliance,
    Direzionale,
    Comunicazione,
    Mobile,
    Amministrazione,
    Altro
}

public enum ImpattoBacklog
{
    Basso,
    Medio,
    Alto,
    Critico
}

public enum StatoBacklog
{
    /// <summary>Appena inserita, in attesa di voti e di decisione Owner.</summary>
    Proposta,
    /// <summary>Owner l'ha esaminata e attende ulteriori voti / discussione.</summary>
    InEsame,
    /// <summary>Promossa a backlog, in lavorazione su uno sprint.</summary>
    InLavorazione,
    /// <summary>Funzionalità rilasciata.</summary>
    Completata,
    /// <summary>Scartata o duplicata.</summary>
    Scartata
}
