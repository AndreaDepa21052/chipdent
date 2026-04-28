using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Clinica : TenantEntity
{
    public string Nome { get; set; } = string.Empty;
    public string Citta { get; set; } = string.Empty;
    public string Indirizzo { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public int NumeroRiuniti { get; set; }
    public ClinicaStato Stato { get; set; } = ClinicaStato.Operativa;

    /// <summary>Latitudine geografica (WGS84). Null = sede non geolocalizzata.</summary>
    public double? Latitudine { get; set; }

    /// <summary>Longitudine geografica (WGS84).</summary>
    public double? Longitudine { get; set; }

    public bool IsGeolocalized => Latitudine.HasValue && Longitudine.HasValue;
}

public enum ClinicaStato
{
    Operativa,
    InApertura,
    Chiusa
}
