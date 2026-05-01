using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Infrastructure.Sepa;

/// <summary>
/// Genera distinte di bonifici SEPA Credit Transfer in formato
/// <c>pain.001.001.03</c> (ISO 20022) — accettato da tutti gli home banking
/// italiani via canale CBI standard.
///
/// Struttura: un singolo <c>PmtInf</c> per data esecuzione, con N
/// <c>CdtTrfTxInf</c> (uno per scadenza/beneficiario).
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
        string Causale);

    public record SepaInput(
        string MessageId,
        DateTime DataCreazione,
        DateTime DataEsecuzione,
        string OrdinanteNome,
        string OrdinanteIban,
        string? OrdinanteBic,
        string? OrdinanteCodiceFiscale,
        IReadOnlyList<SepaTransazione> Transazioni);

    public static (string Xml, decimal Totale, int Count) Build(SepaInput input)
    {
        if (input.Transazioni.Count == 0)
            throw new InvalidOperationException("Distinta vuota: nessuna scadenza selezionata.");
        if (string.IsNullOrWhiteSpace(input.OrdinanteIban))
            throw new InvalidOperationException("IBAN ordinante mancante.");

        var totale = input.Transazioni.Sum(t => t.Importo);
        var count = input.Transazioni.Count;
        var inv = CultureInfo.InvariantCulture;

        var groupHeader = new XElement(Ns + "GrpHdr",
            new XElement(Ns + "MsgId", Truncate(input.MessageId, 35)),
            new XElement(Ns + "CreDtTm", input.DataCreazione.ToString("yyyy-MM-ddTHH:mm:ss", inv)),
            new XElement(Ns + "NbOfTxs", count),
            new XElement(Ns + "CtrlSum", totale.ToString("0.00", inv)),
            new XElement(Ns + "InitgPty",
                new XElement(Ns + "Nm", Truncate(SanitizeText(input.OrdinanteNome), 70)),
                IfNotEmpty(input.OrdinanteCodiceFiscale, cf =>
                    new XElement(Ns + "Id",
                        new XElement(Ns + "OrgId",
                            new XElement(Ns + "Othr",
                                new XElement(Ns + "Id", cf)))))));

        var paymentInfo = new XElement(Ns + "PmtInf",
            new XElement(Ns + "PmtInfId", Truncate("PMT-" + input.MessageId, 35)),
            new XElement(Ns + "PmtMtd", "TRF"),                          // Transfer (bonifico)
            new XElement(Ns + "BtchBookg", "true"),                      // contabilizzazione cumulativa
            new XElement(Ns + "NbOfTxs", count),
            new XElement(Ns + "CtrlSum", totale.ToString("0.00", inv)),
            new XElement(Ns + "PmtTpInf",
                new XElement(Ns + "SvcLvl",
                    new XElement(Ns + "Cd", "SEPA"))),
            new XElement(Ns + "ReqdExctnDt", input.DataEsecuzione.ToString("yyyy-MM-dd", inv)),
            new XElement(Ns + "Dbtr",
                new XElement(Ns + "Nm", Truncate(SanitizeText(input.OrdinanteNome), 70))),
            new XElement(Ns + "DbtrAcct",
                new XElement(Ns + "Id",
                    new XElement(Ns + "IBAN", NormalizeIban(input.OrdinanteIban)))),
            new XElement(Ns + "DbtrAgt",
                new XElement(Ns + "FinInstnId",
                    !string.IsNullOrWhiteSpace(input.OrdinanteBic)
                        ? new XElement(Ns + "BIC", input.OrdinanteBic.Trim().ToUpperInvariant())
                        : new XElement(Ns + "Othr", new XElement(Ns + "Id", "NOTPROVIDED")))),
            new XElement(Ns + "ChrgBr", "SLEV"));                        // SLEV = SEPA Levy (charges shared)

        foreach (var t in input.Transazioni)
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
                new XElement(Ns + "Cdtr",
                    new XElement(Ns + "Nm", Truncate(SanitizeText(t.BeneficiarioNome), 70))),
                new XElement(Ns + "CdtrAcct",
                    new XElement(Ns + "Id",
                        new XElement(Ns + "IBAN", NormalizeIban(t.BeneficiarioIban)))),
                new XElement(Ns + "RmtInf",
                    new XElement(Ns + "Ustrd", Truncate(SanitizeText(t.Causale), 140))));
            paymentInfo.Add(tx);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                new XElement(Ns + "CstmrCdtTrfInitn",
                    groupHeader,
                    paymentInfo)));

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
            // Set ammesso nelle banche italiane: lettere, cifre, spazio e punteggiatura comune.
            if (char.IsLetterOrDigit(ch) || " /-?:().,'+".Contains(ch))
                sb.Append(ch);
            else if (ch == 'à' || ch == 'è' || ch == 'é' || ch == 'ì' || ch == 'ò' || ch == 'ù')
                sb.Append(ch switch { 'à' => 'a', 'è' or 'é' => 'e', 'ì' => 'i', 'ò' => 'o', _ => 'u' });
            else if (ch == 'À' || ch == 'È' || ch == 'É' || ch == 'Ì' || ch == 'Ò' || ch == 'Ù')
                sb.Append(ch switch { 'À' => 'A', 'È' or 'É' => 'E', 'Ì' => 'I', 'Ò' => 'O', _ => 'U' });
            else if (ch == '&') sb.Append("E");
            // altri caratteri: skipped (anziché escape)
        }
        return sb.ToString();
    }
}
