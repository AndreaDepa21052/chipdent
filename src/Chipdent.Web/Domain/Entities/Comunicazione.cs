using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Comunicazione : TenantEntity
{
    public string MittenteUserId { get; set; } = string.Empty;
    public string MittenteNome { get; set; } = string.Empty;
    public CategoriaComunicazione Categoria { get; set; } = CategoriaComunicazione.Generico;
    public string Oggetto { get; set; } = string.Empty;
    public string Corpo { get; set; } = string.Empty;
    public string? ClinicaId { get; set; }
    public List<string> LettaDaUserIds { get; set; } = new();
    public StatoRichiesta Stato { get; set; } = StatoRichiesta.NonApplicabile;
    public string? GestitaDaUserId { get; set; }
    public DateTime? GestitaIl { get; set; }

    /// <summary>True se la comunicazione richiede una conferma di lettura esplicita dei destinatari.</summary>
    public bool RichiedeConferma { get; set; }

    /// <summary>Numero totale di destinatari per questa comunicazione (snapshot al momento dell'invio).</summary>
    public int TotaleDestinatari { get; set; }
}

public enum CategoriaComunicazione
{
    Generico,
    Annuncio,
    RichiestaFerie,
    RichiestaPermesso,
    Segnalazione,
    UrgenzaOperativa
}

public enum StatoRichiesta
{
    NonApplicabile,
    InAttesa,
    Approvata,
    Rifiutata
}
