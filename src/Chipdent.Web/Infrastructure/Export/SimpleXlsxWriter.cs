using System.Globalization;
using System.IO.Compression;
using System.Text;
using Chipdent.Web.Models;

namespace Chipdent.Web.Infrastructure.Export;

/// <summary>
/// Writer XLSX minimale (Office Open XML) per i report di Chipdent.
/// Costruisce manualmente lo zip OOXML con i pacchetti essenziali — niente NuGet aggiuntivi.
/// Pensato per fogli piccoli (tipicamente &lt;1k righe): testi inline, no shared strings, no styles complessi.
/// </summary>
public static class SimpleXlsxWriter
{
    public static byte[] BuildMovimentiMensili(MovimentiMensiliReport report)
    {
        var headers = new[]
        {
            "Mese/Anno", "Società",
            "N. assunzioni", "N. annullamento assunzioni",
            "N. cessazioni anticipate", "N. contratti non rinnovati", "N. contratti non rinnovati prossimo mese",
            "N. proroghe",
            "N. distacchi", "N. rettifiche/annullamento distacchi",
            "N. trasformazioni/aumento livello",
            "N. trasferimenti sede", "N. cambi mansione/reparto",
            "Note"
        };

        var rows = new List<IReadOnlyList<XlsxCell>>(report.Righe.Count + 1);
        // Header row
        rows.Add(headers.Select(h => XlsxCell.Text(h)).ToArray());

        foreach (var r in report.Righe)
        {
            rows.Add(new[]
            {
                XlsxCell.Text(report.MeseAnnoLabel),
                XlsxCell.Text(r.ClinicaNome),
                XlsxCell.Number(r.NumeroAssunzioni),
                XlsxCell.Number(r.NumeroAnnullamentiAssunzione),
                XlsxCell.Number(r.NumeroCessazioniAnticipate),
                XlsxCell.Number(r.NumeroContrattiNonRinnovati),
                XlsxCell.Number(r.NumeroContrattiNonRinnovatiProssimoMese),
                XlsxCell.Number(r.NumeroProroghe),
                XlsxCell.Number(r.NumeroDistacchi),
                XlsxCell.Number(r.NumeroRettificheDistacchi),
                XlsxCell.Number(r.NumeroTrasformazioniLivello),
                XlsxCell.Number(r.NumeroTrasferimentiSede),
                XlsxCell.Number(r.NumeroCambiMansione),
                XlsxCell.Text(string.Join(" | ", r.Note))
            });
        }

        return BuildWorkbook("Movimenti", rows);
    }

    private static byte[] BuildWorkbook(string sheetName, IReadOnlyList<IReadOnlyList<XlsxCell>> rows)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", BuildContentTypes());
            WriteEntry(zip, "_rels/.rels", BuildRootRels());
            WriteEntry(zip, "xl/workbook.xml", BuildWorkbookXml(sheetName));
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels());
            WriteEntry(zip, "xl/worksheets/sheet1.xml", BuildSheetXml(rows));
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string BuildContentTypes() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private static string BuildRootRels() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string BuildWorkbookXml(string sheetName)
    {
        var safeName = EscapeXml(sheetName);
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="{safeName}" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
    }

    private static string BuildWorkbookRels() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;

    private static string BuildSheetXml(IReadOnlyList<IReadOnlyList<XlsxCell>> rows)
    {
        // Costruiamo l'XML a mano per evitare problemi di encoding dell'XmlWriter
        // (StringWriter → UTF-16 nella dichiarazione, file salvato in UTF-8 → Excel non apre).
        var sb = new StringBuilder(rows.Count * 64);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetData>");

        for (int rIdx = 0; rIdx < rows.Count; rIdx++)
        {
            var row = rows[rIdx];
            sb.Append("<row r=\"").Append((rIdx + 1).ToString(CultureInfo.InvariantCulture)).Append("\">");

            for (int cIdx = 0; cIdx < row.Count; cIdx++)
            {
                var cell = row[cIdx];
                var coord = $"{ColumnLetter(cIdx)}{rIdx + 1}";

                if (cell.IsNumber)
                {
                    sb.Append("<c r=\"").Append(coord).Append("\"><v>")
                      .Append(cell.NumberValue.ToString(CultureInfo.InvariantCulture))
                      .Append("</v></c>");
                }
                else
                {
                    sb.Append("<c r=\"").Append(coord).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                      .Append(EscapeXml(cell.TextValue ?? string.Empty))
                      .Append("</t></is></c>");
                }
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string ColumnLetter(int zeroBasedIndex)
    {
        var letters = "";
        int n = zeroBasedIndex;
        while (true)
        {
            letters = (char)('A' + (n % 26)) + letters;
            n = n / 26 - 1;
            if (n < 0) break;
        }
        return letters;
    }

    private static string EscapeXml(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

    private readonly struct XlsxCell
    {
        public bool IsNumber { get; }
        public double NumberValue { get; }
        public string? TextValue { get; }

        private XlsxCell(bool isNumber, double number, string? text)
        { IsNumber = isNumber; NumberValue = number; TextValue = text; }

        public static XlsxCell Text(string? s) => new(false, 0, s ?? string.Empty);
        public static XlsxCell Number(double n) => new(true, n, null);
    }
}
