// AUTO-GENERATED from FileRaw/DES-01-04-2026.pdf
// Do NOT edit by hand — rigenerare con tools/import-fatture-ibans.py
//
// Estratto dalle fatture passive (AssoSoftware/FatturaPA). Ogni riga è la coppia
// (Cedente/prestatore.RagioneSociale, IBAN) letta dal blocco di sinistra della prima
// pagina di ciascuna fattura: l'IBAN appartiene SEMPRE al cedente, mai al cessionario.
//
// Suppliers totali con IBAN univoco: 24
// Suppliers SCARTATI per IBAN multipli (vanno gestiti a mano):
//   - Datev Koinos Srl: IT15F0503411103000000000781 x10, IT50W0569633840000002922X85 x5
//   - LABORATORIO ODONTOTECNICO M.B. SRL: IT02E0538751061000049436295 x1, IT04N0569651060000002776X26 x3
//   - MARCI' RACHELE: IT59Y0358901600010570958298 x5, IT51O0538711111000042566669 x1
// ATTENZIONE: IBAN condivisi tra più cedenti (verificare a mano se intenzionale):
//   - IT60L0200850240000106413548 → LYRECO ITALIA SRL, VIGILANZA DESIO-ALL' ERTA SRL

namespace Chipdent.Web.Infrastructure.Mongo;

internal static class FattureFornitoriIbanData
{
    public sealed record Riga(string RagioneSociale, string Iban);

    /// <summary>Mappa (Cedente, IBAN) estratta dalle fatture passive PDF.
    /// Solo righe con UN unico IBAN per cedente — i conflitti sono esclusi.</summary>
    public static IReadOnlyList<Riga> Righe { get; } = new Riga[]
    {
        new("Air Liquide Italia Gas e Servizi Srl", "IT29X0100501604000000000153"),
        new("BERETTA GIANCARLO & C. SNC", "IT81W0344033100000000767000"),
        new("BORZI' VALERIA GIULIA", "IT71U0200809500000421130401"),
        new("CAZZANIGA UBERTO MARIA PIERUGO", "IT25V0306932540000005856168"),
        new("COLLEONI FABIO PIETRO", "IT48M0329601601000067680773"),
        new("Culligan Italy S.r.l", "IT42T0200805364000030061282"),
        new("DOT Impresa S.r.l.", "IT26C0326822300052585592370"),
        new("DOTT SSA SERENA PORCARI", "IT97Z0306234210000001781055"),
        new("DRAGANTI LUCA", "IT07J0503422800000000001661"),
        new("EDENRED ITALIA Srl", "IT28T0333201600000001112420"),
        new("EDIL B. & C. SAS DI BARBIERI STEFANO E C.", "IT02Q0503450122000000072628"),
        new("FBM HEALTHCARE S.R.L.- In Liquidazione", "IT68K0608501600000000024604"),
        new("HENRY SCHEIN KRUGG S.R.L", "IT29J0200805364000030068362"),
        new("IGLESIAS BUSCA MARIA DEL PILAR", "IT68I0503433522000000003711"),
        new("Infinity srl", "IT86Z0894053570000000001099"),
        new("Invisalign S.r.l.", "IT45X0338001600000014427029"),
        new("LYRECO ITALIA SRL", "IT60L0200850240000106413548"),
        new("Mouzhan Maghsoudlou Rad", "IT55Q0366901600589884667018"),
        new("PA DIGITALE S.P.A.", "IT22B0200832974001437196864"),
        new("Sistemi S.P.A.", "IT39M0306933362100000009198"),
        new("SWEDEN & MARTINA S.p.A.", "IT61Q0898262680030000500272"),
        new("TIBICHI EDWIN FLAVIUS", "IT11X0200809500000430288497"),
        new("Triulzi Margherita", "IT44K0306901791100000001564"),
        new("VIGILANZA DESIO-ALL' ERTA SRL", "IT60L0200850240000106413548"),
    };
}
