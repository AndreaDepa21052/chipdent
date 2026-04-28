using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Catalogo dei DPI (Dispositivi di Protezione Individuale) gestiti per una sede.
/// Le consegne effettive sono in <see cref="ConsegnaDpi"/>.
/// </summary>
public class Dpi : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public TipoDpi Tipo { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Modello { get; set; }
    public string? Codice { get; set; }
    public int? IntervalloSostituzioneGiorni { get; set; }
    public string? Note { get; set; }
    public bool Attivo { get; set; } = true;
}

public enum TipoDpi
{
    Mascherina,
    Guanti,
    Camice,
    Occhiali,
    Calzature,
    Cuffia,
    SchermoFacciale,
    GrembiulePiombato,
    Altro
}

/// <summary>
/// Consegna di uno specifico DPI a un dipendente, con firma digitale (click) e data di scadenza.
/// </summary>
public class ConsegnaDpi : TenantEntity
{
    public string DpiId { get; set; } = string.Empty;
    public string DipendenteId { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;

    public DateTime DataConsegna { get; set; } = DateTime.UtcNow;
    public int Quantita { get; set; } = 1;
    public DateTime? ScadenzaSostituzione { get; set; }

    public DateTime? FirmaIl { get; set; }
    public string? FirmaIp { get; set; }
    public string? FirmaUserId { get; set; }
    public string? FirmaNome { get; set; }

    public string ConsegnaDaUserId { get; set; } = string.Empty;
    public string ConsegnaDaNome { get; set; } = string.Empty;
    public string? Note { get; set; }
    public StatoConsegnaDpi Stato { get; set; } = StatoConsegnaDpi.InAttesaFirma;
}

public enum StatoConsegnaDpi
{
    InAttesaFirma,
    Firmata,
    Sostituita,
    Smarrita,
    Restituita
}
