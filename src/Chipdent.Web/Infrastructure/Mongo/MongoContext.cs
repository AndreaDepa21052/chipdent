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
    public IMongoCollection<CorrezioneTimbratura> CorrezioniTimbrature => Database.GetCollection<CorrezioneTimbratura>("correzioniTimbrature");
    public IMongoCollection<ApprovazioneTimesheet> ApprovazioniTimesheet => Database.GetCollection<ApprovazioneTimesheet>("approvazioniTimesheet");
    public IMongoCollection<SegnalazioneWhistleblowing> Whistleblowing => Database.GetCollection<SegnalazioneWhistleblowing>("whistleblowing");
    public IMongoCollection<RichiestaAssistenza> RichiesteAssistenza => Database.GetCollection<RichiestaAssistenza>("richiesteAssistenza");
    public IMongoCollection<FeedbackPaziente> FeedbackPazienti => Database.GetCollection<FeedbackPaziente>("feedbackPazienti");
    public IMongoCollection<RondaSicurezza> RondeSicurezza => Database.GetCollection<RondaSicurezza>("rondeSicurezza");
    public IMongoCollection<Consumabile> Consumabili => Database.GetCollection<Consumabile>("consumabili");
    public IMongoCollection<MovimentoConsumabile> MovimentiConsumabili => Database.GetCollection<MovimentoConsumabile>("movimentiConsumabili");
    public IMongoCollection<ProductBacklogItem> ProductBacklog => Database.GetCollection<ProductBacklogItem>("productBacklog");
    public IMongoCollection<Fornitore> Fornitori => Database.GetCollection<Fornitore>("fornitori");
    public IMongoCollection<FatturaFornitore> Fatture => Database.GetCollection<FatturaFornitore>("fatture");
    public IMongoCollection<ScadenzaPagamento> ScadenzePagamento => Database.GetCollection<ScadenzaPagamento>("scadenzePagamento");
    public IMongoCollection<DistintaPagamento> DistinteSepa => Database.GetCollection<DistintaPagamento>("distinteSepa");
    public IMongoCollection<CashflowSettings> CashflowSettings => Database.GetCollection<CashflowSettings>("cashflowSettings");
    public IMongoCollection<EntrataAttesa> EntrateAttese => Database.GetCollection<EntrataAttesa>("entrateAttese");
    public IMongoCollection<DistaccoDipendente> Distacchi => Database.GetCollection<DistaccoDipendente>("distacchi");
    public IMongoCollection<IscrizioneRentri> Rentri => Database.GetCollection<IscrizioneRentri>("rentri");
    public IMongoCollection<ProtocolloClinica> ProtocolliClinica => Database.GetCollection<ProtocolloClinica>("protocolliClinica");
    public IMongoCollection<InterventoClinica> InterventiClinica => Database.GetCollection<InterventoClinica>("interventiClinica");
    public IMongoCollection<MenuVisibility> MenuVisibilities => Database.GetCollection<MenuVisibility>("menuVisibilities");
    public IMongoCollection<ImportFatturePassiveBatch> ImportFattureBatches => Database.GetCollection<ImportFatturePassiveBatch>("importFattureBatches");
    public IMongoCollection<ImportFatturaRiga> ImportFattureRighe => Database.GetCollection<ImportFatturaRiga>("importFattureRighe");
    public IMongoCollection<PropostaAnagraficaFornitore> ProposteAnagraficaFornitori => Database.GetCollection<PropostaAnagraficaFornitore>("proposteAnagraficaFornitori");
    public IMongoCollection<ProcedimentoDisciplinare> Disciplinari => Database.GetCollection<ProcedimentoDisciplinare>("disciplinari");
    public IMongoCollection<PremioDipendente> Premi => Database.GetCollection<PremioDipendente>("premiDipendenti");
    public IMongoCollection<SchedaValutazione> Valutazioni => Database.GetCollection<SchedaValutazione>("valutazioniDipendenti");
    public IMongoCollection<VisitaMysteryClient> MysteryClient => Database.GetCollection<VisitaMysteryClient>("mysteryClient");
    public IMongoCollection<DocumentoDipendente> DocumentiDipendente => Database.GetCollection<DocumentoDipendente>("documentiDipendente");
    public IMongoCollection<CambioLivelloRetribuzione> CambiLivello => Database.GetCollection<CambioLivelloRetribuzione>("cambiLivelloDipendenti");
    public IMongoCollection<CambioMansioneReparto> CambiMansione => Database.GetCollection<CambioMansioneReparto>("cambiMansioneDipendenti");
    public IMongoCollection<CollaborazioneClinica> CollaborazioniDottori => Database.GetCollection<CollaborazioneClinica>("collaborazioniDottori");
    public IMongoCollection<DocumentoDottore> DocumentiDottore => Database.GetCollection<DocumentoDottore>("documentiDottore");
    public IMongoCollection<AttestatoEcm> AttestatiEcm => Database.GetCollection<AttestatoEcm>("attestatiEcm");
}
