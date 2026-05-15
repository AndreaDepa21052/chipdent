using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Aggiorna gli IBAN dei fornitori usando i dati estratti dalle fatture passive
/// PDF (vedi <see cref="FattureFornitoriIbanData"/>, rigenerato da
/// <c>tools/import-fatture-ibans.py</c>).
///
/// Sicurezza (richiesta esplicita dell'utente, "non confondere gli IBAN"):
///   1. Il matching tra cedente PDF e fornitore in anagrafica è fatto su due
///      livelli, in ordine:
///        a. match esatto sulla chiave token-sorted di
///           <see cref="ScadenziarioFornitoriSeeder.NormalizzaPerMatch"/>
///           (copre persone fisiche / dottori con ordine nome/cognome variabile);
///        b. match per sottoinsieme di token: i token del fornitore in anagrafica
///           devono essere tutti contenuti nei token del cedente PDF (copre i
///           casi dove l'anagrafica usa un nome corto, es. "AIR LIQUIDE", e il
///           PDF la ragione sociale estesa, es. "Air Liquide Italia Gas e
///           Servizi Srl"). Se più fornitori matchano lo stesso cedente, SKIP.
///   2. L'IBAN viene scritto solo se il fornitore lo ha vuoto.
///      Se diverge da quello già presente, NON sovrascriviamo e logghiamo un
///      warning — la decisione finale resta umana via "Proposte anagrafica".
///   3. Se nessun fornitore matcha il cedente PDF, NON creiamo un nuovo
///      fornitore (sarebbe rumoroso e potrebbe duplicare): logghiamo "no
///      match" e basta.
///
/// Idempotente: al secondo passaggio nessun update viene effettuato.
/// </summary>
internal static class FattureFornitoriIbanSeeder
{
    public static async Task SeedAsync(
        MongoContext ctx,
        Tenant tenant,
        ILogger logger,
        CancellationToken ct)
    {
        if (FattureFornitoriIbanData.Righe.Count == 0) return;

        var fornitori = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id)
            .ToListAsync(ct);
        if (fornitori.Count == 0) return;

        // Indice 1: chiave token-sorted → fornitore (match esatto su persone fisiche).
        var bySortedKey = new Dictionary<string, Fornitore>(StringComparer.Ordinal);
        // Indice 2: token-set normalizzati per fornitore (per il match per sottoinsieme).
        var tokensByFornitore = new List<(Fornitore F, HashSet<string> Tokens)>(fornitori.Count);
        foreach (var f in fornitori)
        {
            var k = ScadenziarioFornitoriSeeder.NormalizzaPerMatch(f.RagioneSociale);
            if (!string.IsNullOrEmpty(k) && !bySortedKey.ContainsKey(k))
                bySortedKey[k] = f;
            var ts = TokenizzaPerSubset(f.RagioneSociale);
            if (ts.Count > 0)
                tokensByFornitore.Add((f, ts));
        }

        var aggiornati = 0;
        var noMatch = 0;
        var ambigui = 0;
        var divergenti = 0;
        var giaUguali = 0;

        foreach (var riga in FattureFornitoriIbanData.Righe)
        {
            Fornitore? target = null;

            // 1) match esatto sui token sortati (copre dottori / persone fisiche).
            var sortedKey = ScadenziarioFornitoriSeeder.NormalizzaPerMatch(riga.RagioneSociale);
            if (!string.IsNullOrEmpty(sortedKey))
                bySortedKey.TryGetValue(sortedKey, out target);

            // 2) match per sottoinsieme: seed tokens ⊆ PDF tokens. Solo se UNICO.
            if (target is null)
            {
                var pdfTokens = TokenizzaPerSubset(riga.RagioneSociale);
                if (pdfTokens.Count == 0) { noMatch++; continue; }
                var candidati = new List<Fornitore>();
                foreach (var (f, ts) in tokensByFornitore)
                {
                    if (ts.IsSubsetOf(pdfTokens)) candidati.Add(f);
                }
                if (candidati.Count == 0) { noMatch++; continue; }
                if (candidati.Count > 1)
                {
                    ambigui++;
                    logger.LogWarning(
                        "FattureFornitoriIban: cedente «{Pdf}» matcha {N} fornitori, skip per sicurezza ({Lista})",
                        riga.RagioneSociale, candidati.Count,
                        string.Join(", ", candidati.Select(c => c.RagioneSociale)));
                    continue;
                }
                target = candidati[0];
            }

            if (target is null) { noMatch++; continue; }

            // Confronto IBAN: normalizziamo (uppercase, no whitespace) per evitare
            // falsi positivi su differenze cosmetiche.
            var nuovoIban = Normalize(riga.Iban);
            var attualeIban = Normalize(target.Iban);

            if (string.IsNullOrEmpty(nuovoIban)) continue;

            if (string.IsNullOrEmpty(attualeIban))
            {
                await ctx.Fornitori.UpdateOneAsync(
                    x => x.Id == target.Id,
                    Builders<Fornitore>.Update
                        .Set(x => x.Iban, nuovoIban)
                        .Set(x => x.UpdatedAt, DateTime.UtcNow),
                    cancellationToken: ct);
                target.Iban = nuovoIban;
                aggiornati++;
                logger.LogInformation(
                    "FattureFornitoriIban: {Codice} «{Rs}» ← IBAN {Iban} (da cedente «{Pdf}»)",
                    target.Codice, target.RagioneSociale, nuovoIban, riga.RagioneSociale);
            }
            else if (string.Equals(attualeIban, nuovoIban, StringComparison.Ordinal))
            {
                giaUguali++;
            }
            else
            {
                divergenti++;
                logger.LogWarning(
                    "FattureFornitoriIban: «{Rs}» ha già IBAN {Vecchio}; PDF propone {Nuovo} — NON sovrascritto",
                    target.RagioneSociale, attualeIban, nuovoIban);
            }
        }

        if (aggiornati > 0 || noMatch > 0 || ambigui > 0 || divergenti > 0)
        {
            logger.LogInformation(
                "FattureFornitoriIban: aggiornati {U}, già allineati {E}, divergenti (skip) {D}, ambigui (skip) {A}, no-match {N} su {Tot} righe",
                aggiornati, giaUguali, divergenti, ambigui, noMatch, FattureFornitoriIbanData.Righe.Count);
        }
    }

    private static string? Normalize(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        var sb = new System.Text.StringBuilder(iban.Length);
        foreach (var ch in iban) if (!char.IsWhiteSpace(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Tokenizza una ragione sociale in token alfanumerici lowercase,
    /// scartando suffissi giuridici comuni e parole funzionali italiane. Usato
    /// per il match per sottoinsieme tra anagrafica e cedente PDF.</summary>
    internal static HashSet<string> TokenizzaPerSubset(string? s)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(s)) return result;
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length == 0) return;
            var t = current.ToString();
            current.Clear();
            if (StopWords.Contains(t)) return;
            result.Add(t);
        }
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) current.Append(char.ToLowerInvariant(ch));
            else if (char.IsWhiteSpace(ch)) Flush();
            // punteggiatura interna a un token (es. "s.p.a.") viene assorbita
        }
        Flush();
        return result;
    }

    // Stop-word: suffissi/forme giuridiche e congiunzioni italiane usate spesso
    // come "rumore" intorno al nome distintivo del fornitore.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "dr", "dott", "dottssa", "dottoressa",
        "srl", "spa", "sas", "snc", "sc", "scrl", "scarl", "ss", "soc",
        "di", "de", "del", "della", "dei", "delle", "da",
        "la", "il", "lo", "le", "gli", "i",
        "e", "ed", "o", "od",
        "c", "f", "lli", "flli",
    };
}
