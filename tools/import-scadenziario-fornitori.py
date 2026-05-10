#!/usr/bin/env python3
"""Estrae l'elenco (Fornitore, IBAN) da FileRaw/scadenziario.xlsx e genera
src/Chipdent.Web/Infrastructure/Mongo/ScadenziarioFornitoriData.cs.

Il foglio sorgente è "prova": colonna G = nome fornitore, colonna O = IBAN.
Se per uno stesso fornitore compaiono più IBAN nei vari righi, viene scelto
il più frequente (la moda); a parità di frequenza, il primo trovato.

Uso (dalla root del repo):
    python3 tools/import-scadenziario-fornitori.py
"""
from __future__ import annotations

from collections import Counter, defaultdict
from pathlib import Path

import openpyxl  # type: ignore[import-not-found]


REPO_ROOT = Path(__file__).resolve().parents[1]
XLSX_PATH = REPO_ROOT / "FileRaw" / "scadenziario.xlsx"
OUT_PATH = REPO_ROOT / "src" / "Chipdent.Web" / "Infrastructure" / "Mongo" / "ScadenziarioFornitoriData.cs"


def cs_str(value, default: str = "null") -> str:
    if value is None:
        return default
    s = str(value).strip().replace("\xa0", " ").strip()
    if not s:
        return default
    # collassa newline e tab in singolo spazio (il foglio ne contiene alcuni nei nomi)
    s = " ".join(s.split())
    s = s.replace("\\", "\\\\").replace("\"", "\\\"")
    return f'"{s}"'


def main() -> None:
    wb = openpyxl.load_workbook(str(XLSX_PATH), data_only=True)
    ws = wb["prova"]

    # fornitore -> Counter di IBAN normalizzati (uppercase, no spazi)
    ibans: dict[str, Counter[str]] = defaultdict(Counter)
    # fornitore -> primo nome "raw" incontrato (ordine d'apparizione)
    first_seen: dict[str, str] = {}

    for i, row in enumerate(ws.iter_rows(values_only=True)):
        if i == 0:
            continue
        forn = row[6]
        if forn is None or not str(forn).strip():
            continue
        forn_raw = str(forn).strip().replace("\xa0", " ")
        key = forn_raw.lower()
        if key not in first_seen:
            first_seen[key] = forn_raw

        iban = row[14]
        if isinstance(iban, str) and iban.strip():
            iban_norm = "".join(ch for ch in iban if ch.isalnum()).upper()
            if iban_norm:
                ibans[key][iban_norm] += 1

    entries: list[tuple[str, str | None]] = []
    for key, ragione in first_seen.items():
        if ibans[key]:
            iban = ibans[key].most_common(1)[0][0]
        else:
            iban = None
        entries.append((ragione, iban))

    entries.sort(key=lambda x: x[0].casefold())

    out = []
    out.append("// AUTO-GENERATED from FileRaw/scadenziario.xlsx (foglio \"prova\")")
    out.append("// Do NOT edit by hand — rigenerare con tools/import-scadenziario-fornitori.py")
    out.append("")
    out.append("namespace Chipdent.Web.Infrastructure.Mongo;")
    out.append("")
    out.append("internal static class ScadenziarioFornitoriData")
    out.append("{")
    out.append("    public sealed record Riga(string RagioneSociale, string? Iban);")
    out.append("")
    out.append("    /// <summary>Elenco fornitori dedotti dallo scadenziario Confident:")
    out.append("    /// per ognuno, l'IBAN più ricorrente nei righi (se presente).</summary>")
    out.append("    public static IReadOnlyList<Riga> Righe { get; } = new Riga[]")
    out.append("    {")
    for ragione, iban in entries:
        out.append(f"        new({cs_str(ragione)}, {cs_str(iban)}),")
    out.append("    };")
    out.append("}")
    out.append("")

    OUT_PATH.write_text("\n".join(out), encoding="utf-8")
    with_iban = sum(1 for _, i in entries if i)
    print(f"Wrote {OUT_PATH.relative_to(REPO_ROOT)}: "
          f"{len(entries)} fornitori, {with_iban} con IBAN")


if __name__ == "__main__":
    main()
