using Chipdent.Web.Infrastructure.Changelog;

namespace Chipdent.Web.Models;

public class WhatsNewViewModel
{
    public string CurrentVersion { get; set; } = string.Empty;
    public bool IsMvpReleased { get; set; }
    public IReadOnlyList<WhatsNewGroup> Groups { get; set; } = Array.Empty<WhatsNewGroup>();
}

public record WhatsNewGroup(string Header, DateTime Date, IReadOnlyList<ChangelogEntry> Entries);
