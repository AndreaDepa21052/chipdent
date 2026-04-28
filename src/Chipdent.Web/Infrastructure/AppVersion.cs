namespace Chipdent.Web.Infrastructure;

/// <summary>
/// Versione applicativa Chipdent. Aggiornare al rilascio di milestone.
/// </summary>
public static class AppVersion
{
    /// <summary>Versione corrente, es. "v1.500.0".</summary>
    public const string Number = "v1.500.0";

    /// <summary>Codename della release.</summary>
    public const string Codename = "Tornavento Multi-Workspace";

    /// <summary>True quando l'MVP è chiuso (la versione raggiunge 1.000.0).</summary>
    public static bool IsMvpReleased => Number.StartsWith("v1.");

    /// <summary>Stringa pronta per la UI: "v1.500.0 · Tornavento Multi-Workspace".</summary>
    public static string Display => $"{Number} · {Codename}";
}
