using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DevOps.GitHub.Sync.Data.Entities;

/// <summary>
/// Represents a GitHub App installation. Stores the installation identifier and the
/// short-lived installation access token that is used to authenticate API calls on
/// behalf of the installed account/repository.
/// </summary>
[Table("GitHubInstallations")]
public class GitHubInstallation
{
    [Key]
    public int Id { get; set; }

    /// <summary>GitHub-assigned installation identifier.</summary>
    [Required]
    public long InstallationId { get; set; }

    /// <summary>Login (username or organization name) of the account that installed the app.</summary>
    [Required]
    [MaxLength(200)]
    public string AccountLogin { get; set; } = string.Empty;

    /// <summary>"User" or "Organization".</summary>
    [Required]
    [MaxLength(50)]
    public string AccountType { get; set; } = string.Empty;

    /// <summary>"all" or "selected".</summary>
    [Required]
    [MaxLength(50)]
    public string RepositorySelection { get; set; } = string.Empty;

    /// <summary>Current short-lived installation access token.</summary>
    [MaxLength(500)]
    public string? AccessToken { get; set; }

    /// <summary>UTC expiry of the current access token.</summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>Raw JSON of the installation webhook payload for reference.</summary>
    public string? RawPayload { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
