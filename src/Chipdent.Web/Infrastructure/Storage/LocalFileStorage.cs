namespace Chipdent.Web.Infrastructure.Storage;

/// <summary>
/// Salva i file localmente sotto wwwroot/uploads/{tenantId}/{folder}/{guid_safeName}.
/// I file sono serviti via UseStaticFiles → URL pubblico /uploads/...
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalFileStorage> _log;
    public const string UploadsRoot = "uploads";

    public LocalFileStorage(IWebHostEnvironment env, ILogger<LocalFileStorage> log)
    {
        _env = env;
        _log = log;
    }

    public async Task<StoredFile> SaveAsync(string tenantId, string folder, string fileName, Stream content, string? contentType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId required", nameof(tenantId));

        var safeFolder = Sanitize(folder);
        var safeName = Sanitize(Path.GetFileNameWithoutExtension(fileName));
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "";

        var unique = $"{Guid.NewGuid():N}-{safeName}{ext}";
        var relative = Path.Combine(UploadsRoot, tenantId, safeFolder, unique).Replace('\\', '/');

        var absDir = Path.Combine(_env.WebRootPath, UploadsRoot, tenantId, safeFolder);
        Directory.CreateDirectory(absDir);

        var absPath = Path.Combine(absDir, unique);
        await using (var fs = File.Create(absPath))
        {
            await content.CopyToAsync(fs, ct);
        }

        var size = new FileInfo(absPath).Length;
        _log.LogInformation("Saved upload {Path} ({Size} bytes)", relative, size);
        return new StoredFile(relative, "/" + relative, size, fileName);
    }

    public Task<bool> DeleteAsync(string tenantId, string relativePath, CancellationToken ct = default)
    {
        // Sicurezza: il path deve restare sotto uploads/{tenantId}/.
        var prefix = Path.Combine(UploadsRoot, tenantId).Replace('\\', '/') + "/";
        if (!relativePath.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
        {
            _log.LogWarning("Refused to delete outside tenant scope: {Path}", relativePath);
            return Task.FromResult(false);
        }
        var abs = Path.Combine(_env.WebRootPath, relativePath);
        if (File.Exists(abs))
        {
            File.Delete(abs);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public string GetPublicUrl(string relativePath) => "/" + relativePath.Replace('\\', '/');

    private static string Sanitize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "_";
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', ' ' }).ToHashSet();
        var clean = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        clean = clean.Trim('_', '.');
        return string.IsNullOrEmpty(clean) ? "_" : clean[..Math.Min(clean.Length, 80)];
    }
}
