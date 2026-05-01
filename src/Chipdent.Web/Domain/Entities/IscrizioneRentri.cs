using Chipdent.Web.Domain.Common;

namespace Chipdent.Web.Domain.Entities;

/// <summary>
/// Iscrizione di una clinica al RENTRI — Registro Elettronico Nazionale per la
/// Tracciabilità dei Rifiuti (D.M. 119/2023). Una clinica = una iscrizione.
/// Le credenziali sono opacizzate in lettura (solo l'utente autorizzato vede).
/// </summary>
public class IscrizioneRentri : TenantEntity
{
    public string ClinicaId { get; set; } = string.Empty;
    public DateTime? DataAttivazione { get; set; }

    /// <summary>Username/login portale RENTRI.</summary>
    public string? Username { get; set; }

    /// <summary>Password / token (in chiaro nel modello — la cifratura at-rest è demandata a Mongo/Vault).</summary>
    public string? Password { get; set; }

    /// <summary>Numero di iscrizione assegnato dal portale.</summary>
    public string? NumeroIscrizione { get; set; }

    public string? Note { get; set; }
}
