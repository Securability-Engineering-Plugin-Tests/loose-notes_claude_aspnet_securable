namespace LooseNotes.Web.Data.Entities;

// Curated allowlist of security questions. The user picks an Id; we never accept
// a free-text question from the client.
public class SecurityQuestion
{
    public string Id { get; set; } = default!;
    public string Text { get; set; } = default!;
}
