namespace Chipdent.Web.Infrastructure;

/// <summary>
/// Versione applicativa Chipdent.
/// </summary>
public static class AppVersion
{
    public const string Number = "v1.600.0";
    public const string Codename = "Tornavento Compliance";
    public static bool IsMvpReleased => Number.StartsWith("v1.");
    public static string Display => $"{Number} · {Codename}";
}
