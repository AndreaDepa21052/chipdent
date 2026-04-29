using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Ronda di sicurezza apertura/chiusura sede: checklist firmata digitalmente da chi apre o chiude la clinica.
/// Compila controlli su allarme, chiavi, frigo farmaci, autoclave, ecc.
/// Audit-ready: una riga per ogni passaggio + timestamp + userId.
/// </summary>
public class RondaSicurezza : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public TipoRonda Tipo { get; set; }
    public DateTime DataOra { get; set; } = DateTime.UtcNow;
    public string EseguitaDaUserId { get; set; } = string.Empty;
    public string EseguitaDaNome   { get; set; } = string.Empty;

    /// <summary>Checklist serializzata: ogni voce ha label + ok/ko + nota facoltativa.</summary>
    public List<RondaItem> Items { get; set; } = new();

    /// <summary>True se almeno un item è KO — utile per query rapide.</summary>
    public bool HaAnomalie { get; set; }

    /// <summary>Note generiche dell'operatore.</summary>
    public string? Note { get; set; }
}

public class RondaItem
{
    public string Label { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? Nota { get; set; }
}

public enum TipoRonda
{
    Apertura,
    Chiusura
}
