namespace Chipdent.Web.Infrastructure.Storage;

/// <summary>
/// Astrazione per lo storage file. L'implementazione di default (LocalFileStorage)
/// salva sotto wwwroot/uploads/{tenantId}/{cartella}/{file}.
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(string tenantId, string folder, string fileName, Stream content, string? contentType, CancellationToken ct = default);
    Task<bool> DeleteAsync(string tenantId, string relativePath, CancellationToken ct = default);
    string GetPublicUrl(string relativePath);
}

public record StoredFile(string RelativePath, string PublicUrl, long SizeBytes, string OriginalFileName);
