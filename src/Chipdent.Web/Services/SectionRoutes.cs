namespace Chipdent.Web.Services;

/// <summary>
/// Mappa tra controller MVC e gli slug delle sezioni del catalogo menu che essi
/// servono. Usata dall'authorization handler per capire a quale sezione appartiene
/// la richiesta corrente e, di conseguenza, se un grant per-utente la sblocca.
/// Allineata alla sectionMap del _Layout e a <see cref="MenuCatalog"/>.
/// </summary>
public static class SectionRoutes
{
    /// <summary>Slug → nome controller (senza suffisso "Controller").</summary>
    private static readonly Dictionary<string, string> SlugToController = new(StringComparer.OrdinalIgnoreCase)
    {
        // Operatività
        ["dashboard"]            = "Dashboard",
        ["turni"]                = "Turni",
        ["ferie"]                = "Ferie",
        ["cambio-turno"]         = "CambioTurno",
        ["sostituzioni"]         = "Sostituzioni",
        ["presenze"]             = "Presenze",
        ["dpi"]                  = "Dpi",
        ["miei-documenti"]       = "MieiDocumenti",
        ["mie-timbrature"]       = "MieTimbrature",
        ["segnalazioni"]         = "Segnalazioni",
        ["videoassistenza"]      = "Videoassistenza",
        ["chat"]                 = "Chat",
        ["comunicazioni"]        = "Comunicazioni",
        ["product-backlog"]      = "ProductBacklog",
        // Anagrafiche
        ["societa"]              = "Societa",
        ["cliniche"]             = "Cliniche",
        ["dottori"]              = "Dottori",
        ["dipendenti"]           = "Dipendenti",
        ["fornitori"]            = "Tesoreria",
        ["contratti"]            = "Contratti",
        ["formazione"]           = "Formazione",
        // Direzionale
        ["ai-insights"]          = "AiInsights",
        ["ottimizzazione"]       = "Ottimizzazione",
        ["benchmark"]            = "Benchmark",
        ["predizioni"]           = "Predizioni",
        ["feedback"]             = "Feedback",
        ["headcount"]            = "Headcount",
        ["report"]               = "Report",
        // Tesoreria
        ["tesoreria"]            = "Tesoreria",
        ["tesoreria-cashflow"]   = "Cashflow",
        ["tesoreria-distinte"]   = "Tesoreria",
        // Compliance
        ["rls"]                  = "Rls",
        ["documentazione"]       = "Documentazione",
        ["scadenziario"]         = "Scadenziario",
        ["calendario-interventi"]= "CalendarioInterventi",
        ["operations"]           = "Operations",
        ["audit"]                = "Audit",
        // Amministrazione
        ["users"]                = "Users",
        ["configurazione"]       = "Configurazione",
        ["whistleblowing"]       = "Whistleblowing",
    };

    /// <summary>Controller → slug delle sezioni che serve (inverso di SlugToController).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ControllerSections =
        SlugToController
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToArray(), StringComparer.OrdinalIgnoreCase);
}
