using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

public class Clinica : TenantEntity
{
    public string Nome { get; set; } = string.Empty;

    /// <summary>
    /// Nome abbreviato (es. "MI7", "BUS", "CCH") usato come LOC nello scadenziario.
    /// Se null/empty si applica un fallback derivativo dal Nome (vedi
    /// <c>TesoreriaController.SiglaSede</c>). Lo <c>ScadenziarioGenerator</c>
    /// usa questo campo sia in <b>scrittura</b> (popolamento del campo LOC delle scadenze
    /// generate, via SiglaSede) sia in <b>lettura</b> (matching della clinica destinataria
    /// quando l'import contiene una sigla nel nome fornitore o nella sezione).
    /// </summary>
    public string? NomeAbbreviato { get; set; }

    /// <summary>
    /// Società (persona giuridica) a cui appartiene la clinica. Null = non
    /// ancora associata. I pagamenti delle scadenze della clinica seguono
    /// l'IBAN della società (vedi <see cref="Societa.Iban"/>).
    /// </summary>
    public string? SocietaId { get; set; }

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

    /// <summary>Note libere sulla clinica (testo descrittivo, libero formato).</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Quando true, durante la generazione delle scadenze viene aggiunta
    /// automaticamente una nota secondaria valorizzata con
    /// <see cref="NotaSecondariaAutomatica"/> alla <c>ScadenzaPagamento.Note</c>
    /// (separata dal contenuto eventuale con " · "). Default false.
    /// Usato per veicolare istruzioni operative ricorrenti (es. "ATTESA OK
    /// DIREZIONE", "Verificare con segreteria", "Compensa con NC mensile")
    /// senza doverle scrivere manualmente su ogni scadenza della sede.
    /// </summary>
    public bool AggiungiNotaSecondariaAutomaticamente { get; set; }

    /// <summary>Testo della nota secondaria automatica. Letto solo se
    /// <see cref="AggiungiNotaSecondariaAutomaticamente"/> è true.</summary>
    public string? NotaSecondariaAutomatica { get; set; }
}

public enum ClinicaStato
{
    Operativa,
    InApertura,
    Chiusa
}
