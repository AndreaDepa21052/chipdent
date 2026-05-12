using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Chipdent.Web.Infrastructure.Sepa;

/// <summary>
/// Genera distinte di bonifici SEPA Credit Transfer in formato
/// <c>pain.001.001.03</c> (ISO 20022) — accettato da tutti gli home banking
/// italiani via canale CBI standard.
///
/// Struttura: un <c>GrpHdr</c> + N <c>PmtInf</c> (uno per IBAN ordinante / data
/// esecuzione), ognuno con N <c>CdtTrfTxInf</c> (uno per scadenza/beneficiario).
/// Permette di pagare da IBAN diversi (uno per clinica) in un'unica distinta.
/// </summary>
public static class SepaXmlBuilder
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public record SepaTransazione(
        string EndToEndId,
        decimal Importo,
        string BeneficiarioNome,
        string BeneficiarioIban,
        string? BeneficiarioBic,
        string Causale,
        string? BeneficiarioCodiceFiscale = null,
        string? BeneficiarioIndirizzo = null,
        string? BeneficiarioCodicePostale = null,
        string? BeneficiarioLocalita = null,
        string? BeneficiarioProvincia = null,
        string BeneficiarioPaese = "IT");

    /// <summary>Gruppo di transazioni che condividono lo stesso ordinante (IBAN/BIC/Nome).</summary>
    public record SepaGruppoOrdinante(
        string PaymentInfoId,
        string OrdinanteNome,
        string OrdinanteIban,
        string? OrdinanteBic,
        string? OrdinanteCodiceFiscale,
        IReadOnlyList<SepaTransazione> Transazioni);

    public record SepaInput(
        string MessageId,
        DateTime DataCreazione,
        DateTime DataEsecuzione,
        string InitiatingPartyNome,
        string? InitiatingPartyCodiceFiscale,
        IReadOnlyList<SepaGruppoOrdinante> Gruppi);

    public static (string Xml, decimal Totale, int Count) Build(SepaInput input)
    {
        if (input.Gruppi.Count == 0 || input.Gruppi.Sum(g => g.Transazioni.Count) == 0)
            throw new InvalidOperationException("Distinta vuota: nessuna scadenza selezionata.");
        foreach (var g in input.Gruppi)
        {
            if (string.IsNullOrWhiteSpace(g.OrdinanteIban))
                throw new InvalidOperationException($"IBAN ordinante mancante per gruppo {g.PaymentInfoId}.");
        }

        var totale = input.Gruppi.Sum(g => g.Transazioni.Sum(t => t.Importo));
        var count = input.Gruppi.Sum(g => g.Transazioni.Count);
        var inv = CultureInfo.InvariantCulture;

        var groupHeader = new XElement(Ns + "GrpHdr",
            new XElement(Ns + "MsgId", Truncate(input.MessageId, 35)),
            new XElement(Ns + "CreDtTm", input.DataCreazione.ToString("yyyy-MM-ddTHH:mm:ss", inv)),
            new XElement(Ns + "NbOfTxs", count),
            new XElement(Ns + "CtrlSum", totale.ToString("0.00", inv)),
            new XElement(Ns + "InitgPty",
                new XElement(Ns + "Nm", Truncate(SanitizeText(input.InitiatingPartyNome), 70)),
                IfNotEmpty(input.InitiatingPartyCodiceFiscale, cf =>
                    new XElement(Ns + "Id",
                        new XElement(Ns + "OrgId",
                            new XElement(Ns + "Othr",
                                new XElement(Ns + "Id", cf)))))));

        var customerCdtTrfInitn = new XElement(Ns + "CstmrCdtTrfInitn", groupHeader);

        foreach (var gruppo in input.Gruppi)
        {
            if (gruppo.Transazioni.Count == 0) continue;

            var pmtInfTotale = gruppo.Transazioni.Sum(t => t.Importo);
            var paymentInfo = new XElement(Ns + "PmtInf",
                new XElement(Ns + "PmtInfId", Truncate(gruppo.PaymentInfoId, 35)),
                new XElement(Ns + "PmtMtd", "TRF"),
                new XElement(Ns + "BtchBookg", "true"),
                new XElement(Ns + "NbOfTxs", gruppo.Transazioni.Count),
                new XElement(Ns + "CtrlSum", pmtInfTotale.ToString("0.00", inv)),
                new XElement(Ns + "PmtTpInf",
                    new XElement(Ns + "SvcLvl",
                        new XElement(Ns + "Cd", "SEPA"))),
                new XElement(Ns + "ReqdExctnDt", input.DataEsecuzione.ToString("yyyy-MM-dd", inv)),
                new XElement(Ns + "Dbtr",
                    new XElement(Ns + "Nm", Truncate(SanitizeText(gruppo.OrdinanteNome), 70))),
                new XElement(Ns + "DbtrAcct",
                    new XElement(Ns + "Id",
                        new XElement(Ns + "IBAN", NormalizeIban(gruppo.OrdinanteIban)))),
                new XElement(Ns + "DbtrAgt",
                    new XElement(Ns + "FinInstnId",
                        !string.IsNullOrWhiteSpace(gruppo.OrdinanteBic)
                            ? new XElement(Ns + "BIC", gruppo.OrdinanteBic.Trim().ToUpperInvariant())
                            : new XElement(Ns + "Othr", new XElement(Ns + "Id", "NOTPROVIDED")))),
                new XElement(Ns + "ChrgBr", "SLEV"));

            foreach (var t in gruppo.Transazioni)
            {
                var tx = new XElement(Ns + "CdtTrfTxInf",
                    new XElement(Ns + "PmtId",
                        new XElement(Ns + "InstrId", Truncate(t.EndToEndId, 35)),
                        new XElement(Ns + "EndToEndId", Truncate(t.EndToEndId, 35))),
                    new XElement(Ns + "Amt",
                        new XElement(Ns + "InstdAmt",
                            new XAttribute("Ccy", "EUR"),
                            t.Importo.ToString("0.00", inv))),
                    IfNotEmpty(t.BeneficiarioBic, bic =>
                        new XElement(Ns + "CdtrAgt",
                            new XElement(Ns + "FinInstnId",
                                new XElement(Ns + "BIC", bic.Trim().ToUpperInvariant())))),
                    BuildCdtr(t),
                    new XElement(Ns + "CdtrAcct",
                        new XElement(Ns + "Id",
                            new XElement(Ns + "IBAN", NormalizeIban(t.BeneficiarioIban)))),
                    new XElement(Ns + "RmtInf",
                        new XElement(Ns + "Ustrd", Truncate(SanitizeText(t.Causale), 140))));
                paymentInfo.Add(tx);
            }
            customerCdtTrfInitn.Add(paymentInfo);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                customerCdtTrfInitn));

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(sb, settings))
        {
            doc.Save(writer);
        }
        return (sb.ToString(), totale, count);
    }

    private static XElement? IfNotEmpty(string? value, Func<string, XElement> factory) =>
        string.IsNullOrWhiteSpace(value) ? null : factory(value);

    /// <summary>
    /// Costruisce il blocco &lt;Cdtr&gt; con Nm + (opzionale) PstlAdr + (opzionale) Id.
    /// L'indirizzo postale e il codice fiscale del beneficiario non sono obbligatori in pain.001,
    /// ma molte banche italiane (CBI) li riportano sull'estratto conto e nella ricevuta SEPA
    /// quando presenti. PstCd/TwnNm/Ctry sono i campi standard ISO 20022; CtrySubDvsn ospita
    /// la sigla di provincia. Il Codice Fiscale italiano va in PrvtId per le persone fisiche
    /// (16 caratteri alfanumerici) e in OrgId per le organizzazioni (11 cifre = anche P.IVA).
    /// </summary>
    private static XElement BuildCdtr(SepaTransazione t)
    {
        var cdtr = new XElement(Ns + "Cdtr",
            new XElement(Ns + "Nm", Truncate(SanitizeText(t.BeneficiarioNome), 70)));

        var pstlAdr = BuildPstlAdr(t);
        if (pstlAdr is not null) cdtr.Add(pstlAdr);

        var id = BuildPartyId(t.BeneficiarioCodiceFiscale);
        if (id is not null) cdtr.Add(id);

        return cdtr;
    }

    private static XElement? BuildPstlAdr(SepaTransazione t)
    {
        var indirizzo = string.IsNullOrWhiteSpace(t.BeneficiarioIndirizzo) ? null : t.BeneficiarioIndirizzo!.Trim();
        var cap = string.IsNullOrWhiteSpace(t.BeneficiarioCodicePostale) ? null : t.BeneficiarioCodicePostale!.Trim();
        var localita = string.IsNullOrWhiteSpace(t.BeneficiarioLocalita) ? null : t.BeneficiarioLocalita!.Trim();
        var provincia = string.IsNullOrWhiteSpace(t.BeneficiarioProvincia) ? null : t.BeneficiarioProvincia!.Trim();
        var paese = string.IsNullOrWhiteSpace(t.BeneficiarioPaese) ? "IT" : t.BeneficiarioPaese.Trim().ToUpperInvariant();

        if (indirizzo is null && cap is null && localita is null && provincia is null) return null;

        var pstlAdr = new XElement(Ns + "PstlAdr");
        if (indirizzo is not null)
            pstlAdr.Add(new XElement(Ns + "StrtNm", Truncate(SanitizeText(indirizzo), 70)));
        if (cap is not null)
            pstlAdr.Add(new XElement(Ns + "PstCd", Truncate(SanitizeText(cap), 16)));
        if (localita is not null)
            pstlAdr.Add(new XElement(Ns + "TwnNm", Truncate(SanitizeText(localita), 35)));
        if (provincia is not null)
            pstlAdr.Add(new XElement(Ns + "CtrySubDvsn", Truncate(SanitizeText(provincia), 35)));
        pstlAdr.Add(new XElement(Ns + "Ctry", paese.Length == 2 ? paese : "IT"));
        return pstlAdr;
    }

    /// <summary>
    /// Mappa il Codice Fiscale italiano in &lt;Id&gt;: 16 caratteri = persona fisica (PrvtId),
    /// 11 cifre = persona giuridica (OrgId). Schema "CF" come SchmeNm proprietario.
    /// </summary>
    private static XElement? BuildPartyId(string? codiceFiscale)
    {
        if (string.IsNullOrWhiteSpace(codiceFiscale)) return null;
        var cf = new string(codiceFiscale.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cf.Length == 0) return null;

        var isPersonaFisica = cf.Length == 16;
        var partyType = isPersonaFisica ? "PrvtId" : "OrgId";

        return new XElement(Ns + "Id",
            new XElement(Ns + partyType,
                new XElement(Ns + "Othr",
                    new XElement(Ns + "Id", cf),
                    new XElement(Ns + "SchmeNm",
                        new XElement(Ns + "Prtry", "CF")))));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static string NormalizeIban(string iban) =>
        new string(iban.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Rimuove caratteri non ammessi nei campi testuali ISO 20022 (set base latino).</summary>
    private static string SanitizeText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || " /-?:().,'+".Contains(ch))
                sb.Append(ch);
            else if (ch == 'à' || ch == 'è' || ch == 'é' || ch == 'ì' || ch == 'ò' || ch == 'ù')
                sb.Append(ch switch { 'à' => 'a', 'è' or 'é' => 'e', 'ì' => 'i', 'ò' => 'o', _ => 'u' });
            else if (ch == 'À' || ch == 'È' || ch == 'É' || ch == 'Ì' || ch == 'Ò' || ch == 'Ù')
                sb.Append(ch switch { 'À' => 'A', 'È' or 'É' => 'E', 'Ì' => 'I', 'Ò' => 'O', _ => 'U' });
            else if (ch == '&') sb.Append("E");
        }
        return sb.ToString();
    }
}
