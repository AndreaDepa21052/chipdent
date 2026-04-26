using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Trasferimento : TenantEntity
{
    public string PersonaId { get; set; } = string.Empty;
    public TipoPersona PersonaTipo { get; set; }
    public string PersonaNome { get; set; } = string.Empty;

    public string? ClinicaDaId { get; set; }
    public string? ClinicaDaNome { get; set; }
    public string ClinicaAId { get; set; } = string.Empty;
    public string ClinicaANome { get; set; } = string.Empty;

    public DateTime DataEffetto { get; set; } = DateTime.UtcNow;
    public MotivoTrasferimento Motivo { get; set; } = MotivoTrasferimento.Riorganizzazione;
    public string? Note { get; set; }

    public string DecisoDaUserId { get; set; } = string.Empty;
    public string DecisoDaNome { get; set; } = string.Empty;
}

public enum MotivoTrasferimento
{
    TrasferimentoVolontario,
    Riorganizzazione,
    Copertura,
    Promozione,
    AperturaSede,
    Altro
}
