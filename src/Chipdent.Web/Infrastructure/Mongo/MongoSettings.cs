namespace Chipdent.Web.Infrastructure.Mongo;

public class MongoSettings
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://chipdent:chipdent@localhost:27017";
    public string Database { get; set; } = "chipdent";
}
