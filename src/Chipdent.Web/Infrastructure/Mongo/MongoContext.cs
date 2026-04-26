using Chipdent.Web.Domain.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

public class MongoContext
{
    private static int _conventionsRegistered;

    public IMongoDatabase Database { get; }

    public MongoContext(IOptions<MongoSettings> options)
    {
        if (Interlocked.Exchange(ref _conventionsRegistered, 1) == 0)
        {
            var pack = new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new EnumRepresentationConvention(MongoDB.Bson.BsonType.String),
                new IgnoreExtraElementsConvention(true)
            };
            ConventionRegistry.Register("ChipdentConventions", pack, _ => true);
        }

        var client = new MongoClient(options.Value.ConnectionString);
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoCollection<Tenant> Tenants => Database.GetCollection<Tenant>("tenants");
    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
    public IMongoCollection<Clinica> Cliniche => Database.GetCollection<Clinica>("cliniche");
    public IMongoCollection<Dottore> Dottori => Database.GetCollection<Dottore>("dottori");
    public IMongoCollection<Dipendente> Dipendenti => Database.GetCollection<Dipendente>("dipendenti");
    public IMongoCollection<Turno> Turni => Database.GetCollection<Turno>("turni");
    public IMongoCollection<Invito> Inviti => Database.GetCollection<Invito>("inviti");
}
