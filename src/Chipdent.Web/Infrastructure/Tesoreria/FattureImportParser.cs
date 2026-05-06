using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Chipdent.Web.Domain.Entities;

namespace Chipdent.Web.Infrastructure.Tesoreria;

/// <summary>
/// Parser per i file delle fatture passive in arrivo dal back-office Confident.
/// Supporta:
///   - CSV con separatore ';' (encoding UTF-8 con BOM, header italiano)
///   - XLSX a uno o più sheet (parsing manuale via ZipArchive + XmlReader,
///     senza dipendenze NuGet aggiuntive)
///
/// I due formati noti sono:
///   - CCH:    18 colonne, include "IVA"
///   - Ident:  17 colonne, NON include "IVA"
/// L'autodetect avviene leggendo l'header (presenza/assenza colonna "IVA")
/// e/o il nome del file ("CCH" / "Ident").
/// </summary>
public static class FattureImportParser
{
    private static readonly CultureInfo It = new("it-IT");

    /// <summary>Esito parsing di un singolo file/sheet.</summary>
    public sealed record ParsedFile(
        string NomeFile,
        string Sezione,
        long DimensioneByte,
        string ChecksumSha256,
        IReadOnlyList<ImportFatturaRiga> Righe);

    public static ParsedFile ParseCsv(string nomeFile, byte[] contenuto)
    {
        var sezione = DetectSezioneFromName(nomeFile);
        using var ms = new MemoryStream(contenuto);
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var righe = new List<ImportFatturaRiga>();

        string? headerLine = reader.ReadLine();
        if (headerLine == null)
            return new ParsedFile(nomeFile, sezione, contenuto.LongLength, Sha256(contenuto), righe);

        var header = SplitCsvLine(headerLine);
        var idx = MapHeader(header);
        if (sezione == "Sconosciuta")
            sezione = idx.HasIva ? "CCH" : "Ident";

        int rowNum = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rowNum++;
            var cells = SplitCsvLine(line);
            righe.Add(BuildRiga(cells, idx, nomeFile, sezione, rowNum));
        }

        return new ParsedFile(nomeFile, sezione, contenuto.LongLength, Sha256(contenuto), righe);
    }

    /// <summary>Parser XLSX manuale (ZipArchive + XmlReader). Restituisce un file per sheet.</summary>
    public static IReadOnlyList<ParsedFile> ParseXlsx(string nomeFileSorgente, byte[] contenuto)
    {
        var checksum = Sha256(contenuto);
        var risultati = new List<ParsedFile>();
        using var ms = new MemoryStream(contenuto);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var sharedStrings = ReadSharedStrings(zip);
        var sheets = ReadWorkbookSheets(zip);

        foreach (var (nomeSheet, target) in sheets)
        {
            var entry = zip.GetEntry($"xl/{target}") ?? zip.GetEntry(target);
            if (entry == null) continue;
            var rows = ReadSheetRows(entry, sharedStrings);
            if (rows.Count == 0) continue;

            var header = rows[0];
            var idx = MapHeader(header);
            var sezione = DetectSezioneFromName(nomeSheet);
            if (sezione == "Sconosciuta") sezione = idx.HasIva ? "CCH" : "Ident";

            var righe = new List<ImportFatturaRiga>();
            for (int r = 1; r < rows.Count; r++)
            {
                var cells = rows[r];
                if (cells.All(string.IsNullOrWhiteSpace)) continue;
                righe.Add(BuildRiga(cells, idx, nomeSheet, sezione, r));
            }

            risultati.Add(new ParsedFile($"{nomeFileSorgente}#{nomeSheet}", sezione, entry.Length, checksum, righe));
        }

        return risultati;
    }

    // ─── helpers comuni ───────────────────────────────────────────────────

    private sealed class HeaderIndex
    {
        public bool HasIva;
        public int GestioniCollegate = -1, AttIva = -1, RegimeIva = -1, Sez = -1, Protocollo = -1;
        public int DataReg = -1, Numero = -1, DataDoc = -1, DataRic = -1;
        public int Fornitore = -1, TipoDoc = -1;
        public int Totale = -1, Iva = -1, Netto = -1, Ritenuta = -1;
        public int Valuta = -1, Causale = -1, Allegati = -1;
    }

    private static HeaderIndex MapHeader(IReadOnlyList<string> header)
    {
        var idx = new HeaderIndex();
        for (int i = 0; i < header.Count; i++)
        {
            var h = (header[i] ?? "").Trim().Trim('﻿');
            switch (h.ToLowerInvariant())
            {
                case "gestioni collegate complete": idx.GestioniCollegate = i; break;
                case "att. iva": idx.AttIva = i; break;
                case "regime iva": idx.RegimeIva = i; break;
                case "sez.": idx.Sez = i; break;
                case "protocollo": idx.Protocollo = i; break;
                case "data registrazione": idx.DataReg = i; break;
                case "numero": idx.Numero = i; break;
                case "data doc.": idx.DataDoc = i; break;
                case "data ricezione": idx.DataRic = i; break;
                case "fornitore": idx.Fornitore = i; break;
                case "tipo doc.": idx.TipoDoc = i; break;
                case "totale documento": idx.Totale = i; break;
                case "iva": idx.Iva = i; idx.HasIva = true; break;
                case "netto a pagare": idx.Netto = i; break;
                case "ritenuta": idx.Ritenuta = i; break;
                case "valuta": idx.Valuta = i; break;
                case "causale": idx.Causale = i; break;
                case "allegati": idx.Allegati = i; break;
            }
        }
        return idx;
    }

    private static ImportFatturaRiga BuildRiga(
        IReadOnlyList<string> c, HeaderIndex idx, string nomeFile, string sezione, int numeroRiga)
    {
        var errori = new List<string>();
        string? S(int i) => i >= 0 && i < c.Count ? c[i] : null;

        DateTime? D(int i, string label)
        {
            var s = S(i); if (string.IsNullOrWhiteSpace(s)) return null;
            var dt = ParseDate(s);
            if (dt == null) errori.Add($"Data non valida ({label}): '{s}'");
            return dt;
        }
        decimal? Dec(int i, string label)
        {
            var s = S(i); if (string.IsNullOrWhiteSpace(s)) return null;
            var v = ParseDecimal(s);
            if (v == null) errori.Add($"Numero non valido ({label}): '{s}'");
            return v;
        }

        var riga = new ImportFatturaRiga
        {
            NomeFile = nomeFile,
            Sezione = sezione,
            NumeroRiga = numeroRiga,
            GestioniCollegate = S(idx.GestioniCollegate),
            AttIva = S(idx.AttIva),
            RegimeIva = S(idx.RegimeIva),
            SezioneRegistro = S(idx.Sez),
            Protocollo = S(idx.Protocollo),
            DataRegistrazione = D(idx.DataReg, "Data registrazione"),
            Numero = S(idx.Numero),
            DataDocumento = D(idx.DataDoc, "Data doc."),
            DataRicezione = D(idx.DataRic, "Data ricezione"),
            Fornitore = S(idx.Fornitore),
            TipoDocumento = S(idx.TipoDoc),
            TotaleDocumento = Dec(idx.Totale, "Totale documento"),
            Iva = idx.HasIva ? Dec(idx.Iva, "IVA") : null,
            NettoAPagare = Dec(idx.Netto, "Netto a pagare"),
            Ritenuta = Dec(idx.Ritenuta, "Ritenuta"),
            Valuta = S(idx.Valuta),
            Causale = S(idx.Causale),
            Allegati = S(idx.Allegati),
            Errori = errori,
        };

        if (string.IsNullOrWhiteSpace(riga.Fornitore)) errori.Add("Fornitore mancante");
        if (riga.TotaleDocumento == null) errori.Add("Totale documento mancante");

        return riga;
    }

    private static string DetectSezioneFromName(string nome)
    {
        var u = nome.ToUpperInvariant();
        if (u.Contains("CCH")) return "CCH";
        if (u.Contains("IDENT")) return "Ident";
        return "Sconosciuta";
    }

    // ─── CSV ──────────────────────────────────────────────────────────────

    /// <summary>CSV con separatore ';'. Supporta valori quotati con doppi apici.</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == ';') { result.Add(sb.ToString()); sb.Clear(); }
                else if (ch == '"') inQuotes = true;
                else sb.Append(ch);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    // ─── XLSX (manuale) ───────────────────────────────────────────────────

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return list;
        using var s = entry.Open();
        using var xr = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false });
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "si")
            {
                var sb = new StringBuilder();
                using var sub = xr.ReadSubtree();
                while (sub.Read())
                {
                    if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                        sb.Append(sub.ReadElementContentAsString());
                }
                list.Add(sb.ToString());
            }
        }
        return list;
    }

    private static List<(string Name, string Target)> ReadWorkbookSheets(ZipArchive zip)
    {
        // mappa rId -> target dal file relationships
        var rels = new Dictionary<string, string>();
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            using var s = relsEntry.Open();
            using var xr = XmlReader.Create(s);
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "Relationship")
                {
                    var id = xr.GetAttribute("Id"); var tgt = xr.GetAttribute("Target");
                    if (id != null && tgt != null) rels[id] = tgt;
                }
            }
        }

        var sheets = new List<(string, string)>();
        var wb = zip.GetEntry("xl/workbook.xml");
        if (wb == null) return sheets;
        using var ws = wb.Open();
        using var wxr = XmlReader.Create(ws);
        while (wxr.Read())
        {
            if (wxr.NodeType == XmlNodeType.Element && wxr.LocalName == "sheet")
            {
                var name = wxr.GetAttribute("name") ?? string.Empty;
                var rId = wxr.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
                          ?? wxr.GetAttribute("r:id");
                if (rId != null && rels.TryGetValue(rId, out var tgt))
                    sheets.Add((name, tgt));
            }
        }
        return sheets;
    }

    private static List<List<string>> ReadSheetRows(ZipArchiveEntry entry, List<string> sharedStrings)
    {
        var rows = new List<List<string>>();
        using var s = entry.Open();
        using var xr = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false });

        List<string>? currentRow = null;
        while (xr.Read())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "row")
            {
                currentRow = new List<string>();
                rows.Add(currentRow);
            }
            else if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "c" && currentRow != null)
            {
                var refAttr = xr.GetAttribute("r");
                var t = xr.GetAttribute("t"); // s, str, b, inlineStr, e, n (default)
                int colIdx = ColIndexFromRef(refAttr);
                while (currentRow.Count < colIdx) currentRow.Add(string.Empty);

                string value = string.Empty;
                using var sub = xr.ReadSubtree();
                while (sub.Read())
                {
                    if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "v")
                    {
                        value = sub.ReadElementContentAsString();
                    }
                    else if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t")
                    {
                        // inlineStr <is><t>...
                        value = sub.ReadElementContentAsString();
                    }
                }

                if (t == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sIdx)
                    && sIdx >= 0 && sIdx < sharedStrings.Count)
                {
                    value = sharedStrings[sIdx];
                }
                else if (t == "b")
                {
                    value = value == "1" ? "true" : "false";
                }
                else if (string.IsNullOrEmpty(t) || t == "n")
                {
                    // numero o data: se è un double in range plausibile delle date Excel
                    // (1900-01-01 .. 2099-12-31 ≈ 1 .. 73050) e l'header indica che la colonna è una data,
                    // sarà convertito a valle in BuildRiga via ParseDate. Qui passiamo il numero come stringa
                    // ma se sembra una data Excel la convertiamo subito in formato dd-MM-yyyy per uniformità.
                    if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                        && d >= 1 && d <= 73050 && Math.Abs(d - Math.Truncate(d)) < 1e-6)
                    {
                        // potrebbe essere una data: la converto preservando anche la rappresentazione numerica.
                        // BuildRiga proverà prima ParseDate, poi ParseDecimal.
                        try
                        {
                            var dt = DateTime.FromOADate(d);
                            // euristica: se l'anno è ragionevole (>=2000) lo trattiamo come data
                            if (dt.Year >= 2000 && dt.Year <= 2099)
                                value = dt.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
                        }
                        catch { /* lascia come numero */ }
                    }
                }

                currentRow.Add(value ?? string.Empty);
            }
        }
        return rows;
    }

    private static int ColIndexFromRef(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        int col = 0;
        foreach (var ch in cellRef)
        {
            if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
            else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
            else break;
        }
        return Math.Max(0, col - 1);
    }

    // ─── parsing valori ───────────────────────────────────────────────────

    private static DateTime? ParseDate(string s)
    {
        s = s.Trim();
        string[] fmts = { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss" };
        if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
        if (DateTime.TryParse(s, It, DateTimeStyles.AssumeLocal, out dt))
            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
        return null;
    }

    private static decimal? ParseDecimal(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        // formato italiano "1.234,56"; gestiamo anche "1234.56"
        if (decimal.TryParse(s, NumberStyles.Number, It, out var v)) return v;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v)) return v;
        return null;
    }

    private static string Sha256(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var h = sha.ComputeHash(data);
        var sb = new StringBuilder(h.Length * 2);
        foreach (var b in h) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
