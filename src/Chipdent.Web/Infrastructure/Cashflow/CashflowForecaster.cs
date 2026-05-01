using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Cashflow;

/// <summary>
/// Costruisce la proiezione di cash flow del tenant a 12 settimane:
///  - uscite "certe" = scadenze in stato DaPagare/Programmato
///  - uscite "previste" = predizione ricorrenti basata sullo storico fatture (>= 3 fatture
///    in 12 mesi con varianza cadenza < 30% = ricorrente "alta confidence")
///  - entrate "previste" = EntrataAttesa inserite manualmente dall'Owner
///  - saldo proiettato = saldo iniziale + entrate - (certe + previste) cumulato per settimana
/// </summary>
public class CashflowForecaster
{
    private readonly MongoContext _mongo;

    /// <summary>Numero minimo di fatture storiche per considerare un fornitore "ricorrente".</summary>
    public const int MinFattureStoriche = 3;
    /// <summary>Varianza relativa massima della cadenza (CV) per dichiarare ricorrenza affidabile.</summary>
    public const double VarianzaMaxCadenza = 0.30;
    /// <summary>Numero di settimane future coperte dalla proiezione.</summary>
    public const int OrizzonteSettimane = 12;

    public CashflowForecaster(MongoContext mongo)
    {
        _mongo = mongo;
    }

    public async Task<CashflowForecast> BuildAsync(string tenantId, CancellationToken ct = default)
    {
        var oggi = DateTime.UtcNow.Date;
        var dodiciMesiFa = oggi.AddMonths(-12);
        var orizzonteFine = oggi.AddDays(7 * OrizzonteSettimane);

        var settings = await _mongo.CashflowSettings.Find(s => s.TenantId == tenantId).FirstOrDefaultAsync(ct)
                       ?? new CashflowSettings { TenantId = tenantId };
        var fornitori = (await _mongo.Fornitori.Find(f => f.TenantId == tenantId).ToListAsync(ct))
            .ToDictionary(f => f.Id);
        var fatture = await _mongo.Fatture
            .Find(f => f.TenantId == tenantId && f.Stato != StatoFattura.Rifiutata)
            .ToListAsync(ct);
        var scadenze = await _mongo.ScadenzePagamento
            .Find(s => s.TenantId == tenantId
                       && (s.Stato == StatoScadenza.DaPagare || s.Stato == StatoScadenza.Programmato))
            .ToListAsync(ct);
        var entrate = await _mongo.EntrateAttese
            .Find(e => e.TenantId == tenantId && e.DataAttesa >= oggi && e.DataAttesa <= orizzonteFine)
            .ToListAsync(ct);

        // ── 1) Ricorrenti ─────────────────────────────────────────────
        var ricorrenti = DetectRicorrenti(fatture, fornitori, oggi, dodiciMesiFa);

        // ── 2) Uscite previste = istanze future dei ricorrenti ────────
        var usciteVisibili = new List<MovimentoForecast>();
        foreach (var r in ricorrenti.Where(x => x.Confidence != ConfidenceRicorrenza.Bassa))
        {
            var prossima = r.ProssimaDataAttesa;
            while (prossima <= orizzonteFine)
            {
                usciteVisibili.Add(new MovimentoForecast(
                    Data: prossima,
                    Tipo: TipoMovimento.UscitaPrevista,
                    Importo: r.ImportoMedio,
                    FornitoreNome: r.FornitoreNome,
                    Descrizione: $"Ricorrente {r.CadenzaGiorni}gg · conf. {r.Confidence}",
                    Confidence: r.Confidence));
                prossima = prossima.AddDays(r.CadenzaGiorni);
            }
        }

        // ── 3) Uscite certe = scadenze ────────────────────────────────
        foreach (var s in scadenze.Where(s => s.DataScadenza <= orizzonteFine))
        {
            var f = fornitori.GetValueOrDefault(s.FornitoreId);
            usciteVisibili.Add(new MovimentoForecast(
                Data: s.DataScadenza < oggi ? oggi : s.DataScadenza,    // scaduti contati nella prima settimana
                Tipo: TipoMovimento.UscitaCerta,
                Importo: s.Importo,
                FornitoreNome: f?.RagioneSociale ?? "—",
                Descrizione: s.Stato == StatoScadenza.Programmato ? "Programmato" : "Da pagare",
                Confidence: ConfidenceRicorrenza.Alta));
        }

        // ── 4) Entrate ────────────────────────────────────────────────
        foreach (var e in entrate)
        {
            usciteVisibili.Add(new MovimentoForecast(
                Data: e.DataAttesa,
                Tipo: TipoMovimento.EntrataAttesa,
                Importo: e.Importo,
                FornitoreNome: e.Descrizione,
                Descrizione: e.Descrizione,
                Confidence: ConfidenceRicorrenza.Alta));
        }

        // ── 5) Aggregazione settimanale ───────────────────────────────
        var settimane = new List<SettimanaForecast>();
        var saldoCorrente = settings.SaldoCassa;
        var inizioSettimana = StartOfWeek(oggi);
        var primoSettoreSottoSoglia = (int?)null;

        for (var w = 0; w < OrizzonteSettimane; w++)
        {
            var dal = inizioSettimana.AddDays(7 * w);
            var al = dal.AddDays(7);
            var movsSettimana = usciteVisibili.Where(m => m.Data >= dal && m.Data < al).ToList();
            var certeOut = movsSettimana.Where(m => m.Tipo == TipoMovimento.UscitaCerta).Sum(m => m.Importo);
            var previsteOut = movsSettimana.Where(m => m.Tipo == TipoMovimento.UscitaPrevista).Sum(m => m.Importo);
            var entrateIn = movsSettimana.Where(m => m.Tipo == TipoMovimento.EntrataAttesa).Sum(m => m.Importo);
            saldoCorrente = saldoCorrente + entrateIn - certeOut - previsteOut;

            var sottoSoglia = saldoCorrente < settings.SogliaRischio;
            if (sottoSoglia && primoSettoreSottoSoglia is null) primoSettoreSottoSoglia = w;

            settimane.Add(new SettimanaForecast(
                NumeroSettimana: w,
                DataInizio: dal,
                UsciteCerte: certeOut,
                UscitePreviste: previsteOut,
                Entrate: entrateIn,
                SaldoFineSettimana: saldoCorrente,
                SottoSoglia: sottoSoglia));
        }

        // ── 6) KPI cumulativi 30/60/90 gg ─────────────────────────────
        decimal SumWindow(int days, Func<MovimentoForecast, bool> predicate) =>
            usciteVisibili.Where(m => m.Data >= oggi && m.Data < oggi.AddDays(days) && predicate(m)).Sum(m => m.Importo);

        return new CashflowForecast(
            Settings: settings,
            Settimane: settimane,
            Movimenti: usciteVisibili.OrderBy(m => m.Data).ToList(),
            Ricorrenti: ricorrenti,
            UsciteCerte30: SumWindow(30, m => m.Tipo == TipoMovimento.UscitaCerta),
            UsciteCerte60: SumWindow(60, m => m.Tipo == TipoMovimento.UscitaCerta),
            UsciteCerte90: SumWindow(90, m => m.Tipo == TipoMovimento.UscitaCerta),
            UscitePreviste90: SumWindow(90, m => m.Tipo == TipoMovimento.UscitaPrevista),
            Entrate90: SumWindow(90, m => m.Tipo == TipoMovimento.EntrataAttesa),
            SaldoProiettato90: settimane.Count > 0 ? settimane[^1].SaldoFineSettimana : settings.SaldoCassa,
            PrimaSettimanaSottoSoglia: primoSettoreSottoSoglia);
    }

    /// <summary>
    /// Per ogni fornitore con >= MinFattureStoriche fatture in 12 mesi calcola la
    /// cadenza media (gg fra emissioni successive) e la sua varianza relativa.
    /// Confidence: Alta se CV < 0.20, Media se < 0.30, altrimenti Bassa.
    /// </summary>
    private static List<RicorrenzaFornitore> DetectRicorrenti(
        List<FatturaFornitore> fatture, IDictionary<string, Fornitore> fornitori, DateTime oggi, DateTime dodiciMesiFa)
    {
        var risultato = new List<RicorrenzaFornitore>();
        var perFornitore = fatture
            .Where(f => f.DataEmissione >= dodiciMesiFa)
            .GroupBy(f => f.FornitoreId);

        foreach (var g in perFornitore)
        {
            var lista = g.OrderBy(f => f.DataEmissione).ToList();
            if (lista.Count < MinFattureStoriche) continue;

            // Cadenze in giorni fra fatture successive
            var cadenze = new List<double>();
            for (var i = 1; i < lista.Count; i++)
            {
                cadenze.Add((lista[i].DataEmissione - lista[i - 1].DataEmissione).TotalDays);
            }
            if (cadenze.Count == 0) continue;

            var mediaCadenza = cadenze.Average();
            if (mediaCadenza < 1) continue;

            var varianza = cadenze.Sum(x => Math.Pow(x - mediaCadenza, 2)) / cadenze.Count;
            var stdev = Math.Sqrt(varianza);
            var cv = stdev / mediaCadenza;        // coefficient of variation

            var confidence = cv switch
            {
                < 0.20 => ConfidenceRicorrenza.Alta,
                < VarianzaMaxCadenza => ConfidenceRicorrenza.Media,
                _ => ConfidenceRicorrenza.Bassa
            };

            var importoMedio = (decimal)lista.Average(f => (double)f.Totale);
            var ultimaData = lista[^1].DataEmissione;
            var prossima = ultimaData.AddDays(mediaCadenza);
            // Se la prossima è già nel passato, la pushiamo a oggi+qualche giorno
            if (prossima < oggi) prossima = oggi.AddDays(7);

            risultato.Add(new RicorrenzaFornitore(
                FornitoreId: g.Key,
                FornitoreNome: fornitori.GetValueOrDefault(g.Key)?.RagioneSociale ?? "—",
                NumeroFatture: lista.Count,
                CadenzaGiorni: (int)Math.Round(mediaCadenza),
                CoefficientOfVariation: cv,
                ImportoMedio: Math.Round(importoMedio, 2),
                UltimaDataFattura: ultimaData,
                ProssimaDataAttesa: prossima.Date,
                Confidence: confidence));
        }
        return risultato.OrderByDescending(r => r.ImportoMedio * r.NumeroFatture).ToList();
    }

    private static DateTime StartOfWeek(DateTime d)
    {
        var diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
        return d.AddDays(-diff).Date;
    }
}

public record CashflowForecast(
    CashflowSettings Settings,
    IReadOnlyList<SettimanaForecast> Settimane,
    IReadOnlyList<MovimentoForecast> Movimenti,
    IReadOnlyList<RicorrenzaFornitore> Ricorrenti,
    decimal UsciteCerte30,
    decimal UsciteCerte60,
    decimal UsciteCerte90,
    decimal UscitePreviste90,
    decimal Entrate90,
    decimal SaldoProiettato90,
    int? PrimaSettimanaSottoSoglia);

public record SettimanaForecast(
    int NumeroSettimana,
    DateTime DataInizio,
    decimal UsciteCerte,
    decimal UscitePreviste,
    decimal Entrate,
    decimal SaldoFineSettimana,
    bool SottoSoglia);

public record MovimentoForecast(
    DateTime Data,
    TipoMovimento Tipo,
    decimal Importo,
    string FornitoreNome,
    string Descrizione,
    ConfidenceRicorrenza Confidence);

public record RicorrenzaFornitore(
    string FornitoreId,
    string FornitoreNome,
    int NumeroFatture,
    int CadenzaGiorni,
    double CoefficientOfVariation,
    decimal ImportoMedio,
    DateTime UltimaDataFattura,
    DateTime ProssimaDataAttesa,
    ConfidenceRicorrenza Confidence);

public enum TipoMovimento
{
    UscitaCerta,
    UscitaPrevista,
    EntrataAttesa
}

public enum ConfidenceRicorrenza
{
    Alta,
    Media,
    Bassa
}
