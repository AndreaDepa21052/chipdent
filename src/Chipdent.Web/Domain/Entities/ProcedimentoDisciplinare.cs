using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Procedimento disciplinare aperto verso un dipendente. Tiene traccia dei
/// quattro step canonici della procedura (lettera, lettera firmata, risposta
/// del dipendente, conclusione) con allegati e date.
/// </summary>
public class ProcedimentoDisciplinare : TenantEntity
{
    public string DipendenteId { get; set; } = string.Empty;
    public string DipendenteNome { get; set; } = string.Empty;

    /// <summary>Oggetto sintetico del procedimento (es. "Ritardo reiterato").</summary>
    public string Oggetto { get; set; } = string.Empty;

    /// <summary>Data di apertura del procedimento (riferimento episodio).</summary>
    public DateTime DataApertura { get; set; } = DateTime.UtcNow;

    public StatoProcedimento Stato { get; set; } = StatoProcedimento.Aperto;

    public string? Note { get; set; }

    // Step 1: lettera disciplinare emessa
    public DateTime? Step1LetteraData { get; set; }
    public string? Step1AllegatoNome { get; set; }
    public string? Step1AllegatoPath { get; set; }
    public long? Step1AllegatoSize { get; set; }
    public string? Step1Note { get; set; }

    // Step 2: lettera disciplinare firmata
    public DateTime? Step2FirmataData { get; set; }
    public string? Step2AllegatoNome { get; set; }
    public string? Step2AllegatoPath { get; set; }
    public long? Step2AllegatoSize { get; set; }
    public string? Step2Note { get; set; }

    // Step 3: risposta del dipendente (giustificazioni)
    public DateTime? Step3RispostaData { get; set; }
    public string? Step3AllegatoNome { get; set; }
    public string? Step3AllegatoPath { get; set; }
    public long? Step3AllegatoSize { get; set; }
    public string? Step3Note { get; set; }

    // Step 4: conclusione del procedimento
    public DateTime? Step4ConclusioneData { get; set; }
    public EsitoProcedimento? Step4Esito { get; set; }
    public string? Step4AllegatoNome { get; set; }
    public string? Step4AllegatoPath { get; set; }
    public long? Step4AllegatoSize { get; set; }
    public string? Step4Note { get; set; }

    public int StepCorrente
    {
        get
        {
            if (Step4ConclusioneData.HasValue) return 4;
            if (Step3RispostaData.HasValue) return 3;
            if (Step2FirmataData.HasValue) return 2;
            if (Step1LetteraData.HasValue) return 1;
            return 0;
        }
    }
}

public enum StatoProcedimento
{
    Aperto,
    InCorso,
    Concluso,
    Archiviato
}

public enum EsitoProcedimento
{
    Richiamo,
    Sospensione,
    Multa,
    Licenziamento,
    Archiviazione,
    Altro
}
