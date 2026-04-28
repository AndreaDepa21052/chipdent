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
    public IMongoCollection<VisitaMedica> VisiteMediche => Database.GetCollection<VisitaMedica>("visiteMediche");
    public IMongoCollection<Corso> Corsi => Database.GetCollection<Corso>("corsi");
    public IMongoCollection<DVR> DVRs => Database.GetCollection<DVR>("dvrs");
    public IMongoCollection<DocumentoClinica> DocumentiClinica => Database.GetCollection<DocumentoClinica>("documentiClinica");
    public IMongoCollection<Comunicazione> Comunicazioni => Database.GetCollection<Comunicazione>("comunicazioni");
    public IMongoCollection<Trasferimento> Trasferimenti => Database.GetCollection<Trasferimento>("trasferimenti");
    public IMongoCollection<AuditEntry> Audit => Database.GetCollection<AuditEntry>("audit");
    public IMongoCollection<RichiestaFerie> RichiesteFerie => Database.GetCollection<RichiestaFerie>("richiesteFerie");
    public IMongoCollection<TurnoTemplate> TurniTemplate => Database.GetCollection<TurnoTemplate>("turniTemplate");
    public IMongoCollection<Messaggio> Messaggi => Database.GetCollection<Messaggio>("messaggi");
    public IMongoCollection<SogliaCopertura> SoglieCopertura => Database.GetCollection<SogliaCopertura>("soglieCopertura");
    public IMongoCollection<CategoriaDocumentoObbligatoria> CategorieDocumentoObbligatorie => Database.GetCollection<CategoriaDocumentoObbligatoria>("categorieDocumentoObbligatorie");
    public IMongoCollection<WorkflowConfiguration> WorkflowConfigs => Database.GetCollection<WorkflowConfiguration>("workflowConfigs");
    public IMongoCollection<Contratto> Contratti => Database.GetCollection<Contratto>("contratti");
    public IMongoCollection<RichiestaCambioTurno> RichiesteCambioTurno => Database.GetCollection<RichiestaCambioTurno>("richiesteCambioTurno");
    public IMongoCollection<Segnalazione> Segnalazioni => Database.GetCollection<Segnalazione>("segnalazioni");
    public IMongoCollection<RichiestaSostituzione> Sostituzioni => Database.GetCollection<RichiestaSostituzione>("sostituzioni");
    public IMongoCollection<Dpi> Dpi => Database.GetCollection<Dpi>("dpi");
    public IMongoCollection<ConsegnaDpi> ConsegneDpi => Database.GetCollection<ConsegnaDpi>("consegneDpi");
    public IMongoCollection<Timbratura> Timbrature => Database.GetCollection<Timbratura>("timbrature");
}
