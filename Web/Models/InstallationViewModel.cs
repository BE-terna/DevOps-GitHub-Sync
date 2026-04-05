namespace DevOps.GitHub.Sync.Web.Models;

/// <summary>
/// View-model for the GitHub App post-installation redirect page (<c>GET /github/installed</c>).
/// </summary>
public class InstallationViewModel
{
    /// <summary>
    /// <c>true</c> when the GitHub API returned the installation details;
    /// <c>false</c> when the installation could not be found.
    /// </summary>
    public bool Found { get; set; }

    /// <summary>GitHub-assigned installation identifier passed as a query parameter.</summary>
    public long InstallationId { get; set; }

    /// <summary><c>install</c> or <c>update</c> – the action reported by GitHub.</summary>
    public string SetupAction { get; set; } = string.Empty;

    // ── Populated when Found = true ───────────────────────────────────────────

    /// <summary>Login (username or org) of the account that installed the app.</summary>
    public string? AccountLogin { get; set; }

    /// <summary>"User" or "Organization".</summary>
    public string? AccountType { get; set; }

    /// <summary>"all" or "selected".</summary>
    public string? RepositorySelection { get; set; }

    /// <summary>Timestamp when the installation was suspended, if applicable.</summary>
    public DateTimeOffset? SuspendedAt { get; set; }
}
