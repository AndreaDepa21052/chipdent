namespace Chipdent.Web.Infrastructure;

/// <summary>
/// Versione applicativa Chipdent.
/// </summary>
public static class AppVersion
{
    public const string Number = "v1.900.0";
    public const string Codename = "Tornavento Distinctive";
    public static bool IsMvpReleased => Number.StartsWith("v1.");
    public static string Display => $"{Number} · {Codename}";
}
