using Chipdent.Web.Domain.Entities;
using Chipdent.Web.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Chipdent.Web.Services;

/// <summary>
/// Gestisce la visibilità per ruolo dei menu della sidebar. La configurazione è
/// globale (cross-tenant) ed è modificabile solo dal PlatformAdmin.
/// </summary>
public interface IMenuVisibilityService
{
    Task<HashSet<string>> GetHiddenForRoleAsync(string role, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, HashSet<string>>> GetAllAsync(CancellationToken ct = default);
    Task SetHiddenForRoleAsync(string role, IEnumerable<string> hiddenSections, CancellationToken ct = default);
}

public class MongoMenuVisibilityService : IMenuVisibilityService
{
    private readonly MongoContext _ctx;

    public MongoMenuVisibilityService(MongoContext ctx) => _ctx = ctx;

    public async Task<HashSet<string>> GetHiddenForRoleAsync(string role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(role)) return new HashSet<string>();
        var doc = await _ctx.MenuVisibilities.Find(m => m.Role == role).FirstOrDefaultAsync(ct);
        return doc is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(doc.HiddenSections, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, HashSet<string>>> GetAllAsync(CancellationToken ct = default)
    {
        var docs = await _ctx.MenuVisibilities.Find(_ => true).ToListAsync(ct);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in MenuCatalog.ConfigurableRoles)
        {
            var doc = docs.FirstOrDefault(d => string.Equals(d.Role, role, StringComparison.OrdinalIgnoreCase));
            result[role] = doc is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(doc.HiddenSections, StringComparer.OrdinalIgnoreCase);
        }
        return result;
    }

    public async Task SetHiddenForRoleAsync(string role, IEnumerable<string> hiddenSections, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role required", nameof(role));

        var clean = (hiddenSections ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await _ctx.MenuVisibilities.Find(m => m.Role == role).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            await _ctx.MenuVisibilities.InsertOneAsync(new MenuVisibility
            {
                Role = role,
                HiddenSections = clean
            }, cancellationToken: ct);
        }
        else
        {
            await _ctx.MenuVisibilities.UpdateOneAsync(
                m => m.Id == existing.Id,
                Builders<MenuVisibility>.Update
                    .Set(m => m.HiddenSections, clean)
                    .Set(m => m.UpdatedAt, DateTime.UtcNow),
                cancellationToken: ct);
        }
    }
}
