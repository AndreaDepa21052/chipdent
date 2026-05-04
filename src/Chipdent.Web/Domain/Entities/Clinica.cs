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

    /// <summary>
    /// True per la sigla "holding" (es. CCH) usata da Confident come soggetto pagatore
    /// dei costi cross-sede e centrali. Non è una clinica clinica, ma è un soggetto
    /// economico che genera fatture/scadenze nello scadenziario.
    /// </summary>
    public bool IsHolding { get; set; }

    /// <summary>Organico target (n. dipendenti) usato dal modulo Headcount per il gap "vs target".</summary>
    public int? OrganicoTarget { get; set; }

    /// <summary>Latitudine geografica (WGS84). Null = sede non geolocalizzata.</summary>
    public double? Latitudine { get; set; }

    /// <summary>Longitudine geografica (WGS84).</summary>
    public double? Longitudine { get; set; }

    public bool IsGeolocalized => Latitudine.HasValue && Longitudine.HasValue;

    // Dati bancari ordinante (Tesoreria → distinte SEPA)
    /// <summary>IBAN del conto della singola clinica usato come ordinante per i bonifici a fornitori
    /// di questa sede. Se null, fallback all'IBAN del tenant.</summary>
    public string? IbanOrdinante { get; set; }
    public string? BicOrdinante { get; set; }
    /// <summary>Ragione sociale stampata sulla distinta. Se null, fallback al pagatore del tenant.</summary>
    public string? RagioneSocialeOrdinante { get; set; }
}

public enum ClinicaStato
{
    Operativa,
    InApertura,
    Chiusa
}
