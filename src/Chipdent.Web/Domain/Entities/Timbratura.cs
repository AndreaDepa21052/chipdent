using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Timbratura di entrata/uscita per un dipendente. Può essere effettuata via PIN
/// dal kiosk di sede, scansione QR, o inserita manualmente dal Direttore.
/// </summary>
public class Timbratura : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;

    public TipoTimbratura Tipo { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public MetodoTimbratura Metodo { get; set; } = MetodoTimbratura.Pin;

    /// <summary>Turno collegato (best-effort match per data + persona, può restare null).</summary>
    public string? TurnoCollegatoId { get; set; }

    /// <summary>UserId di chi ha registrato la timbratura (può essere il dipendente stesso o il Direttore).</summary>
    public string? RegistrataDaUserId { get; set; }
    public string? Note { get; set; }

    /// <summary>True se il dipendente sta lavorando in remoto (smart working).</summary>
    public bool Remoto { get; set; }

    // ───── Anti-frode (timbrature web) ─────

    /// <summary>Lat/lon catturati dal browser al momento della timbratura. Null se non concessi o non disponibili.</summary>
    public double? Latitudine { get; set; }
    public double? Longitudine { get; set; }

    /// <summary>Distanza in metri dalla sede (calcolata server-side al momento della timbratura).</summary>
    public double? DistanzaMetri { get; set; }

    /// <summary>True se la timbratura è risultata "fuori area": geofencing fallito ma timbratura accettata
    /// (perché smart-working o sede non geolocalizzata) — aiuta il direttore a filtrare le anomalie.</summary>
    public bool FuoriArea { get; set; }

    /// <summary>Selfie facoltativo (relativeStoragePath gestito da IFileStorage). Audit-only, non visibile in UI standard.</summary>
    public string? SelfiePath { get; set; }
}

public enum TipoTimbratura
{
    CheckIn,
    CheckOut,
    PauseStart,
    PauseEnd
}

public enum MetodoTimbratura
{
    Pin,
    Qr,
    Manuale,
    Web
}
