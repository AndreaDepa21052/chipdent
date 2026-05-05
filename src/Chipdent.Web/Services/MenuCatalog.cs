using Chipdent.Web.Infrastructure.Identity;

namespace Chipdent.Web.Services;

/// <summary>
/// Catalogo statico delle sezioni della sidebar, raggruppate come nel layout.
/// Usato dal pannello PlatformAdmin per renderizzare la matrice di visibilità
/// e dal layout stesso per filtrare gli item.
/// </summary>
public static class MenuCatalog
{
    public record Section(string Slug, string Label);

    public record Group(string Key, string Label, IReadOnlyList<Section> Sections);

    /// <summary>
    /// Ruoli per cui il PlatformAdmin può configurare i menu.
    /// PlatformAdmin non è incluso: vede sempre tutto.
    /// Fornitore non è incluso: ha un portale dedicato.
    /// </summary>
    public static readonly IReadOnlyList<string> ConfigurableRoles = new[]
    {
        Policies.Names.Owner,
        Policies.Names.Management,
        Policies.Names.Direttore,
        Policies.Names.Backoffice,
        Policies.Names.Staff
    };

    public static readonly IReadOnlyList<Group> Groups = new List<Group>
    {
        new("operativita", "Operatività", new List<Section>
        {
            new("dashboard",        "Dashboard"),
            new("turni",            "Turni"),
            new("ferie",            "Ferie"),
            new("cambio-turno",     "Cambio turno"),
            new("sostituzioni",     "Sostituzioni"),
            new("presenze",         "Presenze"),
            new("dpi",              "DPI"),
            new("miei-documenti",   "I miei documenti"),
            new("mie-timbrature",   "Le mie timbrature"),
            new("segnalazioni",     "Segnalazioni"),
            new("videoassistenza",  "Videoassistenza"),
            new("chat",             "Chat"),
            new("comunicazioni",    "Comunicazioni"),
            new("product-backlog",  "Backlog di prodotto"),
        }),
        new("anagrafiche", "Anagrafiche", new List<Section>
        {
            new("cliniche",     "Cliniche"),
            new("dottori",      "Dottori"),
            new("dipendenti",   "Dipendenti"),
            new("fornitori",    "Fornitori"),
            new("contratti",    "Contratti"),
            new("formazione",   "Formazione & ECM"),
        }),
        new("direzionale", "Direzionale", new List<Section>
        {
            new("ai-insights",     "AI Insights"),
            new("ottimizzazione",  "Ottimizzazione AI"),
            new("benchmark",       "Benchmark sedi"),
            new("predizioni",      "Predizione assenze"),
            new("feedback",        "Feedback pazienti"),
            new("headcount",       "Headcount"),
            new("report",          "Report"),
        }),
        new("tesoreria", "Tesoreria", new List<Section>
        {
            new("tesoreria",            "Scadenziario"),
            new("tesoreria-cashflow",   "Cashflow"),
            new("tesoreria-distinte",   "Distinte SEPA"),
        }),
        new("compliance", "Compliance", new List<Section>
        {
            new("rls",                       "RLS / Sicurezza"),
            new("documentazione",            "Documentazione"),
            new("scadenziario",              "Scadenziario"),
            new("calendario-interventi",     "Calendario interventi"),
            new("operations",                "Operations"),
            new("audit",                     "Audit log"),
        }),
        new("amministrazione", "Amministrazione", new List<Section>
        {
            new("users",            "Utenti"),
            new("configurazione",   "Configurazione"),
            new("whistleblowing",   "Whistleblowing"),
        }),
    };

    public static IEnumerable<Section> AllSections =>
        Groups.SelectMany(g => g.Sections);

    public static string LabelOf(string slug) =>
        AllSections.FirstOrDefault(s => s.Slug == slug)?.Label ?? slug;
}
