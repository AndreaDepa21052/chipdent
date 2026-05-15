#!/usr/bin/env python3
"""Estrae (RagioneSociale del Cedente, IBAN) dai PDF delle fatture passive
concatenate (es. FileRaw/DES-01-04-2026.pdf) e genera
src/Chipdent.Web/Infrastructure/Mongo/FattureFornitoriIbanData.cs.

Regole di sicurezza (richieste esplicitamente dall'utente):
  - L'IBAN viene associato SEMPRE al Cedente/prestatore della fattura, mai al
    Cessionario/committente. Il riconoscimento del cedente avviene leggendo
    SOLO le parole presenti nella colonna sinistra della prima pagina della
    fattura (x < 290 pt sul layout A4 verticale standard del rendering
    AssoSoftware), così da non confondere "Denominazione:" del cedente con
    quella del cessionario, che sono sulla stessa riga ma in colonne diverse.
  - Se per lo stesso cedente compaiono IBAN diversi in fatture diverse, il
    fornitore viene marcato come "in conflitto" e l'IBAN NON viene scritto.
    Va rivisto a mano. Lo script stampa l'elenco dei conflitti.
  - Le fatture senza IBAN (pagamenti SDD/cash/etc.) vengono ignorate.

Uso (dalla root del repo):
    python3 tools/import-fatture-ibans.py [pdf-path]

Senza argomento usa FileRaw/DES-01-04-2026.pdf.
"""
from __future__ import annotations

import collections
import re
import sys
from pathlib import Path

import pdfplumber  # type: ignore[import-not-found]


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PDF = REPO_ROOT / "FileRaw" / "DES-01-04-2026.pdf"
OUT_PATH = REPO_ROOT / "src" / "Chipdent.Web" / "Infrastructure" / "Mongo" / "FattureFornitoriIbanData.cs"

HEADER_FATTURA = "Cedente/prestatore"
LEFT_COL_MAX_X = 290  # cedente è a sinistra, cessionario a destra
IT_IBAN_RE = re.compile(r"IT\d{2}[A-Z]\d{10}[A-Z0-9]{12}")


def first_line_starts_with(page, prefix: str) -> bool:
    words = page.extract_words()
    if not words:
        return False
    top0 = round(words[0]["top"])
    first_line = " ".join(w["text"] for w in words if round(w["top"]) == top0)
    return first_line.startswith(prefix)


def cedente_column_text(page) -> str:
    """Ricompone il testo della sola colonna sinistra (cedente) ordinato per riga."""
    words = [w for w in page.extract_words(keep_blank_chars=False)
             if w["x0"] < LEFT_COL_MAX_X]
    lines = collections.defaultdict(list)
    for w in words:
        lines[round(w["top"])].append(w)
    rows = []
    for top in sorted(lines):
        ws = sorted(lines[top], key=lambda w: w["x0"])
        rows.append(" ".join(w["text"] for w in ws))
    return "\n".join(rows)


def extract_iban_near_keyword(text: str) -> str | None:
    """Trova la parola "IBAN" e cerca un IBAN italiano completo (27 caratteri)
    nei ~400 caratteri successivi dopo aver compresso whitespace e newline.
    Restituisce None se non trova un pattern IT IBAN valido."""
    m = re.search(r"\bIBAN\b", text, re.IGNORECASE)
    if not m:
        return None
    window = text[m.end(): m.end() + 400]
    compact = re.sub(r"\s", "", window)
    mm = IT_IBAN_RE.search(compact)
    if not mm:
        return None
    return mm.group(0).upper()


def extract_cedente_denominazione(cedente_text: str) -> tuple[str | None, str]:
    """Restituisce (ragione_sociale, tipo) dove tipo è 'denom' (società) o
    'cognome' (persona fisica). Sotto AssoSoftware, le società hanno la riga
    "Denominazione:" e le persone fisiche hanno "Cognome nome:"."""
    m = re.search(r"Denominazione:\s*(.+)", cedente_text)
    if m:
        return m.group(1).strip(), "denom"
    m = re.search(r"Cognome nome:\s*(.+)", cedente_text)
    if m:
        return m.group(1).strip(), "cognome"
    return None, ""


def cs_str(value, default: str = "null") -> str:
    if value is None:
        return default
    s = " ".join(str(value).replace("\xa0", " ").split())
    if not s:
        return default
    s = s.replace("\\", "\\\\").replace("\"", "\\\"")
    return f'"{s}"'


def main() -> None:
    pdf_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PDF
    if not pdf_path.exists():
        sys.exit(f"PDF non trovato: {pdf_path}")

    # Per ogni cedente colleziona tutti gli IBAN trovati (per rilevare conflitti).
    # supplier_ibans[ragione_sociale] = Counter({iban: occorrenze})
    supplier_ibans: dict[str, collections.Counter[str]] = collections.defaultdict(collections.Counter)
    supplier_tipo: dict[str, str] = {}
    supplier_first_page: dict[str, int] = {}
    invoices_total = 0
    invoices_no_iban = 0

    with pdfplumber.open(str(pdf_path)) as pdf:
        pages = pdf.pages
        # Una fattura inizia su una pagina che parte con "Cedente/prestatore"
        starts = [i for i, p in enumerate(pages) if first_line_starts_with(p, HEADER_FATTURA)]
        ranges = []
        for k, s in enumerate(starts):
            e = starts[k + 1] if k + 1 < len(starts) else len(pages)
            ranges.append((s, e))

        for s, e in ranges:
            invoices_total += 1
            cedente_text = cedente_column_text(pages[s])
            denom, tipo = extract_cedente_denominazione(cedente_text)
            if not denom:
                continue

            # L'IBAN può essere su una qualunque pagina della fattura
            # (per fatture multipagina la sezione "Modalità pagamento" è in fondo).
            iban = None
            for pi in range(s, e):
                txt = pages[pi].extract_text() or ""
                iban = extract_iban_near_keyword(txt)
                if iban:
                    break
            if not iban:
                invoices_no_iban += 1
                continue

            supplier_ibans[denom][iban] += 1
            supplier_tipo.setdefault(denom, tipo)
            supplier_first_page.setdefault(denom, s + 1)

    # ── Risoluzione conflitti ──
    # Se per lo stesso cedente compaiono IBAN diversi → SKIP (sicurezza).
    # Se compare un solo IBAN → ok.
    accepted: list[tuple[str, str]] = []
    conflicts: list[tuple[str, list[tuple[str, int]]]] = []
    for denom, counter in sorted(supplier_ibans.items(), key=lambda x: x[0].casefold()):
        ibans_unique = list(counter.items())
        if len(ibans_unique) == 1:
            accepted.append((denom, ibans_unique[0][0]))
        else:
            conflicts.append((denom, ibans_unique))

    # IBAN condivisi tra più cedenti: lo segnaliamo come warning (può essere
    # legittimo — gruppo, factoring — ma è raro e merita una verifica umana).
    iban_to_suppliers: dict[str, list[str]] = collections.defaultdict(list)
    for denom, iban in accepted:
        iban_to_suppliers[iban].append(denom)
    shared = [(iban, names) for iban, names in iban_to_suppliers.items() if len(names) > 1]

    # ── Output Cs file ──
    out = []
    out.append("// AUTO-GENERATED from FileRaw/" + pdf_path.name)
    out.append("// Do NOT edit by hand — rigenerare con tools/import-fatture-ibans.py")
    out.append("//")
    out.append("// Estratto dalle fatture passive (AssoSoftware/FatturaPA). Ogni riga è la coppia")
    out.append("// (Cedente/prestatore.RagioneSociale, IBAN) letta dal blocco di sinistra della prima")
    out.append("// pagina di ciascuna fattura: l'IBAN appartiene SEMPRE al cedente, mai al cessionario.")
    out.append("//")
    out.append("// Suppliers totali con IBAN univoco: " + str(len(accepted)))
    if conflicts:
        out.append("// Suppliers SCARTATI per IBAN multipli (vanno gestiti a mano):")
        for denom, lst in conflicts:
            ib_str = ", ".join(f"{ib} x{n}" for ib, n in lst)
            out.append(f"//   - {denom}: {ib_str}")
    if shared:
        out.append("// ATTENZIONE: IBAN condivisi tra più cedenti (verificare a mano se intenzionale):")
        for iban, names in shared:
            out.append(f"//   - {iban} → {', '.join(names)}")
    out.append("")
    out.append("namespace Chipdent.Web.Infrastructure.Mongo;")
    out.append("")
    out.append("internal static class FattureFornitoriIbanData")
    out.append("{")
    out.append("    public sealed record Riga(string RagioneSociale, string Iban);")
    out.append("")
    out.append("    /// <summary>Mappa (Cedente, IBAN) estratta dalle fatture passive PDF.")
    out.append("    /// Solo righe con UN unico IBAN per cedente — i conflitti sono esclusi.</summary>")
    out.append("    public static IReadOnlyList<Riga> Righe { get; } = new Riga[]")
    out.append("    {")
    for denom, iban in accepted:
        out.append(f"        new({cs_str(denom)}, {cs_str(iban)}),")
    out.append("    };")
    out.append("}")
    out.append("")

    OUT_PATH.write_text("\n".join(out), encoding="utf-8")

    print(f"Wrote {OUT_PATH.relative_to(REPO_ROOT)}")
    print(f"  - Fatture totali analizzate:        {invoices_total}")
    print(f"  - Fatture senza IBAN (es. RID/SDD): {invoices_no_iban}")
    print(f"  - Suppliers con IBAN univoco:       {len(accepted)}")
    print(f"  - Suppliers con IBAN in conflitto:  {len(conflicts)} (scartati)")
    print(f"  - IBAN condivisi tra più cedenti:   {len(shared)} (segnalati come warning)")
    if conflicts:
        print("\nConflitti (da gestire manualmente):")
        for denom, lst in conflicts:
            print(f"  - {denom}:")
            for ib, n in lst:
                print(f"      {ib} (× {n})")
    if shared:
        print("\nIBAN condivisi (verificare se intenzionale):")
        for iban, names in shared:
            print(f"  - {iban} → {', '.join(names)}")


if __name__ == "__main__":
    main()
