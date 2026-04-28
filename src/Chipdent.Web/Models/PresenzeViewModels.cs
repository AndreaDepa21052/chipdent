using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class PresenzeIndexViewModel
{
    public DateTime Mese { get; set; } = DateTime.Today;
    public IReadOnlyList<DipendentePresenzeRow> Righe { get; set; } = Array.Empty<DipendentePresenzeRow>();
    public IReadOnlyList<TimbraturaRow> UltimeTimbrature { get; set; } = Array.Empty<TimbraturaRow>();
    public bool CanManage { get; set; }
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
    public string? FilterClinicaId { get; set; }
}

public record DipendentePresenzeRow(
    string DipendenteId,
    string Nome,
    string ClinicaNome,
    int OrePianificate,
    int OreLavorate,
    int Ritardi,
    int UsciteAnticipate,
    int GiorniLavorati,
    int GiorniPianificati,
    int OrePausa = 0,
    int SaldoOre = 0,
    int GiorniInRemoto = 0);

public record TimbraturaRow(Timbratura T, string DipendenteNome);

public class TimbraManualeViewModel
{
    [Required] public string DipendenteId { get; set; } = string.Empty;
    [Required] public TipoTimbratura Tipo { get; set; } = TipoTimbratura.CheckIn;
    [Required, DataType(DataType.DateTime)] public DateTime Quando { get; set; } = DateTime.Now;
    public string? Note { get; set; }
    public IReadOnlyList<Dipendente> Dipendenti { get; set; } = Array.Empty<Dipendente>();
}

public class PinSetupViewModel
{
    [Required] public string DipendenteId { get; set; } = string.Empty;
    [Required, RegularExpression(@"^\d{4,6}$", ErrorMessage = "Il PIN deve essere di 4-6 cifre")]
    public string Pin { get; set; } = string.Empty;
}
