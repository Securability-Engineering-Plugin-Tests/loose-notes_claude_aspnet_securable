namespace LooseNotes.Services;

public interface IShareTokenService
{
    /// <summary>Generates a cryptographically random share token.</summary>
    string GenerateToken();
}
