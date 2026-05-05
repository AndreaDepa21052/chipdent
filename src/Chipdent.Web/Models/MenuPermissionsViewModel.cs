using Chipdent.Web.Services;

namespace Chipdent.Web.Models;

public class MenuPermissionsViewModel
{
    public IReadOnlyList<MenuCatalog.Group> Groups { get; init; } = Array.Empty<MenuCatalog.Group>();
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Mappa role -> set di slug nascosti. Una sezione è visibile se NON è nel set.
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>> Hidden { get; init; } =
        new Dictionary<string, HashSet<string>>();

    public bool IsHidden(string role, string slug) =>
        Hidden.TryGetValue(role, out var set) && set.Contains(slug);

    public string Flash { get; init; } = string.Empty;
}
