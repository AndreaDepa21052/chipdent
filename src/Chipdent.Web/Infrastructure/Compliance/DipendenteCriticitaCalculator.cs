using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Models;

namespace Chipdent.Web.Infrastructure.Compliance;

/// <summary>
/// Calcola le criticità mostrate nella striscia verticale della modale di
/// modifica rapida del dipendente. Ogni chip è cliccabile e salta al campo
/// corrispondente nel form.
/// </summary>
public static class DipendenteCriticitaCalculator
{
    public const int GiorniPreavvisoDocumenti = 60;
    public const int GiorniPreavvisoContratto = 30;
    public const int GiorniPreavvisoPeriodoProva = 15;

    public static (List<DipendenteCriticita> Critiche, List<DipendenteCriticita> Avvisi, int Completezza)
        Calcola(Dipendente d, IReadOnlyList<VisitaMedica> visite, DateTime oggi)
    {
        var critiche = new List<DipendenteCriticita>();
        var avvisi = new List<DipendenteCriticita>();

        // ── Identità ─────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.CodiceFiscale))
            critiche.Add(new("Codice fiscale", "🆔", "fld-CodiceFiscale", "sec-identita"));
        if (d.DataNascita is null)
            avvisi.Add(new("Data di nascita", "🎂", "fld-DataNascita", "sec-identita"));

        // ── Contatti ─────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.Email))
            critiche.Add(new("Email di lavoro", "✉️", "fld-Email", "sec-contatti"));
        if (string.IsNullOrWhiteSpace(d.Cellulare) && string.IsNullOrWhiteSpace(d.Telefono))
            avvisi.Add(new("Telefono/cellulare", "📞", "fld-Cellulare", "sec-contatti"));
        if (string.IsNullOrWhiteSpace(d.IndirizzoResidenza))
            avvisi.Add(new("Indirizzo di residenza", "🏠", "fld-IndirizzoResidenza", "sec-contatti"));

        // ── Ruolo & organizzazione ──────────────────────────────
        if (string.IsNullOrWhiteSpace(d.ClinicaId))
            critiche.Add(new("Sede non assegnata", "🏥", "fld-ClinicaId", "sec-contratto"));
        if (string.IsNullOrWhiteSpace(d.ManagerId) && d.Ruolo is not RuoloDipendente.Direzione)
            avvisi.Add(new("Manager diretto", "👤", "fld-ManagerId", "sec-ruolo"));
        if (string.IsNullOrWhiteSpace(d.TitoloStudio))
            avvisi.Add(new("Titolo di studio", "🎓", "fld-TitoloStudio", "sec-studi"));

        // ── Contratto ────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.LivelloContratto))
            avvisi.Add(new("Livello CCNL", "📋", "fld-LivelloContratto", "sec-contratto"));
        if (string.IsNullOrWhiteSpace(d.IBAN))
            avvisi.Add(new("IBAN per stipendio", "🏦", "fld-IBAN", "sec-contratto"));
        if (string.IsNullOrWhiteSpace(d.PinTimbratura))
            avvisi.Add(new("PIN timbratura", "🔢", "fld-PinTimbratura", "sec-contratto"));

        // Periodo di prova in scadenza
        if (d.DataFinePeriodoProva is DateTime fineProva && !d.IsCessato)
        {
            var giorni = (fineProva.Date - oggi.Date).TotalDays;
            if (giorni >= 0 && giorni <= GiorniPreavvisoPeriodoProva)
                avvisi.Add(new($"Fine periodo prova fra {giorni:0} gg", "⏳",
                    "fld-DataFinePeriodoProva", "sec-contratto"));
        }

        // Contratto TD scaduto/in scadenza (proroga se presente vince)
        var scadenzaTd = d.DataScadenzaProroga ?? d.DataScadenzaContratto;
        if (scadenzaTd is DateTime st && !d.IsCessato)
        {
            var giorni = (st.Date - oggi.Date).TotalDays;
            var fld = d.DataScadenzaProroga.HasValue ? "fld-DataScadenzaProroga" : "fld-DataScadenzaContratto";
            if (giorni < 0)
                critiche.Add(new($"Contratto TD scaduto ({Math.Abs(giorni):0} gg fa)", "📆", fld, "sec-contratto"));
            else if (giorni <= GiorniPreavvisoContratto)
                avvisi.Add(new($"Contratto TD scade fra {giorni:0} gg", "📆", fld, "sec-contratto"));
        }

        // ── Documenti d'identità ────────────────────────────────
        if (string.IsNullOrWhiteSpace(d.DocumentoNumero))
            avvisi.Add(new("Numero documento", "🪪", "fld-DocumentoNumero", "sec-documenti"));
        AggiungiAlertScadenza(d.ScadenzaCartaIdentita, "Carta d'identità", "🪪",
            "fld-ScadenzaCartaIdentita", "sec-documenti", oggi, critiche, avvisi);
        AggiungiAlertScadenza(d.ScadenzaTesseraSanitaria, "Tessera sanitaria", "🩺",
            "fld-ScadenzaTesseraSanitaria", "sec-documenti", oggi, critiche, avvisi);
        AggiungiAlertScadenza(d.ScadenzaPermessoSoggiorno, "Permesso di soggiorno", "📄",
            "fld-ScadenzaPermessoSoggiorno", "sec-documenti", oggi, critiche, avvisi);

        // Maternità
        if (d.InizioMaternita is not null)
        {
            AggiungiAlertScadenza(d.ScadenzaDocumentiMaternita, "Documenti maternità", "👶",
                "fld-ScadenzaDocumentiMaternita", "sec-eventi", oggi, critiche, avvisi);
        }

        // ── Visita medica (su entità separata) ───────────────────
        if (!d.IsCessato)
        {
            var ultimaVisita = visite
                .Where(v => v.Esito != EsitoVisita.NonIdoneo)
                .OrderByDescending(v => v.Data)
                .FirstOrDefault();
            if (ultimaVisita is null)
            {
                avvisi.Add(new("Visita medica mancante", "🩺",
                    "fld-VisitaMedica", "sec-contratto",
                    Tooltip: "Nessuna visita medica registrata in fascicolo."));
            }
            else if (ultimaVisita.ScadenzaIdoneita is DateTime sv)
            {
                var giorni = (sv.Date - oggi.Date).TotalDays;
                if (giorni < 0)
                    critiche.Add(new($"Visita medica scaduta ({Math.Abs(giorni):0} gg fa)", "🩺",
                        "fld-VisitaMedica", "sec-contratto",
                        Tooltip: $"Idoneità scaduta il {sv:dd/MM/yyyy}."));
                else if (giorni <= GiorniPreavvisoDocumenti)
                    avvisi.Add(new($"Visita medica fra {giorni:0} gg", "🩺",
                        "fld-VisitaMedica", "sec-contratto",
                        Tooltip: $"Idoneità in scadenza il {sv:dd/MM/yyyy}."));
            }
        }

        // ── Completezza % ────────────────────────────────────────
        var campiTotali = 14;
        var campiOk = 0;
        if (!string.IsNullOrWhiteSpace(d.Nome)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.Cognome)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.CodiceFiscale)) campiOk++;
        if (d.DataNascita is not null) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.Email)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.Cellulare) || !string.IsNullOrWhiteSpace(d.Telefono)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.IndirizzoResidenza)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.ClinicaId)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.LivelloContratto)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.IBAN)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.TitoloStudio)) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.DocumentoNumero)) campiOk++;
        if (d.ScadenzaCartaIdentita is not null) campiOk++;
        if (!string.IsNullOrWhiteSpace(d.PinTimbratura)) campiOk++;
        var completezza = (int)Math.Round(100.0 * campiOk / campiTotali);

        return (critiche, avvisi, completezza);
    }

    private static void AggiungiAlertScadenza(
        DateTime? scadenza, string etichetta, string icona,
        string fldId, string sectionId, DateTime oggi,
        List<DipendenteCriticita> critiche, List<DipendenteCriticita> avvisi)
    {
        if (scadenza is null) return;
        var giorni = (scadenza.Value.Date - oggi.Date).TotalDays;
        if (giorni < 0)
            critiche.Add(new($"{etichetta} scaduta", icona, fldId, sectionId,
                Tooltip: $"Scaduta il {scadenza:dd/MM/yyyy} ({Math.Abs(giorni):0} gg fa)."));
        else if (giorni <= GiorniPreavvisoDocumenti)
            avvisi.Add(new($"{etichetta} fra {giorni:0} gg", icona, fldId, sectionId,
                Tooltip: $"Scade il {scadenza:dd/MM/yyyy}."));
    }
}
