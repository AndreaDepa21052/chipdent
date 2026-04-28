namespace Chipdent.Web.Infrastructure;

/// <summary>
/// Versione applicativa Chipdent. Aggiornare al rilascio di milestone.
/// La prima release MVP "Tornavento" sarà <c>v1.000.0</c>.
/// </summary>
public static class AppVersion
{
    /// <summary>Versione corrente, es. "v0.900.0".</summary>
    public const string Number = "v0.900.0";

    /// <summary>Codename della release, es. "Tornavento".</summary>
    public const string Codename = "pre-Tornavento";

    /// <summary>True quando l'MVP è chiuso (la versione raggiunge 1.000.0).</summary>
    public static bool IsMvpReleased => Number.StartsWith("v1.");

    /// <summary>Stringa pronta per la UI: "v0.900.0 · pre-Tornavento".</summary>
    public static string Display => $"{Number} · {Codename}";
}
