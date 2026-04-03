namespace DevOps.GitHub.Sync.Web.Options;

/// <summary>
/// Configuration options for the GitHub App (AppId, private key, webhook secret).
/// Bind from the "GitHubApp" section in appsettings / user secrets / environment variables.
/// </summary>
public sealed class GitHubAppOptions
{
    public const string SectionName = "GitHubApp";

    /// <summary>Numeric GitHub App ID shown on the App settings page.</summary>
    public long AppId { get; set; }

    /// <summary>
    /// PEM-encoded RSA private key generated for the GitHub App.
    /// Store securely (user secrets / Key Vault). New lines may be represented as \n.
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>Webhook secret configured on the GitHub App settings page.</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
