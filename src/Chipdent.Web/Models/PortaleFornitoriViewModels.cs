using System.ComponentModel.DataAnnotations;
using Chipdent.Web.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace Chipdent.Web.Models;

/// <summary>Dashboard del portale lato fornitore — vede solo i propri dati.</summary>
public class PortaleFornitoreDashboardViewModel
{
    public Fornitore Fornitore { get; set; } = null!;

    public decimal TotaleFatturatoYTD { get; set; }
    public decimal EspostoApertoTotale { get; set; }
    public int FattureInApprovazione { get; set; }
    public int ScadenzeProssime30 { get; set; }
    public int ScadenzeScadute { get; set; }

    public List<RigaScadenzaFornitore> ScadenzeAperte { get; set; } = new();
    public List<RigaScadenzaFornitore> ScadenzePassate { get; set; } = new();
    public List<RigaFatturaFornitore> Fatture { get; set; } = new();

    public Dictionary<string, string> ClinicheLookup { get; set; } = new();
}

public class RigaScadenzaFornitore
{
    public string Id { get; set; } = string.Empty;
    public DateTime DataScadenza { get; set; }
    public decimal Importo { get; set; }
    public StatoScadenza Stato { get; set; }
    public MetodoPagamento Metodo { get; set; }
    public string ClinicaNome { get; set; } = string.Empty;
    public string NumeroFattura { get; set; } = string.Empty;
    public DateTime? DataPagamento { get; set; }
    public DateTime? DataProgrammata { get; set; }
}

public class RigaFatturaFornitore
{
    public string Id { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public DateTime DataEmissione { get; set; }
    public decimal Totale { get; set; }
    public StatoFattura Stato { get; set; }
    public string? MotivoRifiuto { get; set; }
    public string ClinicaNome { get; set; } = string.Empty;
    public bool HasAllegato { get; set; }
}

/// <summary>Form di upload fattura dal portale fornitore.</summary>
public class FornitoreUploadFatturaViewModel
{
    [Required(ErrorMessage = "Seleziona la sede destinataria.")]
    public string ClinicaId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Numero fattura obbligatorio.")]
    public string Numero { get; set; } = string.Empty;

    [Required]
    public DateTime DataEmissione { get; set; } = DateTime.UtcNow.Date;

    [Required]
    public DateTime MeseCompetenza { get; set; } = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    public CategoriaSpesa Categoria { get; set; } = CategoriaSpesa.AltreSpeseFisse;

    [Range(0, double.MaxValue, ErrorMessage = "Imponibile non valido.")]
    public decimal Imponibile { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "IVA non valida.")]
    public decimal Iva { get; set; }

    [Required]
    public DateTime DataScadenza { get; set; } = DateTime.UtcNow.Date.AddDays(30);

    public MetodoPagamento Metodo { get; set; } = MetodoPagamento.Bonifico;
    public string? Note { get; set; }

    public IFormFile? Allegato { get; set; }

    public List<Clinica> Cliniche { get; set; } = new();

    public decimal Totale => Imponibile + Iva;
}

/// <summary>Form anagrafica self-service del fornitore.</summary>
public class FornitoreSelfAnagraficaViewModel
{
    [Required]
    public string RagioneSociale { get; set; } = string.Empty;

    public string? PartitaIva { get; set; }
    public string? CodiceFiscale { get; set; }
    public string? CodiceSdi { get; set; }
    public string? Pec { get; set; }

    [EmailAddress]
    public string? EmailContatto { get; set; }

    public string? Telefono { get; set; }
    public string? Indirizzo { get; set; }
    public string? Iban { get; set; }
}
