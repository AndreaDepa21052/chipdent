namespace Chipdent.Web.Infrastructure;

/// <summary>
/// Versione applicativa Chipdent. Aggiornare al rilascio di milestone.
/// La prima release MVP "Tornavento" è <c>v1.000.0</c>.
/// </summary>
public static class AppVersion
{
    /// <summary>Versione corrente, es. "v1.000.0".</summary>
    public const string Number = "v1.000.0";

    /// <summary>Codename della release, es. "Tornavento".</summary>
    public const string Codename = "Tornavento";

    /// <summary>True quando l'MVP è chiuso (la versione raggiunge 1.000.0).</summary>
    public static bool IsMvpReleased => Number.StartsWith("v1.");

    /// <summary>Stringa pronta per la UI: "v1.000.0 · Tornavento".</summary>
    public static string Display => $"{Number} · {Codename}";
}
