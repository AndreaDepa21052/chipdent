using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Chipdent.Web.Infrastructure.Tesoreria;

/// <summary>
/// Generatore XLSX minimale (Office Open XML / SpreadsheetML 2006) usato dagli
/// export tabellari della tesoreria. Niente dipendenze esterne: il file viene
/// composto manualmente come ZipArchive con le tre parti essenziali (workbook,
/// worksheet, sharedStrings) + content types e _rels. Compatibile con Excel,
/// LibreOffice Calc, Numbers, Google Sheets.
///
/// Limiti volutamente accettati per tenere il codice corto:
/// - tutte le celle sono di tipo <c>inlineStr</c> (nessuno styling, nessuna
///   conversione automatica a numero). È un export "documentale", non un
///   foglio di lavoro. Apri/calcola via Excel se vuoi le funzioni.
/// - 1 sheet dati per chiamata (più un eventuale foglio <c>Liste</c> nascosto
///   per le validation list su colonne enumerate).
/// </summary>
internal static class XlsxWriter
{
    public static byte[] Build(string sheetName, IReadOnlyList<string> header, IReadOnlyList<string[]> rows,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? listValidationsByColumn = null)
    {
        var safeSheet = SanitizeSheetName(sheetName);
        // Mappa colonna-dati → colonna-lista nel foglio "Liste".
        var listMap = (listValidationsByColumn ?? new Dictionary<int, IReadOnlyList<string>>())
            .Where(kv => kv.Value is { Count: > 0 })
            .OrderBy(kv => kv.Key)
            .Select((kv, i) => (DataCol: kv.Key, Values: kv.Value, ListeCol: ColLetter(i)))
            .ToList();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", ContentTypesXml(listMap.Count > 0));
            WriteEntry(zip, "_rels/.rels", RootRelsXml());
            WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml(listMap.Count > 0));
            WriteEntry(zip, "xl/workbook.xml", WorkbookXml(safeSheet, listMap.Count > 0));
            WriteEntry(zip, "xl/styles.xml", StylesXml());
            WriteEntry(zip, "xl/worksheets/sheet1.xml", SheetXml(header, rows, listMap));
            if (listMap.Count > 0)
                WriteEntry(zip, "xl/worksheets/sheet2.xml", ListeSheetXml(listMap));
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string ContentTypesXml(bool hasListe) =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
        """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
        """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
        """<Default Extension="xml" ContentType="application/xml"/>""" +
        """<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""" +
        """<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""" +
        (hasListe ? """<Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""" : "") +
        """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""" +
        "</Types>";

    private static string RootRelsXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
        """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
        """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>""" +
        "</Relationships>";

    private static string WorkbookRelsXml(bool hasListe) =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
        """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
        """<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>""" +
        """<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""" +
        (hasListe ? """<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>""" : "") +
        "</Relationships>";

    private static string WorkbookXml(string sheetName, bool hasListe) =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
        """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""" +
        "<sheets>" +
        $"""<sheet name="{XmlAttr(sheetName)}" sheetId="1" r:id="rId1"/>""" +
        (hasListe ? """<sheet name="Liste" sheetId="2" state="hidden" r:id="rId3"/>""" : "") +
        "</sheets>" +
        "</workbook>";

    private static string StylesXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
        """<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""" +
        """<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>""" +
        """<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>""" +
        """<borders count="1"><border/></borders>""" +
        """<cellStyleXfs count="1"><xf/></cellStyleXfs>""" +
        """<cellXfs count="2"><xf/><xf fontId="1" applyFont="1"/></cellXfs>""" +
        "</styleSheet>";

    private static string SheetXml(IReadOnlyList<string> header, IReadOnlyList<string[]> rows,
        IReadOnlyList<(int DataCol, IReadOnlyList<string> Values, string ListeCol)> listMap)
    {
        var sb = new StringBuilder(8192);
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("<sheetData>");

        // Header (riga 1, stile bold = s="1")
        sb.Append("<row r=\"1\">");
        for (int i = 0; i < header.Count; i++)
        {
            sb.Append($"<c r=\"{ColLetter(i)}1\" s=\"1\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlText(header[i])}</t></is></c>");
        }
        sb.Append("</row>");

        // Dati
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            sb.Append($"<row r=\"{r + 2}\">");
            for (int i = 0; i < row.Length; i++)
            {
                var v = row[i] ?? string.Empty;
                sb.Append($"<c r=\"{ColLetter(i)}{r + 2}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlText(v)}</t></is></c>");
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData>");

        // Tendine (data validations) sulle colonne mappate. La sqref copre fino a riga 10.000
        // così il vincolo resta attivo anche se l'utente aggiunge righe in coda al file.
        if (listMap.Count > 0)
        {
            sb.Append($"<dataValidations count=\"{listMap.Count}\">");
            foreach (var (dataCol, values, listeCol) in listMap)
            {
                var c = ColLetter(dataCol);
                sb.Append($"<dataValidation type=\"list\" allowBlank=\"1\" showErrorMessage=\"1\" sqref=\"{c}2:{c}10000\">");
                sb.Append($"<formula1>Liste!${listeCol}$1:${listeCol}${values.Count}</formula1>");
                sb.Append("</dataValidation>");
            }
            sb.Append("</dataValidations>");
        }

        sb.Append("</worksheet>");
        return sb.ToString();
    }

    /// <summary>Foglio nascosto con i valori ammessi per ogni colonna validata.</summary>
    private static string ListeSheetXml(IReadOnlyList<(int DataCol, IReadOnlyList<string> Values, string ListeCol)> listMap)
    {
        var sb = new StringBuilder(2048);
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("<sheetData>");
        var maxLen = listMap.Max(x => x.Values.Count);
        for (int r = 0; r < maxLen; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < listMap.Count; c++)
            {
                var vals = listMap[c].Values;
                if (r >= vals.Count) continue;
                sb.Append($"<c r=\"{ColLetter(c)}{r + 1}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlText(vals[r])}</t></is></c>");
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    /// <summary>0 → A, 1 → B, …, 25 → Z, 26 → AA, …</summary>
    private static string ColLetter(int index)
    {
        var sb = new StringBuilder();
        int n = index;
        do
        {
            sb.Insert(0, (char)('A' + n % 26));
            n = n / 26 - 1;
        } while (n >= 0);
        return sb.ToString();
    }

    private static string XmlText(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            // Sanitize control chars (Excel rifiuta tutto sotto 0x20 tranne TAB/LF/CR)
            if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
            switch (ch)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string XmlAttr(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
            switch (ch)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Excel impone &lt;=31 char e niente: <c>\ / ? * [ ]</c></summary>
    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Sheet1";
        var clean = new StringBuilder(31);
        foreach (var ch in name)
        {
            if ("\\/?*[]".IndexOf(ch) >= 0) continue;
            clean.Append(ch);
            if (clean.Length == 31) break;
        }
        return clean.Length > 0 ? clean.ToString() : "Sheet1";
    }
}
