namespace YouplanAdminTool.Core.Abstractions;

/// <summary>Beschafft und erneuert das OAuth2-Access-Token für die Planday Open API.</summary>
public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Verwirft ein zwischengespeichertes Token, z.B. nach einer 401-Antwort, damit der nächste Aufruf ein frisches Token anfordert.</summary>
    void Invalidate();
}
