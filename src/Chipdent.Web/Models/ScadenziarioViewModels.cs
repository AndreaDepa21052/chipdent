namespace Chipdent.Web.Models;

public class ScadenziarioViewModel
{
    public IReadOnlyList<RigaScadenza> Scadenze { get; set; } = Array.Empty<RigaScadenza>();
    public int Orizzonte { get; set; } = 90;
    public int Scadute { get; set; }
    public int Imminenti { get; set; }
    public int Critiche { get; set; }
}

public record RigaScadenza(
    string Categoria,
    string Soggetto,
    DateTime Data,
    ImpattoScadenza Impatto,
    string DettaglioUrl,
    int ScoreUrgenza = 0);

public enum ImpattoScadenza
{
    Basso,
    Medio,
    Alto,
    Critico
}
