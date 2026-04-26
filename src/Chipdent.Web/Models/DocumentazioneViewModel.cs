using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Models;

public class DocumentazioneIndexViewModel
{
    public IReadOnlyList<ClinicaDocumentiGroup> Gruppi { get; set; } = Array.Empty<ClinicaDocumentiGroup>();
    public string? FilterClinicaId { get; set; }
    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}

public record ClinicaDocumentiGroup(string ClinicaId, string ClinicaNome, IReadOnlyList<DocumentoClinica> Documenti);

public class DocumentoFormViewModel
{
    public string? Id { get; set; }
    public string ClinicaId { get; set; } = string.Empty;
    public TipoDocumento Tipo { get; set; } = TipoDocumento.AutorizzazioneSanitaria;
    public string Titolo { get; set; } = string.Empty;
    public string? Numero { get; set; }
    public DateTime? DataEmissione { get; set; }
    public DateTime? DataScadenza { get; set; }
    public string? EnteEmittente { get; set; }
    public string? Note { get; set; }

    public IReadOnlyList<Clinica> Cliniche { get; set; } = Array.Empty<Clinica>();
}
