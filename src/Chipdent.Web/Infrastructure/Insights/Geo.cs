namespace Chipdent.Web.Infrastructure.Insights;

/// <summary>
/// Helper geografici. Usato per il geofencing delle timbrature web.
/// </summary>
public static class Geo
{
    private const double EarthRadiusMeters = 6371000.0;

    /// <summary>
    /// Calcola la distanza ortodromica (metri) tra due punti WGS84 con la formula di Haversine.
    /// Precisione ~0.5% — più che sufficiente per geofencing entro ~10km.
    /// </summary>
    public static double HaversineMetri(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
