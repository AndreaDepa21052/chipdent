using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Chipdent.Web.Domain.Common;

public abstract class Entity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public abstract class TenantEntity : Entity
{
    public string TenantId { get; set; } = string.Empty;
}
