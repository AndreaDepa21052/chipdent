using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Domain.Common;

/// <summary>
/// Etichette IT per gli enum tesoreria, allineate al file scadenziario di Confident.
/// Tenute qui per evitare di sporcare gli enum con [Display] attributes.
/// </summary>
public static class TesoreriaLabels
{
    public static string Label(this CategoriaSpesa c) => c switch
    {
        CategoriaSpesa.Acqua                    => "ACQUA",
        CategoriaSpesa.Energia                  => "ENERGIA",
        CategoriaSpesa.EnergiaElettrica         => "ENERGIA ELETTRICA",
        CategoriaSpesa.Gas                      => "GAS",
        CategoriaSpesa.Telefonia                => "TELEFONIA",
        CategoriaSpesa.Affitto                  => "AFFITTO",
        CategoriaSpesa.Locazione                => "LOCAZIONE",
        CategoriaSpesa.SpeseCondominiali        => "SPESE CONDOMINIALI",
        CategoriaSpesa.Cancelleria              => "CANCELLERIA",
        CategoriaSpesa.MaterialiClinici         => "MATERIALI CLINICI",
        CategoriaSpesa.MaterialeMedico          => "MATERIALE MEDICO",
        CategoriaSpesa.Manutenzione             => "MANUTENZIONE",
        CategoriaSpesa.Pulizie                  => "PULIZIE",
        CategoriaSpesa.ServizioPulizia          => "SERVIZIO PULIZIA",
        CategoriaSpesa.Consulenze               => "CONSULENZE",
        CategoriaSpesa.Marketing                => "MARKETING",
        CategoriaSpesa.CanoneMarketing          => "CANONE MARKETING",
        CategoriaSpesa.Trasporti                => "TRASPORTI",
        CategoriaSpesa.Assicurazione            => "ASSICURAZIONE",
        CategoriaSpesa.Leasing                  => "LEASING",
        CategoriaSpesa.Software                 => "SOFTWARE",
        CategoriaSpesa.It                       => "IT",
        CategoriaSpesa.NoleggioIt               => "NOLEGGIO IT",
        CategoriaSpesa.Laboratorio              => "LABORATORIO",
        CategoriaSpesa.Royalties                => "ROYALTIES",
        CategoriaSpesa.EntranceFee              => "ENTRANCE FEE",
        CategoriaSpesa.DueDiligence             => "DUE DILIGENCE",
        CategoriaSpesa.FinanziamentiPassivi     => "FINANZIAMENTI PASSIVI",
        CategoriaSpesa.OneriFinanziari          => "ONERI FINANZIARI",
        CategoriaSpesa.FondoInvestimento        => "FONDO INVESTIMENTO",
        CategoriaSpesa.ImposteTasse             => "IMPOSTE E TASSE",
        CategoriaSpesa.DirezioneSanitaria       => "DIREZIONE SANITARIA",
        CategoriaSpesa.Medici                   => "MEDICI",
        CategoriaSpesa.CompensoAmministratore   => "COMPENSO AMMINISTRATORE",
        CategoriaSpesa.CompensoConsigliere      => "COMPENSO CONSIGLIERE",
        CategoriaSpesa.CostiPersonale           => "COSTI DEL PERSONALE",
        CategoriaSpesa.CostiInizioAttivita      => "COSTI INIZIO ATTIVITÀ",
        CategoriaSpesa.RimborsoAmministratore   => "RIMBORSO AMMINISTRATORE",
        CategoriaSpesa.AltriRicaviVari          => "ALTRI RICAVI VARI",
        CategoriaSpesa.Dividendi                => "DIVIDENDI",
        CategoriaSpesa.AltreSpeseFisse          => "ALTRE SPESE FISSE",
        CategoriaSpesa.Altro                    => "ALTRO",
        _ => c.ToString()
    };

    public static string Label(this StatoScadenza s) => s switch
    {
        StatoScadenza.DaPagare    => "Da pagare",
        StatoScadenza.Programmato => "Programmato",
        StatoScadenza.Pagato      => "Pagato",
        StatoScadenza.Insoluto    => "Scaduto",
        StatoScadenza.Annullato   => "Annullato",
        StatoScadenza.MaiPagato   => "Mai pagato",
        _ => s.ToString()
    };

    public static string Label(this MetodoPagamento m) => m switch
    {
        MetodoPagamento.Bonifico       => "Bonifico",
        MetodoPagamento.Rid            => "RID",
        MetodoPagamento.Riba           => "RIBA",
        MetodoPagamento.CartaCredito   => "CC",
        MetodoPagamento.Contanti       => "Contanti",
        MetodoPagamento.Assegno        => "Assegno",
        MetodoPagamento.Compensazione  => "Compensazione",
        MetodoPagamento.Altro          => "Altro",
        _ => m.ToString()
    };

    public static string Label(this TipoEmissioneFattura t) => t switch
    {
        TipoEmissioneFattura.Elettronica   => "E",
        TipoEmissioneFattura.Manuale       => "M",
        _ => "—"
    };

    public static string LabelEsteso(this TipoEmissioneFattura t) => t switch
    {
        TipoEmissioneFattura.Elettronica   => "Elettronica (SDI)",
        TipoEmissioneFattura.Manuale       => "Manuale",
        _ => "Non specificato"
    };
}
