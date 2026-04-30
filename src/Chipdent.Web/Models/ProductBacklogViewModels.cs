using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class ProductBacklogIndexViewModel
{
    public IReadOnlyList<ProductBacklogRow> Items { get; set; } = Array.Empty<ProductBacklogRow>();
    public StatoBacklog? Filter { get; set; }
    public AreaBacklog? AreaFilter { get; set; }
    public string Sort { get; set; } = "voti"; // voti | recenti | impatto
    public bool IsOwner { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;

    public int TotaleProposte { get; set; }
    public int TotaleInLavorazione { get; set; }
    public int TotaleCompletate { get; set; }
    public int VotiTotali { get; set; }
}

public record ProductBacklogRow(ProductBacklogItem Item, bool VotatoDaMe, bool MiaProposta);

/// <summary>
/// Wizard guidato a 4 step. Tutto l'input passa nello stesso form per semplicità,
/// il client mostra/nasconde gli step. Il controller valida sempre tutti i campi.
/// </summary>
public class NuovaProductBacklogViewModel
{
    // Step 1 — chi e per cosa
    [Required(ErrorMessage = "Indica per quale ruolo è la funzionalità.")]
    [StringLength(60)]
    public string ComeRuolo { get; set; } = string.Empty;

    // Step 2 — cosa e perché (la story)
    [Required(ErrorMessage = "Descrivi cosa vorresti.")]
    [StringLength(400, MinimumLength = 10, ErrorMessage = "Da 10 a 400 caratteri.")]
    public string Vorrei { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica il beneficio atteso (così che…).")]
    [StringLength(400, MinimumLength = 5, ErrorMessage = "Da 5 a 400 caratteri.")]
    public string CosiChe { get; set; } = string.Empty;

    // Step 3 — classificazione
    [Required] public CategoriaBacklog Categoria { get; set; } = CategoriaBacklog.NuovaFunzionalita;
    [Required] public AreaBacklog Area { get; set; } = AreaBacklog.Operativita;
    [Required] public ImpattoBacklog Impatto { get; set; } = ImpattoBacklog.Medio;

    // Step 4 — commento aggiuntivo
    [StringLength(2000)]
    public string? Commento { get; set; }

    /// <summary>Suggerimenti per il selettore "Come ___" — popolati dal controller.</summary>
    public IReadOnlyList<string> RuoliSuggeriti { get; set; } = Array.Empty<string>();
}

/// <summary>Form rapido di decisione Owner.</summary>
public class DecisioneBacklogViewModel
{
    [Required] public string Id { get; set; } = string.Empty;
    [StringLength(1000)] public string? NotaOwner { get; set; }
    [StringLength(40)] public string? SprintTarget { get; set; }
}
