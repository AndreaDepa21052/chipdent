using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Configurazione globale (cross-tenant) della visibilità dei menu della sidebar
/// per ciascun ruolo. Gestita esclusivamente dal PlatformAdmin tramite il
/// pannello "Permessi menu". Una riga per ruolo: la lista contiene gli slug
/// delle sezioni nascoste a quel ruolo.
/// </summary>
public class MenuVisibility : Entity
{
    /// <summary>Nome del ruolo (es. "Owner", "Management", "Direttore", "Backoffice", "Staff").</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Slug delle sezioni nascoste (es. "tesoreria", "ai-insights").</summary>
    public List<string> HiddenSections { get; set; } = new();
}
