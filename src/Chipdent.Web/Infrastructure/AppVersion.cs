namespace Chipdent.Web.Infrastructure;

/// <summary>
/// Versione applicativa Chipdent. Aggiornare al rilascio di milestone.
/// La prima release MVP "Tornavento" è stata <c>v1.000.0</c>.
/// </summary>
public static class AppVersion
{
    /// <summary>Versione corrente, es. "v1.200.0".</summary>
    public const string Number = "v1.200.0";

    /// <summary>Codename della release.</summary>
    public const string Codename = "Tornavento+Staff";

    /// <summary>True quando l'MVP è chiuso (la versione raggiunge 1.000.0).</summary>
    public static bool IsMvpReleased => Number.StartsWith("v1.");

    /// <summary>Stringa pronta per la UI: "v1.200.0 · Tornavento+Staff".</summary>
    public static string Display => $"{Number} · {Codename}";
}
