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
}

public enum TipoTimbratura
{
    CheckIn,
    CheckOut
}

public enum MetodoTimbratura
{
    Pin,
    Qr,
    Manuale
}
