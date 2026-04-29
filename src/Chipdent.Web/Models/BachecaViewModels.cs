namespace Chipdent.Web.Models;

public class BachecaViewModel
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string TenantNome { get; set; } = string.Empty;
    public string ClinicaId { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;
    public string? ClinicaCitta { get; set; }
    public IReadOnlyList<BachecaTurnoRow> Turni { get; set; } = Array.Empty<BachecaTurnoRow>();
    public IReadOnlyList<BachecaComunicazioneRow> Comunicazioni { get; set; } = Array.Empty<BachecaComunicazioneRow>();
}

public record BachecaTurnoRow(TimeSpan From, TimeSpan To, string Nome, string Ruolo);

public record BachecaComunicazioneRow(string Categoria, string Oggetto, string Anteprima, DateTime Quando);
