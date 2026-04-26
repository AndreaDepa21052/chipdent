using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class DottoreProfileViewModel
{
    public Dottore Dottore { get; set; } = new();
    public string? ClinicaPrincipaleNome { get; set; }
    public IReadOnlyList<Trasferimento> Storico { get; set; } = Array.Empty<Trasferimento>();
    public IReadOnlyList<AuditEntry> Audit { get; set; } = Array.Empty<AuditEntry>();
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string Tab { get; set; } = "anagrafica";
}

public class DipendenteProfileViewModel
{
    public Dipendente Dipendente { get; set; } = new();
    public string? ClinicaCorrenteNome { get; set; }
    public string? ManagerNome { get; set; }
    public IReadOnlyList<Trasferimento> Storico { get; set; } = Array.Empty<Trasferimento>();
    public IReadOnlyList<AuditEntry> Audit { get; set; } = Array.Empty<AuditEntry>();
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string Tab { get; set; } = "anagrafica";
}

public class TransferViewModel
{
    public string PersonaId { get; set; } = string.Empty;
    public TipoPersona PersonaTipo { get; set; }
    public string PersonaNome { get; set; } = string.Empty;
    public string? ClinicaAttualeId { get; set; }
    public string? ClinicaAttualeNome { get; set; }

    [Required]
    public string ClinicaAId { get; set; } = string.Empty;

    [Required]
    public DateTime DataEffetto { get; set; } = DateTime.Today;

    public MotivoTrasferimento Motivo { get; set; } = MotivoTrasferimento.Riorganizzazione;

    public string? Note { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public class DismissViewModel
{
    public string PersonaId { get; set; } = string.Empty;
    public TipoPersona PersonaTipo { get; set; }
    public string PersonaNome { get; set; } = string.Empty;

    [Required]
    public DateTime DataDimissioni { get; set; } = DateTime.Today;

    public string? Motivo { get; set; }
}
