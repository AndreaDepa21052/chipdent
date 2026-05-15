using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Backfill one-shot: importa le coppie (Fornitore, Tipo) dalla colonna M del file
/// <c>FileRaw/scadenziario.xlsx</c> e aggiorna le categorie dei fornitori nel DB.
/// Match per approssimazione semantica (Jaccard sui token, sogli a 0.6) contro
/// <see cref="Fornitore.RagioneSociale"/>, ignorando suffissi societari e
/// punteggiatura.
/// </summary>
/// <remarks>
/// Idempotente:
/// <list type="bullet">
///   <item>la <c>CategoriaDefault</c> viene sovrascritta solo se è ancora il fallback
///   <see cref="CategoriaSpesa.AltreSpeseFisse"/> (così edit manuali successivi sono
///   preservati);</item>
///   <item>la <c>CategoriaSecondaria</c> viene impostata solo se attualmente <c>null</c>.</item>
/// </list>
/// Quando un fornitore nello scadenziario compare con più tipi diversi, il default è
/// quello più frequente e l'eventuale secondario è il secondo più frequente.
/// </remarks>
internal static class BackfillCategoriaFornitoriDaScadenziarioSeeder
{
    private sealed record Entry(string Nome, CategoriaSpesa Primaria, CategoriaSpesa? Secondaria);

    // Dati estratti dal file FileRaw/scadenziario.xlsx, sheet "prova", colonne G (Fornitore)
    // e M (Tipo). Le righe non-fornitore (IVA, RITENUTE *) sono escluse a monte.
    private static readonly Entry[] Mappings =
    {
        new("A2A Energia spa",                                                  CategoriaSpesa.EnergiaElettrica,        null),
        new("Alpeggiani Avvocati Associati",                                    CategoriaSpesa.DueDiligence,            null),
        new("Arrigo Martina",                                                   CategoriaSpesa.DirezioneSanitaria,      null),
        new("Belforte GmbH",                                                    CategoriaSpesa.Locazione,               null),
        new("Belluzzo International Partners Studio Legale Tributario",         CategoriaSpesa.DueDiligence,            null),
        new("Biomec srl",                                                       CategoriaSpesa.MaterialeMedico,         null),
        new("BNP PARIBAS LEASE GROUP SA",                                       CategoriaSpesa.Leasing,                 null),
        new("Bors Iustina",                                                     CategoriaSpesa.CostiPersonale,          null),
        new("BPER BANCA",                                                       CategoriaSpesa.FinanziamentiPassivi,    CategoriaSpesa.OneriFinanziari),
        new("Capotosto Ilaria",                                                 CategoriaSpesa.Medici,                  null),
        new("CARMINATI ALLESTIMENTI SRL",                                       CategoriaSpesa.Marketing,               null),
        new("CCH SRL",                                                          CategoriaSpesa.Royalties,               CategoriaSpesa.CanoneMarketing),
        new("Colace Serena",                                                    CategoriaSpesa.CostiPersonale,          null),
        new("Compass Banca spa",                                                CategoriaSpesa.OneriFinanziari,         null),
        new("CRISTAL group srl",                                                CategoriaSpesa.ServizioPulizia,         null),
        new("CVZ ANTINCENDI S.A.S. DI M.P.",                                    CategoriaSpesa.AltreSpeseFisse,         null),
        new("De Lage Landen International B.V. - Succursale di Milano",         CategoriaSpesa.Leasing,                 null),
        new("DENTAL TREY S.R.L.",                                               CategoriaSpesa.MaterialeMedico,         null),
        new("Deutsche Bank S.p.A.",                                             CategoriaSpesa.OneriFinanziari,         null),
        new("DIELLE di LEZZI DANIELE",                                          CategoriaSpesa.MaterialeMedico,         null),
        new("Edenred Italia S.r.l.",                                            CategoriaSpesa.CostiPersonale,          null),
        new("EDIL B.& C. SAS DI BARBIERI STEFANO E C.",                         CategoriaSpesa.CostiInizioAttivita,     null),
        new("ELETTRICA MORANDI DEI F.LLI MORANDI SNC",                          CategoriaSpesa.CostiInizioAttivita,     null),
        new("Eni Plenitude spa",                                                CategoriaSpesa.EnergiaElettrica,        null),
        new("EniMoov S.p.A.",                                                   CategoriaSpesa.AltreSpeseFisse,         null),
        new("FARMACIA SANTA TERESA SAS DI ANNA MARIA BUZZI & C",                CategoriaSpesa.MaterialeMedico,         null),
        new("Ferioli Paolo",                                                    CategoriaSpesa.CostiInizioAttivita,     null),
        new("Ferrario Arredamenti S.r.l.",                                      CategoriaSpesa.CostiInizioAttivita,     null),
        new("IDEAL COMMUNICATION di R. Ruspantini",                             CategoriaSpesa.Marketing,               null),
        new("INDEPENDENT HOSPITALITY MALPENSA SRL",                             CategoriaSpesa.RimborsoAmministratore,  null),
        new("Infinity srl",                                                     CategoriaSpesa.Software,                null),
        new("Invisalign srl",                                                   CategoriaSpesa.Laboratorio,             null),
        new("J Dental Care srl",                                                CategoriaSpesa.MaterialeMedico,         null),
        new("LERETI spa",                                                       CategoriaSpesa.Acqua,                   null),
        new("Lico s.p.a.",                                                      CategoriaSpesa.SpeseCondominiali,       null),
        new("Lyreco Italia srl",                                                CategoriaSpesa.AltreSpeseFisse,         null),
        new("MB DENTAL LAB SRL",                                                CategoriaSpesa.Laboratorio,             null),
        new("Meta Platforms Ireland Limited",                                   CategoriaSpesa.Marketing,               null),
        new("Microsoft Ireland Operations Ltd",                                 CategoriaSpesa.It,                      null),
        new("Miglietta Paolo",                                                  CategoriaSpesa.CompensoAmministratore,  null),
        new("Miglietta Tina",                                                   CategoriaSpesa.CompensoAmministratore,  null),
        new("MONTI BEATRICE",                                                   CategoriaSpesa.Medici,                  null),
        new("MS ARREDO SAS di Milan Stefano & C.",                              CategoriaSpesa.CostiInizioAttivita,     null),
        new("Novelli Giuliana",                                                 CategoriaSpesa.Medici,                  null),
        new("Nunziati Marco",                                                   CategoriaSpesa.Dividendi,               null),
        new("ODONTOCAP S.R.L.",                                                 CategoriaSpesa.Laboratorio,             null),
        new("Plastigomma s.r.l.",                                               CategoriaSpesa.AltreSpeseFisse,         null),
        new("Provenzano Daniele",                                               CategoriaSpesa.CompensoConsigliere,     null),
        new("PROVENZANO PASQUALE",                                              CategoriaSpesa.DirezioneSanitaria,      null),
        new("Q-Print srl",                                                      CategoriaSpesa.AltreSpeseFisse,         null),
        new("RDR Dental s.a.s. di Babolin Danilo e Vanzulli Raffaele",          CategoriaSpesa.Laboratorio,             null),
        new("REP spa",                                                          CategoriaSpesa.NoleggioIt,              null),
        new("SAPIA PRATESI & PARTNERS S.R.L.",                                  CategoriaSpesa.AltreSpeseFisse,         null),
        new("SIAN snc",                                                         CategoriaSpesa.FondoInvestimento,       null),
        new("Sian srl",                                                         CategoriaSpesa.FondoInvestimento,       CategoriaSpesa.Assicurazione),
        new("SPH srl",                                                          CategoriaSpesa.CompensoConsigliere,     null),
        new("Studio Cassano Pierluigi",                                         CategoriaSpesa.SpeseCondominiali,       null),
        new("Studio Marco Mapelli architetto",                                  CategoriaSpesa.CostiInizioAttivita,     null),
        new("SWEDEN & MARTINA S.p.A.",                                          CategoriaSpesa.MaterialeMedico,         null),
        new("Trenord srl",                                                      CategoriaSpesa.RimborsoAmministratore,  null),
        new("Unicredit",                                                        CategoriaSpesa.FinanziamentiPassivi,    null),
        new("Vodafone Italia S.p.A",                                            CategoriaSpesa.Telefonia,               null),
        new("Wichita SRL",                                                      CategoriaSpesa.Locazione,               null),
    };

    private const double SimilarityThreshold = 0.6;

    public static async Task SeedAsync(MongoContext ctx, Tenant tenant, ILogger logger, CancellationToken ct)
    {
        var fornitori = await ctx.Fornitori
            .Find(f => f.TenantId == tenant.Id && !f.IsDeleted)
            .ToListAsync(ct);
        if (fornitori.Count == 0) return;

        var index = fornitori.Select(f => (F: f, Tokens: Tokenize(f.RagioneSociale))).ToList();

        var matched = 0;
        var primarieAggiornate = 0;
        var secondarieAggiornate = 0;
        var ignorati = 0;

        foreach (var m in Mappings)
        {
            var target = Tokenize(m.Nome);
            Fornitore? best = null;
            var bestScore = 0.0;
            foreach (var (f, tok) in index)
            {
                var s = Jaccard(target, tok);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = f;
                }
            }
            if (best is null || bestScore < SimilarityThreshold)
            {
                ignorati++;
                continue;
            }
            matched++;

            var updates = new List<UpdateDefinition<Fornitore>>();
            // Rispetta gli edit manuali: aggiorna la primaria solo se è ancora il fallback.
            if (best.CategoriaDefault == CategoriaSpesa.AltreSpeseFisse
                && m.Primaria != CategoriaSpesa.AltreSpeseFisse)
            {
                updates.Add(Builders<Fornitore>.Update.Set(x => x.CategoriaDefault, m.Primaria));
                best.CategoriaDefault = m.Primaria;
                primarieAggiornate++;
            }
            if (m.Secondaria.HasValue && best.CategoriaSecondaria is null)
            {
                updates.Add(Builders<Fornitore>.Update.Set(x => x.CategoriaSecondaria, m.Secondaria.Value));
                best.CategoriaSecondaria = m.Secondaria.Value;
                secondarieAggiornate++;
            }
            if (updates.Count > 0)
            {
                updates.Add(Builders<Fornitore>.Update.Set(x => x.UpdatedAt, DateTime.UtcNow));
                await ctx.Fornitori.UpdateOneAsync(
                    x => x.Id == best.Id,
                    Builders<Fornitore>.Update.Combine(updates),
                    cancellationToken: ct);
            }
        }

        logger.LogInformation(
            "BackfillCategoriaFornitoriDaScadenziario: {Matched}/{Totale} match (≥{Soglia:P0}); aggiornate {Primarie} primarie + {Secondarie} secondarie; {Ignorati} entry senza match",
            matched, Mappings.Length, SimilarityThreshold, primarieAggiornate, secondarieAggiornate, ignorati);
    }

    // ── Token-set matching ────────────────────────────────────────────────
    // Tokenizza ignorando maiuscole/minuscole, accenti, punteggiatura e suffissi
    // societari ricorrenti (srl, spa, sas, snc, …) che sporcherebbero il Jaccard.
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "srl", "srls", "spa", "sas", "snc", "ssd", "sa", "sl",
        "s", "r", "l", "p", "a", "n", "c",       // residui da S.r.l., S.p.A. dopo split su punti
        "di", "del", "della", "delle", "dei", "degli", "il", "la", "le", "lo", "gli",
        "e", "ed", "the", "of", "on", "co",
        "f", "flli", "lli",
        "succursale", "milano",                  // "Succursale di Milano" non è discriminante
        "international", "italia", "italy", "limited", "ltd", "gmbh", "bv",
        "ireland", "operations",
        "associati", "studio",                   // "Studio X Y" → resta solo X Y
    };

    private static HashSet<string> Tokenize(string? input)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input)) return set;

        var cleaned = new System.Text.StringBuilder(input.Length);
        foreach (var raw in input)
        {
            var ch = char.ToLowerInvariant(raw);
            cleaned.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        var tokens = cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            if (t.Length < 2) continue;
            if (Stopwords.Contains(t)) continue;
            set.Add(t);
        }
        return set;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = 0;
        foreach (var t in a) if (b.Contains(t)) inter++;
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }
}
