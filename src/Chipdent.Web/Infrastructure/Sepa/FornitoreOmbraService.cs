using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Sepa;

/// <summary>
/// Sincronizza i Dottori (collaboratori/liberi professionisti) con un Fornitore
/// "ombra" che ne replica i dati fiscali, per riusare il modulo Tesoreria
/// (scadenze + distinte SEPA) anche per i compensi dottori.
/// Idempotente: trova/crea/aggiorna senza duplicare.
/// </summary>
public class FornitoreOmbraService
{
    private readonly MongoContext _mongo;

    public FornitoreOmbraService(MongoContext mongo)
    {
        _mongo = mongo;
    }

    /// <summary>
    /// Garantisce che il dottore abbia un Fornitore ombra collegato e che i campi
    /// chiave (PIVA, IBAN, ragione sociale) siano allineati.
    /// </summary>
    public async Task<Fornitore?> EnsureForDottoreAsync(Dottore dottore, CancellationToken ct = default)
    {
        // Solo i dottori che fatturano (collaborazione / libero professionista)
        // hanno bisogno di un Fornitore ombra.
        var fattura = dottore.TipoContratto == TipoContratto.Collaborazione
                   || dottore.TipoContratto == TipoContratto.LiberoProfessionista;
        if (!fattura) return null;

        var existing = await _mongo.Fornitori
            .Find(f => f.TenantId == dottore.TenantId && f.DottoreId == dottore.Id)
            .FirstOrDefaultAsync(ct);

        var ragioneSociale = dottore.NomeCompleto;
        if (existing is null)
        {
            var nuovo = new Fornitore
            {
                TenantId = dottore.TenantId,
                RagioneSociale = ragioneSociale,
                PartitaIva = dottore.PartitaIVA,
                CodiceFiscale = dottore.CodiceFiscale,
                EmailContatto = dottore.Email,
                Telefono = dottore.Telefono ?? dottore.Cellulare,
                Iban = dottore.IBAN,
                CategoriaDefault = CategoriaSpesa.Consulenze,
                Stato = dottore.IsCessato ? StatoFornitore.Cessato : StatoFornitore.Attivo,
                TerminiPagamentoGiorni = 30,
                BasePagamento = BasePagamento.FineMeseFattura,
                DottoreId = dottore.Id
            };
            await _mongo.Fornitori.InsertOneAsync(nuovo, cancellationToken: ct);
            return nuovo;
        }

        // Allineo i campi che possono cambiare lato Dottore
        var update = Builders<Fornitore>.Update
            .Set(f => f.RagioneSociale, ragioneSociale)
            .Set(f => f.PartitaIva, dottore.PartitaIVA)
            .Set(f => f.CodiceFiscale, dottore.CodiceFiscale)
            .Set(f => f.EmailContatto, dottore.Email)
            .Set(f => f.Telefono, dottore.Telefono ?? dottore.Cellulare)
            .Set(f => f.Iban, dottore.IBAN)
            .Set(f => f.Stato, dottore.IsCessato ? StatoFornitore.Cessato : StatoFornitore.Attivo)
            .Set(f => f.UpdatedAt, DateTime.UtcNow);

        await _mongo.Fornitori.UpdateOneAsync(f => f.Id == existing.Id, update, cancellationToken: ct);
        return existing;
    }

    /// <summary>
    /// Sincronizza i fornitori-ombra per tutti i dottori del tenant (one-shot all'avvio).
    /// </summary>
    public async Task<int> SyncTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var dottori = await _mongo.Dottori
            .Find(d => d.TenantId == tenantId)
            .ToListAsync(ct);
        var creati = 0;
        foreach (var d in dottori)
        {
            var prima = await _mongo.Fornitori
                .Find(f => f.TenantId == d.TenantId && f.DottoreId == d.Id)
                .AnyAsync(ct);
            await EnsureForDottoreAsync(d, ct);
            if (!prima)
            {
                var dopo = await _mongo.Fornitori
                    .Find(f => f.TenantId == d.TenantId && f.DottoreId == d.Id)
                    .AnyAsync(ct);
                if (dopo) creati++;
            }
        }
        return creati;
    }
}
