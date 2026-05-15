#!/usr/bin/env python3
"""Estrae l'anagrafica completa dei cedenti dalle fatture passive concatenate
(es. FileRaw/DES-01-04-2026.pdf) e genera
src/Chipdent.Web/Infrastructure/Mongo/FattureFornitoriAnagraficaData.cs.

Per ogni fattura legge il blocco "Cedente/prestatore" — SOLO la colonna sinistra
(x < 290 pt sul layout A4 AssoSoftware) e SOLO fino al primo dei marker
"Terzo Intermediario" / "Tipologia documento" — così:
  - non vengono mescolati i campi del cessionario (colonna destra);
  - non viene scambiato il commercialista terzo intermediario (es. Datev
    Koinos) per il cedente vero quando il cedente è una persona fisica.

Campi estratti per ogni cedente:
  - PartitaIva, CodiceFiscale, RagioneSociale (Denominazione o Cognome nome,
    con flag IsPersonaFisica), Indirizzo, Cap, Localita, Provincia, Paese,
    Telefono, Email, PEC, IBAN.

Nota: il "Codice destinatario" che compare nell'header non è del cedente
(è dove il cessionario riceve fatture via SDI). Lo SDI del cedente non è
deducibile dal PDF — Fornitore.CodiceSdi va valorizzato a mano se serve.

Dedup: chiave primaria = PartitaIva normalizzata (uppercase, no spazi). Se
manca, fallback su CodiceFiscale; se manca anche quello, su nome normalizzato.
Per ogni campo si tiene il valore più frequente tra tutte le fatture dello
stesso cedente (Counter.most_common). Conflitti IBAN multi-valore → IBAN
viene scartato e segnalato.

Uso (dalla root del repo):
    python3 tools/import-fatture-anagrafica.py [pdf-path]
"""
from __future__ import annotations

import collections
import re
import sys
from pathlib import Path

import pdfplumber  # type: ignore[import-not-found]


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PDF = REPO_ROOT / "FileRaw" / "DES-01-04-2026.pdf"
OUT_PATH = REPO_ROOT / "src" / "Chipdent.Web" / "Infrastructure" / "Mongo" / "FattureFornitoriAnagraficaData.cs"

HEADER_FATTURA = "Cedente/prestatore"
# Marker che chiudono il blocco anagrafico del cedente
END_MARKERS = ("Terzo Intermediario", "Tipologia documento", "Cessionario/committente")
LEFT_COL_MAX_X = 290
IT_IBAN_RE = re.compile(r"IT\d{2}[A-Z]\d{10}[A-Z0-9]{12}")


def first_line_starts_with(page, prefix: str) -> bool:
    words = page.extract_words()
    if not words:
        return False
    top0 = round(words[0]["top"])
    first_line = " ".join(w["text"] for w in words if round(w["top"]) == top0)
    return first_line.startswith(prefix)


def cedente_block_text(page) -> str:
    """Ricompone le righe della colonna sinistra fino al primo END_MARKER."""
    words = [w for w in page.extract_words(keep_blank_chars=False)
             if w["x0"] < LEFT_COL_MAX_X]
    lines = collections.defaultdict(list)
    for w in words:
        lines[round(w["top"])].append(w)
    rows: list[str] = []
    for top in sorted(lines):
        ws = sorted(lines[top], key=lambda w: w["x0"])
        line = " ".join(w["text"] for w in ws)
        # stop al primo marker: i campi successivi (es. Datev Koinos Srl
        # come Terzo Intermediario) NON appartengono al cedente.
        if any(line.startswith(m) for m in END_MARKERS):
            break
        rows.append(line)
    return "\n".join(rows)


def extract_iban_near_keyword(text: str) -> str | None:
    m = re.search(r"\bIBAN\b", text, re.IGNORECASE)
    if not m:
        return None
    window = text[m.end(): m.end() + 400]
    compact = re.sub(r"\s", "", window)
    mm = IT_IBAN_RE.search(compact)
    return mm.group(0).upper() if mm else None


def get(text: str, pattern: str) -> str | None:
    m = re.search(pattern, text)
    if not m:
        return None
    v = m.group(1).strip()
    return v or None


def parse_cedente_fields(cedente_text: str) -> dict[str, str | None]:
    """Estrae i campi anagrafici dal solo blocco cedente."""
    out: dict[str, str | None] = {}

    # P.IVA: rimuove eventuale prefisso "IT"
    piva_raw = get(cedente_text, r"Identificativo fiscale ai fini IVA:\s*(\S+)")
    if piva_raw and piva_raw.upper().startswith("IT"):
        piva_raw = piva_raw[2:]
    out["PartitaIva"] = piva_raw

    out["CodiceFiscale"] = get(cedente_text, r"Codice fiscale:\s*(\S+)")

    # Denominazione (società) o Cognome nome (PF)
    den = get(cedente_text, r"Denominazione:\s*(.+)")
    if den:
        out["RagioneSociale"] = den
        out["IsPersonaFisica"] = False
    else:
        cog = get(cedente_text, r"Cognome nome:\s*(.+)")
        out["RagioneSociale"] = cog
        out["IsPersonaFisica"] = True if cog else None

    out["Indirizzo"] = get(cedente_text, r"Indirizzo:\s*(.+)")

    m = re.search(r"Comune:\s*(.+?)\s+Provincia:\s*([A-Z]{2})", cedente_text)
    if m:
        out["Localita"] = m.group(1).strip()
        out["Provincia"] = m.group(2).strip()
    else:
        out["Localita"] = None
        out["Provincia"] = None

    m = re.search(r"Cap:\s*(\d{5})\s+Nazione:\s*([A-Z]{2})", cedente_text)
    if m:
        out["CodicePostale"] = m.group(1)
        out["Paese"] = m.group(2)
    else:
        out["CodicePostale"] = None
        out["Paese"] = None

    out["Telefono"] = get(cedente_text, r"Telefono:\s*(.+)")
    out["Email"]    = get(cedente_text, r"Email:\s*(\S+)")
    out["Pec"]      = get(cedente_text, r"PEC:\s*(\S+)")
    return out


def cs_str(value, default: str = "null") -> str:
    if value is None:
        return default
    if isinstance(value, bool):
        return "true" if value else "false"
    s = " ".join(str(value).replace("\xa0", " ").split())
    if not s:
        return default
    s = s.replace("\\", "\\\\").replace("\"", "\\\"")
    return f'"{s}"'


def main() -> None:
    pdf_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PDF
    if not pdf_path.exists():
        sys.exit(f"PDF non trovato: {pdf_path}")

    # Per ogni cedente (chiave = P.IVA||CF||nome normalizzato), aggreghiamo
    # tutti i valori visti su tutte le fatture in cui appare.
    by_key: dict[str, dict[str, collections.Counter]] = collections.defaultdict(
        lambda: collections.defaultdict(collections.Counter))
    is_pf_flag: dict[str, bool] = {}
    invoices_total = 0

    with pdfplumber.open(str(pdf_path)) as pdf:
        pages = pdf.pages
        starts = [i for i, p in enumerate(pages) if first_line_starts_with(p, HEADER_FATTURA)]
        ranges = []
        for k, s in enumerate(starts):
            e = starts[k + 1] if k + 1 < len(starts) else len(pages)
            ranges.append((s, e))

        for s, e in ranges:
            invoices_total += 1
            cedente_text = cedente_block_text(pages[s])
            fields = parse_cedente_fields(cedente_text)
            piva = (fields.get("PartitaIva") or "").upper().strip()
            cf = (fields.get("CodiceFiscale") or "").upper().strip()
            rs = (fields.get("RagioneSociale") or "").strip()
            if not (piva or cf or rs):
                continue
            key = piva or cf or rs.lower()

            # IBAN: scan tutte le pagine della fattura
            iban = None
            for pi in range(s, e):
                txt = pages[pi].extract_text() or ""
                iban = extract_iban_near_keyword(txt)
                if iban:
                    break

            for fname, fval in fields.items():
                if fname == "IsPersonaFisica":
                    if fval is not None:
                        is_pf_flag.setdefault(key, bool(fval))
                    continue
                if fval is not None:
                    by_key[key][fname][fval] += 1
            if iban:
                by_key[key]["Iban"][iban] += 1

    # Risoluzione: per ogni campo prendi il valore più frequente.
    # IBAN multi-valore → scartato (sicurezza) e segnalato.
    fornitori: list[dict] = []
    iban_conflicts: list[tuple[str, list[tuple[str, int]]]] = []

    field_order = [
        "RagioneSociale", "PartitaIva", "CodiceFiscale",
        "Indirizzo", "CodicePostale", "Localita", "Provincia", "Paese",
        "Telefono", "Email", "Pec", "Iban",
    ]

    for key, counters in by_key.items():
        rec: dict = {"IsPersonaFisica": is_pf_flag.get(key, False)}
        for f in field_order:
            c = counters.get(f)
            if not c:
                rec[f] = None
                continue
            # ordina per (count desc, valore) per stabilità
            best, _ = max(c.items(), key=lambda kv: (kv[1], -len(kv[0])))
            rec[f] = best
        # rileva conflitti IBAN
        ic = counters.get("Iban")
        if ic and len(ic) > 1:
            iban_conflicts.append((rec.get("RagioneSociale") or key, list(ic.items())))
            rec["Iban"] = None
        fornitori.append(rec)

    # Ordina per RagioneSociale (stabile)
    fornitori.sort(key=lambda r: (r.get("RagioneSociale") or "").casefold())

    # ── Output Cs file ──
    out: list[str] = []
    out.append("// AUTO-GENERATED from FileRaw/" + pdf_path.name)
    out.append("// Do NOT edit by hand — rigenerare con tools/import-fatture-anagrafica.py")
    out.append("//")
    out.append("// Anagrafica completa dei Cedente/prestatore estratta dalle fatture passive")
    out.append("// PDF (AssoSoftware/FatturaPA). Chiave di deduplicazione = P.IVA (o codice")
    out.append(f"// fiscale / nome se P.IVA manca). Fatture analizzate: {invoices_total}.")
    out.append(f"// Fornitori unici trovati: {len(fornitori)}.")
    if iban_conflicts:
        out.append("// IBAN scartati per conflitto (più valori per lo stesso cedente):")
        for rs, lst in iban_conflicts:
            details = ", ".join(f"{ib} x{n}" for ib, n in lst)
            out.append(f"//   - {rs}: {details}")
    out.append("")
    out.append("namespace Chipdent.Web.Infrastructure.Mongo;")
    out.append("")
    out.append("internal static class FattureFornitoriAnagraficaData")
    out.append("{")
    out.append("    public sealed record Riga(")
    out.append("        string RagioneSociale,")
    out.append("        bool IsPersonaFisica,")
    out.append("        string? PartitaIva,")
    out.append("        string? CodiceFiscale,")
    out.append("        string? Indirizzo,")
    out.append("        string? CodicePostale,")
    out.append("        string? Localita,")
    out.append("        string? Provincia,")
    out.append("        string? Paese,")
    out.append("        string? Telefono,")
    out.append("        string? Email,")
    out.append("        string? Pec,")
    out.append("        string? Iban);")
    out.append("")
    out.append("    public static IReadOnlyList<Riga> Righe { get; } = new Riga[]")
    out.append("    {")
    for r in fornitori:
        rs = r.get("RagioneSociale") or ""
        out.append("        new(")
        out.append(f"            RagioneSociale: {cs_str(rs)},")
        out.append(f"            IsPersonaFisica: {cs_str(r.get('IsPersonaFisica', False))},")
        out.append(f"            PartitaIva: {cs_str(r.get('PartitaIva'))},")
        out.append(f"            CodiceFiscale: {cs_str(r.get('CodiceFiscale'))},")
        out.append(f"            Indirizzo: {cs_str(r.get('Indirizzo'))},")
        out.append(f"            CodicePostale: {cs_str(r.get('CodicePostale'))},")
        out.append(f"            Localita: {cs_str(r.get('Localita'))},")
        out.append(f"            Provincia: {cs_str(r.get('Provincia'))},")
        out.append(f"            Paese: {cs_str(r.get('Paese'))},")
        out.append(f"            Telefono: {cs_str(r.get('Telefono'))},")
        out.append(f"            Email: {cs_str(r.get('Email'))},")
        out.append(f"            Pec: {cs_str(r.get('Pec'))},")
        out.append(f"            Iban: {cs_str(r.get('Iban'))}),")
    out.append("    };")
    out.append("}")
    out.append("")
    OUT_PATH.write_text("\n".join(out), encoding="utf-8")

    with_iban = sum(1 for r in fornitori if r.get("Iban"))
    pf_count = sum(1 for r in fornitori if r.get("IsPersonaFisica"))

    print(f"Wrote {OUT_PATH.relative_to(REPO_ROOT)}")
    print(f"  - Fatture analizzate:    {invoices_total}")
    print(f"  - Fornitori unici:       {len(fornitori)}  (PF: {pf_count}, società: {len(fornitori) - pf_count})")
    print(f"  - Con IBAN univoco:      {with_iban}")
    print(f"  - Con IBAN in conflitto: {len(iban_conflicts)} (scartato, va deciso a mano)")
    if iban_conflicts:
        print("\nConflitti IBAN:")
        for rs, lst in iban_conflicts:
            print(f"  - {rs}: {', '.join(f'{ib} x{n}' for ib, n in lst)}")


if __name__ == "__main__":
    main()
