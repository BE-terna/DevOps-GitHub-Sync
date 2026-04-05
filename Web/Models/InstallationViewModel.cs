namespace DevOps.GitHub.Sync.Web.Models;

/// <summary>
/// Describes how source-repository approval is configured (or needs to be configured)
/// for a given installation, shown on the post-installation page.
/// </summary>
public enum SourceApprovalSetupStatus
{
    /// <summary>
    /// The installation has <c>Actions variables: read</c> permission.
    /// Use the <c>DEVOPS_GITHUB_SYNC_SOURCES</c> repository variable.
    /// </summary>
    HasVariablesPermission,

    /// <summary>
    /// No variables permission; the organisation has already defined the
    /// <c>GitSyncSource</c> custom property in its schema.
    /// The property value must now be set on each target repository.
    /// </summary>
    OrgPropertyDefined,

    /// <summary>
    /// No variables permission; the organisation has <em>not</em> defined the
    /// <c>GitSyncSource</c> custom property yet.
    /// It must be created in the organisation's custom properties settings first.
    /// </summary>
    OrgPropertyNotDefined,

    /// <summary>
    /// The app is installed on a personal (non-organisation) account.
    /// GitHub custom properties are only available for organisation-owned repositories,
    /// so the <c>Actions variables: read</c> permission is required.
    /// </summary>
    UserAccountUnsupported,

    /// <summary>
    /// The organisation schema could not be read (likely a missing permission).
    /// The user should either grant the required permission or verify manually.
    /// </summary>
    SchemaCheckFailed,
}

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

    /// <summary>
    /// <c>true</c> when the installation has been granted
    /// <c>Repository permissions → Actions variables: read</c>.
    /// When <c>false</c> the <c>GitSyncSource</c> custom property path is used instead.
    /// </summary>
    public bool HasVariablesReadPermission { get; set; }

    /// <summary>
    /// Describes what the user must do to enable source-repository approval for this installation.
    /// Only meaningful when <see cref="Found"/> is <c>true</c>.
    /// </summary>
    public SourceApprovalSetupStatus ApprovalSetupStatus { get; set; }
}
