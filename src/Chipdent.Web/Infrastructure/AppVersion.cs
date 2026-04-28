namespace Chipdent.Web.Infrastructure;

/// <summary>
/// Versione applicativa Chipdent. Aggiornare al rilascio di milestone.
/// La prima release MVP "Tornavento" è stata <c>v1.000.0</c>.
/// La <c>v1.300.0</c> chiude tutta la mappa funzionale (Management + Direttore + Staff).
/// </summary>
public static class AppVersion
{
    /// <summary>Versione corrente, es. "v1.300.0".</summary>
    public const string Number = "v1.300.0";

    /// <summary>Codename della release.</summary>
    public const string Codename = "Tornavento Completo";

    /// <summary>True quando l'MVP è chiuso (la versione raggiunge 1.000.0).</summary>
    public static bool IsMvpReleased => Number.StartsWith("v1.");

    /// <summary>Stringa pronta per la UI: "v1.300.0 · Tornavento Completo".</summary>
    public static string Display => $"{Number} · {Codename}";
}
