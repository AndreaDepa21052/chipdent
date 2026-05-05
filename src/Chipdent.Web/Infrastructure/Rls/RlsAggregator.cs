using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Infrastructure.Rls;

/// <summary>
/// Stato compliance di una nomina/corso/visita rispetto alla data odierna.
/// </summary>
public enum StatoCompliance
{
    InRegola,
    InScadenza,
    Scaduto,
    Mancante
}

/// <summary>Singola nomina attiva (ultima registrata) di una persona o sede per una tipologia di corso.</summary>
public record NominaItem(
    DestinatarioCorso DestinatarioTipo,
    string DestinatarioId,
    string Nome,
    string Ruolo,
    string? ClinicaId,
    string ClinicaNome,
    DateTime? DataConseguimento,
    DateTime? Scadenza,
    string? VerbaleNomina,
    StatoCompliance Stato);

/// <summary>Aggregato per tipologia di nomina (RLS / Antincendio / Primo soccorso ecc.).</summary>
public class NomineGroup
{
    public TipoCorso Tipo { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public IReadOnlyList<NominaItem> Items { get; init; } = Array.Empty<NominaItem>();
    public IReadOnlyList<string> ClinicheSenzaCopertura { get; init; } = Array.Empty<string>();

    public int Totali => Items.Count;
    public int InScadenza => Items.Count(i => i.Stato == StatoCompliance.InScadenza);
    public int Scaduti => Items.Count(i => i.Stato == StatoCompliance.Scaduto);
    public int InRegola => Items.Count(i => i.Stato == StatoCompliance.InRegola);
}

/// <summary>Riga corso in scadenza/scaduto, normalizzata per visualizzazione.</summary>
public record CorsoScadenzaItem(
    TipoCorso Tipo,
    string PersonaNome,
    string Ruolo,
    string ClinicaNome,
    DateTime? Scadenza,
    StatoCompliance Stato);

/// <summary>Aggregato di corsi in scadenza/scaduti raggruppato per tipologia.</summary>
public class CorsiPerTipoGroup
{
    public TipoCorso Tipo { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public IReadOnlyList<CorsoScadenzaItem> Items { get; init; } = Array.Empty<CorsoScadenzaItem>();

    public int InScadenza => Items.Count(i => i.Stato == StatoCompliance.InScadenza);
    public int Scaduti => Items.Count(i => i.Stato == StatoCompliance.Scaduto);
    public int Totali => Items.Count;
}

public static class RlsAggregator
{
    /// <summary>Tipologie di "nomina" — corsi che identificano un addetto designato (RLS, Antincendio, Primo soccorso).</summary>
    public static readonly TipoCorso[] TipiNomina =
    {
        TipoCorso.RLS,
        TipoCorso.Antincendio,
        TipoCorso.PrimoSoccorso
    };

    public static (string Label, string Icon) Meta(TipoCorso t) => t switch
    {
        TipoCorso.RLS => ("RLS", "🛡"),
        TipoCorso.Antincendio => ("Addetto antincendio", "🔥"),
        TipoCorso.PrimoSoccorso => ("Addetto primo soccorso", "🚑"),
        TipoCorso.RSPP => ("RSPP", "🦺"),
        TipoCorso.Privacy => ("Privacy / GDPR", "🔒"),
        TipoCorso.Sicurezza81_08 => ("Sicurezza 81/08", "📜"),
        TipoCorso.Radioprotezione => ("Radioprotezione", "☢️"),
        TipoCorso.Anticorruzione => ("Anticorruzione", "⚖️"),
        TipoCorso.FormazioneGeneraleSicurezza => ("Formazione generale sicurezza", "📚"),
        TipoCorso.FormazioneSpecificaRischioBasso => ("Formazione specifica rischio basso", "📘"),
        TipoCorso.FormazioneSpecificaRischioAltoASO => ("Formazione specifica rischio alto ASO", "📕"),
        TipoCorso.AggiornamentoASO10H => ("Aggiornamento ASO 10H", "🔁"),
        _ => ("Altro corso", "📄")
    };

    public static StatoCompliance ComputeStato(DateTime? scadenza, DateTime now, DateTime soon)
    {
        if (!scadenza.HasValue) return StatoCompliance.Mancante;
        if (scadenza.Value < now) return StatoCompliance.Scaduto;
        if (scadenza.Value < soon) return StatoCompliance.InScadenza;
        return StatoCompliance.InRegola;
    }

    /// <summary>
    /// Per ogni tipologia di nomina, ritorna l'elenco delle persone che la ricoprono attualmente
    /// (ultima registrazione vinta per data conseguimento). Gli stati Scaduto/InScadenza/InRegola
    /// dipendono dalla scadenza del corso e dalla soglia <paramref name="soon"/>.
    /// </summary>
    public static IReadOnlyList<NomineGroup> Nomine(
        IReadOnlyList<Corso> corsi,
        IReadOnlyDictionary<string, Dipendente> dipendenti,
        IReadOnlyDictionary<string, Dottore> dottori,
        IReadOnlyDictionary<string, Clinica> cliniche,
        DateTime now,
        DateTime soon,
        string? clinicaFilter = null)
    {
        var groups = new List<NomineGroup>();
        foreach (var tipo in TipiNomina)
        {
            var ultimePerPersona = corsi
                .Where(c => c.Tipo == tipo)
                .GroupBy(c => (c.DestinatarioTipo, c.DestinatarioId))
                .Select(g => g.OrderByDescending(c => c.DataConseguimento).First());

            var items = new List<NominaItem>();
            foreach (var c in ultimePerPersona)
            {
                if (!TryResolveDestinatario(c, dipendenti, dottori, cliniche,
                        out var nome, out var ruolo, out var clinicaId, out var clinicaNome))
                {
                    continue;
                }
                if (clinicaFilter is not null && clinicaId != clinicaFilter) continue;

                items.Add(new NominaItem(
                    c.DestinatarioTipo,
                    c.DestinatarioId,
                    nome,
                    ruolo,
                    clinicaId,
                    clinicaNome,
                    c.DataConseguimento,
                    c.Scadenza,
                    c.VerbaleNomina,
                    ComputeStato(c.Scadenza, now, soon)));
            }

            // Cliniche scoperte: nessun addetto attivo (escludendo gli scaduti) per la tipologia.
            var clinicheCoperte = items
                .Where(i => i.Stato != StatoCompliance.Scaduto && i.Stato != StatoCompliance.Mancante)
                .Select(i => i.ClinicaId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet();
            var clinichePool = clinicaFilter is null
                ? cliniche.Values.Where(c => c.Stato != ClinicaStato.Chiusa && !c.IsHolding)
                : cliniche.Values.Where(c => c.Id == clinicaFilter);
            var scoperte = clinichePool
                .Where(c => !clinicheCoperte.Contains(c.Id))
                .OrderBy(c => c.Nome)
                .Select(c => c.Nome)
                .ToList();

            var (label, icon) = Meta(tipo);
            groups.Add(new NomineGroup
            {
                Tipo = tipo,
                Label = label,
                Icon = icon,
                Items = items.OrderBy(i => i.ClinicaNome).ThenBy(i => i.Nome).ToList(),
                ClinicheSenzaCopertura = scoperte
            });
        }
        return groups;
    }

    /// <summary>
    /// Corsi raggruppati per tipologia, mostrando solo le righe in scadenza o scadute
    /// (rispetto a <paramref name="soon"/>). Una persona con più registrazioni dello stesso
    /// tipo viene considerata sull'ultima registrazione (la più recente per DataConseguimento).
    /// </summary>
    public static IReadOnlyList<CorsiPerTipoGroup> CorsiInScadenzaPerTipo(
        IReadOnlyList<Corso> corsi,
        IReadOnlyDictionary<string, Dipendente> dipendenti,
        IReadOnlyDictionary<string, Dottore> dottori,
        IReadOnlyDictionary<string, Clinica> cliniche,
        DateTime now,
        DateTime soon,
        string? clinicaFilter = null)
    {
        var ultimePerPersonaTipo = corsi
            .GroupBy(c => (c.Tipo, c.DestinatarioTipo, c.DestinatarioId))
            .Select(g => g.OrderByDescending(c => c.DataConseguimento).First());

        var groups = new Dictionary<TipoCorso, List<CorsoScadenzaItem>>();
        foreach (var c in ultimePerPersonaTipo)
        {
            var stato = ComputeStato(c.Scadenza, now, soon);
            if (stato is not (StatoCompliance.InScadenza or StatoCompliance.Scaduto)) continue;

            if (!TryResolveDestinatario(c, dipendenti, dottori, cliniche,
                    out var nome, out var ruolo, out var clinicaId, out var clinicaNome))
            {
                continue;
            }
            if (clinicaFilter is not null && clinicaId != clinicaFilter) continue;

            if (!groups.TryGetValue(c.Tipo, out var list))
            {
                list = new List<CorsoScadenzaItem>();
                groups[c.Tipo] = list;
            }
            list.Add(new CorsoScadenzaItem(c.Tipo, nome, ruolo, clinicaNome, c.Scadenza, stato));
        }

        return groups
            .OrderBy(g => g.Key.ToString())
            .Select(g =>
            {
                var (label, icon) = Meta(g.Key);
                return new CorsiPerTipoGroup
                {
                    Tipo = g.Key,
                    Label = label,
                    Icon = icon,
                    Items = g.Value
                        .OrderBy(i => i.Stato == StatoCompliance.Scaduto ? 0 : 1)
                        .ThenBy(i => i.Scadenza ?? DateTime.MaxValue)
                        .ToList()
                };
            })
            .ToList();
    }

    /// <summary>
    /// Map dipendenteId → set di tipologie di nomina attive (non scadute).
    /// Usata dalla pagina Dipendenti/Index per mostrare i badge.
    /// </summary>
    public static IReadOnlyDictionary<string, HashSet<TipoCorso>> NomineAttivePerDipendente(
        IReadOnlyList<Corso> corsi,
        DateTime now)
    {
        var result = new Dictionary<string, HashSet<TipoCorso>>();
        var ultimi = corsi
            .Where(c => c.DestinatarioTipo == DestinatarioCorso.Dipendente
                        && TipiNomina.Contains(c.Tipo))
            .GroupBy(c => (c.Tipo, c.DestinatarioId))
            .Select(g => g.OrderByDescending(c => c.DataConseguimento).First());

        foreach (var c in ultimi)
        {
            if (c.Scadenza.HasValue && c.Scadenza.Value < now) continue;
            if (!result.TryGetValue(c.DestinatarioId, out var set))
            {
                set = new HashSet<TipoCorso>();
                result[c.DestinatarioId] = set;
            }
            set.Add(c.Tipo);
        }
        return result;
    }

    private static bool TryResolveDestinatario(
        Corso c,
        IReadOnlyDictionary<string, Dipendente> dipendenti,
        IReadOnlyDictionary<string, Dottore> dottori,
        IReadOnlyDictionary<string, Clinica> cliniche,
        out string nome,
        out string ruolo,
        out string? clinicaId,
        out string clinicaNome)
    {
        nome = ruolo = string.Empty;
        clinicaId = null;
        clinicaNome = "—";

        switch (c.DestinatarioTipo)
        {
            case DestinatarioCorso.Dipendente:
                if (!dipendenti.TryGetValue(c.DestinatarioId, out var d) || d.IsCessato) return false;
                nome = d.NomeCompleto;
                ruolo = d.Ruolo.ToString();
                clinicaId = d.ClinicaId;
                clinicaNome = cliniche.TryGetValue(d.ClinicaId, out var cl) ? cl.Nome : "—";
                return true;

            case DestinatarioCorso.Dottore:
                if (!dottori.TryGetValue(c.DestinatarioId, out var dr) || dr.IsCessato) return false;
                nome = dr.NomeCompleto;
                ruolo = string.IsNullOrWhiteSpace(dr.Specializzazione) ? "Dottore" : dr.Specializzazione;
                clinicaId = dr.ClinicaPrincipaleId;
                clinicaNome = !string.IsNullOrEmpty(dr.ClinicaPrincipaleId)
                              && cliniche.TryGetValue(dr.ClinicaPrincipaleId, out var cl2)
                    ? cl2.Nome : "—";
                return true;

            case DestinatarioCorso.Clinica:
                if (!cliniche.TryGetValue(c.DestinatarioId, out var cli)) return false;
                nome = cli.Nome;
                ruolo = "Sede";
                clinicaId = cli.Id;
                clinicaNome = cli.Nome;
                return true;

            default:
                return false;
        }
    }
}
