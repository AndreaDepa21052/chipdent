using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Models;

namespace Chipdent.Web.Infrastructure.Compliance;

/// <summary>
/// Calcola gli alert di compliance per un dottore (RC professionale,
/// scadenza documenti, ECM sotto soglia).
/// </summary>
public static class DottoreAlertCalculator
{
    /// <summary>Soglia in giorni entro cui una scadenza è considerata "in avviso".</summary>
    public const int GiorniPreavviso = 60;

    public static List<DottoreAlert> Calcola(
        Dottore dottore,
        IReadOnlyList<DocumentoDottore> documenti,
        IReadOnlyList<AttestatoEcm> attestati,
        DateTime oggi)
    {
        var result = new List<DottoreAlert>();

        // RC professionale: ne possono esistere più (rinnovi); prendo la più recente.
        var rcDocs = documenti
            .Where(d => d.Tipo == TipoDocumentoDottore.RcProfessionale)
            .OrderByDescending(d => d.Scadenza ?? DateTime.MinValue)
            .ToList();

        if (rcDocs.Count == 0)
        {
            result.Add(new DottoreAlert(
                "RC professionale mancante",
                "Nessuna polizza RC professionale registrata in fascicolo.",
                AlertLivello.Critico,
                Scadenza: null,
                Categoria: "rc"));
        }
        else
        {
            var rc = rcDocs.First();
            var alert = ValutaScadenzaDocumento(rc, oggi, "RC professionale", "rc");
            if (alert is not null) result.Add(alert);
        }

        foreach (var d in documenti.Where(d => d.Tipo != TipoDocumentoDottore.RcProfessionale))
        {
            var label = EtichettaDocumento(d);
            var alert = ValutaScadenzaDocumento(d, oggi, label, "documento");
            if (alert is not null) result.Add(alert);
        }

        // ECM: alert se i crediti acquisiti nel triennio sono sotto soglia.
        if (dottore.AnnoFineTriennioEcm is int annoFine && annoFine > 0)
        {
            var annoInizio = annoFine - 2;
            var creditiAcquisiti = attestati
                .Where(a => a.AnnoRiferimento >= annoInizio && a.AnnoRiferimento <= annoFine)
                .Sum(a => a.CreditiEcm);
            var richiesti = dottore.CreditiEcmRichiestiTriennio;
            var giorniAllaFine = (new DateTime(annoFine, 12, 31) - oggi).TotalDays;
            if (creditiAcquisiti < richiesti)
            {
                var mancanti = richiesti - creditiAcquisiti;
                var livello = giorniAllaFine < 180 ? AlertLivello.Critico : AlertLivello.Avviso;
                result.Add(new DottoreAlert(
                    "ECM sotto soglia",
                    $"Mancano {mancanti:0.#} crediti ECM al completamento del triennio {annoInizio}-{annoFine} ({creditiAcquisiti:0.#}/{richiesti}).",
                    livello,
                    new DateTime(annoFine, 12, 31),
                    Categoria: "ecm"));
            }
        }

        return result
            .OrderByDescending(a => a.Livello)
            .ThenBy(a => a.Scadenza ?? DateTime.MaxValue)
            .ToList();
    }

    private static DottoreAlert? ValutaScadenzaDocumento(DocumentoDottore d, DateTime oggi, string titolo, string categoria)
    {
        if (d.Scadenza is null) return null;
        var giorni = (d.Scadenza.Value.Date - oggi.Date).TotalDays;
        if (giorni < 0)
        {
            return new DottoreAlert(
                $"{titolo} scaduto",
                $"Scaduto il {d.Scadenza:dd/MM/yyyy} ({Math.Abs(giorni):0} giorni fa).",
                AlertLivello.Critico,
                d.Scadenza,
                DocumentoId: d.Id,
                Categoria: categoria);
        }
        if (giorni <= GiorniPreavviso)
        {
            return new DottoreAlert(
                $"{titolo} in scadenza",
                $"Scade il {d.Scadenza:dd/MM/yyyy} (fra {giorni:0} giorni).",
                AlertLivello.Avviso,
                d.Scadenza,
                DocumentoId: d.Id,
                Categoria: categoria);
        }
        return null;
    }

    private static string EtichettaDocumento(DocumentoDottore d) =>
        !string.IsNullOrWhiteSpace(d.EtichettaLibera)
            ? d.EtichettaLibera!
            : d.Tipo switch
            {
                TipoDocumentoDottore.RcProfessionale => "RC professionale",
                TipoDocumentoDottore.CartaIdentita   => "Carta d'identità",
                TipoDocumentoDottore.CodiceFiscale   => "Codice fiscale",
                TipoDocumentoDottore.PartitaIVA      => "Partita IVA",
                TipoDocumentoDottore.IscrizioneAlbo  => "Iscrizione albo",
                TipoDocumentoDottore.Diploma         => "Diploma",
                TipoDocumentoDottore.Specializzazione => "Specializzazione",
                TipoDocumentoDottore.Curriculum      => "Curriculum",
                _ => "Documento"
            };
}
