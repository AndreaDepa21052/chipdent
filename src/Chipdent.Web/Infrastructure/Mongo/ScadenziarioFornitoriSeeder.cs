using System.Text;
using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Sincronizza l'anagrafica fornitori con i dati estratti da
/// <c>FileRaw/scadenziario.xlsx</c> (vedi <see cref="ScadenziarioFornitoriData"/>):
///   - se il fornitore esiste già (match fuzzy su ragione sociale), valorizza
///     <see cref="Fornitore.Iban"/> quando ancora vuoto;
///   - se il fornitore non esiste, lo crea con un codice progressivo F####
///     ereditando la sequenza esistente.
///
/// Idempotente: al secondo passaggio nessun update/insert viene effettuato.
/// </summary>
internal static class ScadenziarioFornitoriSeeder
{
    public static async Task SeedAsync(
        MongoContext ctx,
        Tenant tenant,
        ILogger logger,
        CancellationToken ct)
    {
        var fornitori = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id)
            .ToListAsync(ct);

        // Indice fuzzy → fornitore (case-insensitive, ignora "Dr.", punti, ordine parole).
        var byKey = new Dictionary<string, Fornitore>(StringComparer.Ordinal);
        foreach (var f in fornitori)
        {
            var k = NormalizzaPerMatch(f.RagioneSociale);
            if (!string.IsNullOrEmpty(k) && !byKey.ContainsKey(k))
                byKey[k] = f;
        }

        var maxCodiceF = 0;
        foreach (var f in fornitori)
        {
            if (string.IsNullOrEmpty(f.Codice) || f.Codice[0] != 'F') continue;
            if (int.TryParse(f.Codice.AsSpan(1), out var n) && n > maxCodiceF) maxCodiceF = n;
        }

        var ibanAggiornati = 0;
        var nuoviCreati = 0;

        foreach (var riga in ScadenziarioFornitoriData.Righe)
        {
            var key = NormalizzaPerMatch(riga.RagioneSociale);
            if (string.IsNullOrEmpty(key)) continue;

            if (byKey.TryGetValue(key, out var esistente))
            {
                // Aggiorna IBAN solo se attualmente vuoto.
                if (!string.IsNullOrWhiteSpace(riga.Iban) && string.IsNullOrWhiteSpace(esistente.Iban))
                {
                    await ctx.Fornitori.UpdateOneAsync(
                        x => x.Id == esistente.Id,
                        Builders<Fornitore>.Update
                            .Set(x => x.Iban, riga.Iban)
                            .Set(x => x.UpdatedAt, DateTime.UtcNow),
                        cancellationToken: ct);
                    esistente.Iban = riga.Iban;
                    ibanAggiornati++;
                }
            }
            else
            {
                maxCodiceF++;
                var nuovo = new Fornitore
                {
                    TenantId = tenant.Id,
                    Codice = $"F{maxCodiceF:D4}",
                    RagioneSociale = riga.RagioneSociale,
                    Iban = string.IsNullOrWhiteSpace(riga.Iban) ? null : riga.Iban,
                    CategoriaDefault = CategoriaSpesa.AltreSpeseFisse,
                    Stato = StatoFornitore.Attivo,
                    TerminiPagamentoGiorni = 30,
                    BasePagamento = BasePagamento.DataFattura,
                    Note = "Importato dallo scadenziario Confident."
                };
                await ctx.Fornitori.InsertOneAsync(nuovo, cancellationToken: ct);
                byKey[key] = nuovo;
                nuoviCreati++;
            }
        }

        if (ibanAggiornati > 0 || nuoviCreati > 0)
        {
            logger.LogInformation(
                "ScadenziarioFornitori: aggiornati {Iban} IBAN e creati {Nuovi} nuovi fornitori",
                ibanAggiornati, nuoviCreati);
        }
    }

    /// <summary>Chiave fuzzy per matchare ragioni sociali equivalenti scritte con
    /// formattazione diversa. Lowercase, rimuove "dr"/"dott", split su whitespace,
    /// strip non-alphanumeric su ogni token, ordina i token e concatena.
    /// In questo modo:
    ///   "AGESP SPA" ≡ "AGESP S.P.A."
    ///   "Arrigo Martina" ≡ "Martina Arrigo" ≡ "Dr. Martina Arrigo".</summary>
    internal static string NormalizzaPerMatch(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var tokens = new List<string>();
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length == 0) return;
            var t = current.ToString();
            current.Clear();
            if (t == "dr" || t == "dott") return;
            tokens.Add(t);
        }
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                // tratta whitespace come separatore di token; la punteggiatura
                // interna a un token (es. "s.p.a.") viene assorbita continuando
                // ad accumulare nello stesso token finché non incontra spazio.
                if (char.IsWhiteSpace(ch)) Flush();
            }
        }
        Flush();
        tokens.Sort(StringComparer.Ordinal);
        return string.Concat(tokens);
    }
}
