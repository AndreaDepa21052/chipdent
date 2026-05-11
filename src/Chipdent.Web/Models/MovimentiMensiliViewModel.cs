using System.Globalization;

namespace Chipdent.Web.Models;

/// <summary>
/// Riepilogo mensile dei movimenti del personale per ogni clinica.
/// Sostituisce la compilazione manuale del file
/// "Elenco assunzioni_cessazioni_proroghe_distacchi.xlsx".
/// Le righe sono calcolate aggregando le operazioni registrate in piattaforma
/// (anagrafica dipendente, distacchi, trasferimenti, cambi livello/retribuzione,
/// cambi mansione/reparto).
/// </summary>
public class MovimentiMensiliReport
{
    public int Anno { get; set; }
    public int Mese { get; set; }
    public IReadOnlyList<MovimentoMensileRiga> Righe { get; set; } = Array.Empty<MovimentoMensileRiga>();

    public string MeseAnnoLabel
    {
        get
        {
            var ci = CultureInfo.GetCultureInfo("it-IT");
            var nome = ci.DateTimeFormat.GetMonthName(Mese);
            return $"{nome} {Anno}";
        }
    }

    public int TotaleAssunzioni => Righe.Sum(r => r.NumeroAssunzioni);
    public int TotaleCessazioni => Righe.Sum(r => r.NumeroCessazioniAnticipate + r.NumeroContrattiNonRinnovati);
    public int TotaleProroghe => Righe.Sum(r => r.NumeroProroghe);
    public int TotaleDistacchi => Righe.Sum(r => r.NumeroDistacchi);
    public int TotaleTrasformazioni => Righe.Sum(r => r.NumeroTrasformazioniLivello);
    public int TotaleTrasferimenti => Righe.Sum(r => r.NumeroTrasferimentiSede);
    public int TotaleCambiMansione => Righe.Sum(r => r.NumeroCambiMansione);
}

public class MovimentoMensileRiga
{
    public string ClinicaId { get; set; } = string.Empty;
    public string ClinicaNome { get; set; } = string.Empty;

    public int NumeroAssunzioni { get; set; }
    public int NumeroAnnullamentiAssunzione { get; set; }
    public int NumeroCessazioniAnticipate { get; set; }
    public int NumeroContrattiNonRinnovati { get; set; }
    public int NumeroContrattiNonRinnovatiProssimoMese { get; set; }
    public int NumeroProroghe { get; set; }
    public int NumeroDistacchi { get; set; }
    public int NumeroRettificheDistacchi { get; set; }
    public int NumeroTrasformazioniLivello { get; set; }

    /// <summary>Trasferimenti permanenti DA un'altra sede VERSO questa clinica nel mese (esclusa l'assegnazione iniziale).</summary>
    public int NumeroTrasferimentiSede { get; set; }

    /// <summary>Cambi di mansione/ruolo/reparto registrati nel mese per dipendenti di questa clinica.</summary>
    public int NumeroCambiMansione { get; set; }

    public IReadOnlyList<string> Note { get; set; } = Array.Empty<string>();
}
