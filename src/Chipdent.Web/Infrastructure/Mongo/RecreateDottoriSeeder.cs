using Chipdent.Web.Domain.Entities;
using MongoDB.Driver;

namespace Chipdent.Web.Infrastructure.Mongo;

/// <summary>
/// Migrazione one-shot (richiesta utente 2026-05-15) che ripopola l'anagrafica
/// dottori dopo il <see cref="WipeAnagraficaSeeder"/>: inserisce i 97 dottori
/// dell'elenco fornitori Confident con Cognome, Nome ed Email professionale.
///
/// Per ogni dottore viene assegnato un codice sequenziale <c>D####</c> a
/// partire da D0001 (il wipe ha già azzerato la collezione, quindi non c'è
/// rischio di collisione con codici esistenti).
///
/// I dottori sono creati con <see cref="TipoContratto.Collaborazione"/> (default
/// per chi fattura allo studio): il successivo <see cref="Sepa.FornitoreOmbraService.SyncTenantAsync"/>,
/// invocato in <see cref="MongoSeeder"/>, creerà automaticamente i fornitori-ombra
/// collegati per riusare il modulo Tesoreria.
///
/// Trigger one-shot: la migrazione è marcata
/// <c>"recreate-dottori-2026-05-15"</c> in <see cref="Tenant.MigrazioniApplicate"/>.
/// </summary>
internal static class RecreateDottoriSeeder
{
    private const string MigrationKey = "recreate-dottori-2026-05-15";

    private static readonly (string Cognome, string Nome, string Email)[] Elenco =
    {
        ("Alimova",            "Mariya",                  "alimova@live.it"),
        ("Alorabi",            "Khaled",                  "khaled.alorabi@gmail.com"),
        ("Alvarez",            "Mark Joseph",             "markalvarez@icloud.com"),
        ("Amato",              "Armando",                 "armact1969@gmail.com"),
        ("Andolina",           "Alessia",                 "alessia.andolina@outlook.it"),
        ("Andraede Zambrano",  "Monica Monserrate",       "monica.andrade@outlook.it"),
        ("Argenton",           "",                        "silvern@email.it"),
        ("Arrigo",             "Anna",                    "anna_arrigo@hotmail.com"),
        ("Arrigo",             "Martina",                 "martinarrigodoc@icloud.com"),
        ("Baldi",              "Carmen",                  "carmenbaldi97@gmail.com"),
        ("Battistini",         "Chiara",                  "chiabat.mi@gmail.com"),
        ("Bertino",            "Debora",                  "deborabertino@hotmail.it"),
        ("Bico",               "Erblin",                  "erblin.bico@gmail.com"),
        ("Bonfanti",           "Elisa",                   "bonfanti.elisa15@gmail.com"),
        ("Borzì",              "Valeria Giulia",          "valeriagiulia.borz@yahoo.it"),
        ("Branca",             "Fabio",                   "brancafabio@me.com"),
        ("Bruno",              "Francesco",               "francesco.bruno9711@gmail.com"),
        ("Budelli",            "Giacomo Giuseppe",        "giacomo.budelli@gmail.com"),
        ("Butera",             "Vito",                    "drbutera.vito@gmail.com"),
        ("Campana",            "Amirano",                 "amiranocampana@libero.it"),
        ("Capotosto",          "Ilaria",                  "ilaria.capotosto@hotmail.it"),
        ("Castelli",           "Alessandra",              "alecastle9@hotmail.com"),
        ("Cazzaniga",          "Uberto Maria Pierugo",    "dottorcazzaniga80@gmail.com"),
        ("Citterio",           "Chiara",                  "chiaracitt@gmail.com"),
        ("Colitti",            "Stefania",                "colittistefania@gmail.com"),
        ("Colleoni",           "Fabio",                   "docfcolleoni@gmail.com"),
        ("Colombo",            "Chiara",                  "colombo.chi@gmail.com"),
        ("Conti",              "Ilaria",                  "conti.ilaria5@gmail.com"),
        ("Cristallo",          "Edoardo",                 "edoardoc78@icloud.com"),
        ("D'Anna",             "Francesco",               "francesco.danna@odontoiatricorosmini.it"),
        ("D'Apote",            "Anna",                    "anna.dapote.94@gmail.com"),
        ("Del Col",            "Gian Marco",              "gmdelcol@gmail.com"),
        ("Di Francesco",       "Matilde",                 "difrancesco.matilde@gmail.com"),
        ("D'Innella",          "Eugenia",                 "eugenia.dinnella@yahoo.it"),
        ("Draganti",           "Luca",                    "luca.draganti@outlook.it"),
        ("El Sayed",           "Ahmed",                   "dentahmed8@gmail.com"),
        ("Elsherif",           "Mohamed",                 "mohamedfathi656656@gmail.com"),
        ("Esti",               "Stefano",                 "stenosti@virgilio.it"),
        ("Faranda",            "Rosario",                 "rosario.faranda1@gmail.com"),
        ("Farioli",            "Stefano",                 "stefanodocfarioli@gmail.com"),
        ("Formenti",           "Jacopo",                  "jacopo.formenti@gmail.com"),
        ("Frignati",           "Luca",                    "l.frignati@libero.it"),
        ("Giganti",            "Alessandra",              "alessandra.giganti94@gmail.com"),
        ("Hassan",             "Naima Osman",             "osmanaima.hassan@gmail.com"),
        ("Hodzic",             "Nina",                    "ninahodzic00@gmail.com"),
        ("Jaffal",             "Wassim",                  "jaffalwassim@hotmail.com"),
        ("Kamh",               "Ahmed",                   "ahmed.kamh125@gmail.com"),
        ("Khalil",             "Antounious",              "antonios.khalil@gmail.com"),
        ("La Regina",          "Gaetano",                 "incisivo1@virgilio.it"),
        ("Lanza",              "Maurizio",                "mlanza@hotmail.it"),
        ("Lavagna",            "Amedeo",                  "drlavagna@libero.it"),
        ("Lazzari",            "Giuseppe",                "gi.lazzari1959@gmail.com"),
        ("Lerose",             "Daniele",                 "dani.lerose@virgilio.it"),
        ("Macovei",            "Felicia",                 "felicia.macovei97@gmail.com"),
        ("Maghsoudlou Rad",    "Moozhan",                 "moozhan_7366@yahoo.com"),
        ("Maiere",             "Daniele",                 "d.maiere@libero.it"),
        ("Malavasi",           "Roberto",                 "malavasi@hotmail.it"),
        ("Malveda",            "Mel Brenix",              "melbrenixmalveda10@gmail.com"),
        ("Manganaro",          "Giuseppe",                "g.manganaro15@gmail.com"),
        ("Maranan",            "Athena Mae",              "athena.maranan@gmail.com"),
        ("Marcì",              "Rachele",                 "rachelemar@gmail.com"),
        ("Mazzullo",           "Filippo Lupo",            "lupo.mazzullo@gmail.com"),
        ("Mezzanzanica",       "Claudia",                 "claudia_mezzanzanica@libero.it"),
        ("Monti",              "Beatrice",                "beatricemonti86@gmail.com"),
        ("Morsilli",           "Alessandro",              "dott.morsilli@gmail.com"),
        ("Morsilli",           "Marco",                   "m.morsilli@gmail.com"),
        ("Mucllari",           "Silvano",                 "silvanomucllari@gmail.com"),
        ("Muscianisi",         "Ilaria",                  "i.muscianisi@hotmail.com"),
        ("Nisi",               "Francesco",               "nisi.francescogiuseppe@hsr.it"),
        ("Novelli",            "Giuliana",                "gnovelli59@libero.it"),
        ("Orlando",            "Benedetta",               "benedetta.orlando@hotmail.it"),
        ("Ornago",             "Laura",                   "ornagolaura@gmail.com"),
        ("Paglioli",           "Edoardo",                 "edoardo.paglioli1@gmail.com"),
        ("Panizza",            "Sergio",                  "sergioptravel@gmail.com"),
        ("Paroni",             "Lorenzo Piero",           "lorenzopiero.paroni@gmail.com"),
        ("Piccirillo",         "Giovan Battista",         "giovanbpiccirillo@gmail.com"),
        ("Pilar",              "Iglesias",                "pilar03ig@yahoo.it"),
        ("Pirovano",           "Marica",                  "marika.newzucca@gmail.com"),
        ("Pisotti",            "Chiara",                  "chiarapisotti1994@gmail.com"),
        ("Porcari",            "Serena",                  "porcari.serena@gmail.com"),
        ("Pozzi",              "Antonio Mario",           "antonio.pozzi1956@tiscali.it"),
        ("Provenzano",         "Daniele",                 "danprove@gmail.com"),
        ("Provenzano",         "Pasquale Antonio",        "paprove59@gmail.com"),
        ("Rigobello",          "Matteo Maria",            "matteorigobello4@gmail.com"),
        ("Rodilosso",          "Giorgia",                 "gio.rodilosso@gmail.com"),
        ("Roganti",            "Paolo",                   "paolorog@yahoo.it"),
        ("Rogora",             "Gabriele",                "rogoralele@libero.it"),
        ("Sanarico",           "Edoardo",                 "edoardo.sanarico@icloud.com"),
        ("Scudieri",           "Luigi",                   "luigiscudieri2@gmail.com"),
        ("Scudieri",           "Nicola",                  "nicola.scudieri95@gmail.com"),
        ("Studio Tredici Cecchin", "",                    "segreteria@studiotredicicecchin.it"),
        ("Tibichi",            "Edwin Flavius",           "etibichi@yahoo.com"),
        ("Traini",             "Mauro",                   "maurotraini59@gmail.com"),
        ("Turceninoff",        "Tatiana",                 "turceninoff.tatiana@gmail.com"),
        ("Ugarte Tacca",       "Bruno Alejandro",         "ugartebruno@gmail.com"),
        ("Vedovato",           "Alessandro",              "a.vedovato00@gmail.com"),
        ("Vignola",            "Michele",                 "micheleh725@libero.it"),
    };

    public static async Task SeedAsync(
        MongoContext ctx,
        Tenant tenant,
        ILogger logger,
        CancellationToken ct)
    {
        if (tenant.MigrazioniApplicate.Contains(MigrationKey)) return;

        var tid = tenant.Id;
        var now = DateTime.UtcNow;
        var dottori = new List<Dottore>(Elenco.Length);

        for (var i = 0; i < Elenco.Length; i++)
        {
            var (cognome, nome, email) = Elenco[i];
            dottori.Add(new Dottore
            {
                TenantId = tid,
                Codice = $"D{i + 1:D4}",
                Cognome = cognome,
                Nome = nome,
                Email = email,
                TipoContratto = TipoContratto.Collaborazione,
                DataAssunzione = now,
                Attivo = true,
                CreatedAt = now
            });
        }

        await ctx.Dottori.InsertManyAsync(dottori, cancellationToken: ct);

        await ctx.Tenants.UpdateOneAsync(
            t => t.Id == tid,
            Builders<Tenant>.Update.AddToSet(t => t.MigrazioniApplicate, MigrationKey),
            cancellationToken: ct);
        tenant.MigrazioniApplicate.Add(MigrationKey);

        logger.LogInformation(
            "RecreateDottori: inseriti {N} dottori dall'elenco FORNITORI Confident (codici D0001..D{Last:D4})",
            dottori.Count, dottori.Count);
    }
}
