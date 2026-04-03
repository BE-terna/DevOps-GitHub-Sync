using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace DevOps.GitHub.Sync.Web.Models;

/// <summary>
/// Request body posted by an Azure DevOps pipeline to trigger a GitHub sync.
/// </summary>
public sealed class SyncTriggerRequest
{
    /// <summary>
    /// Non-guessable API key tied to the GitHub App installation.
    /// Obtain this value from the installation record (e.g. via an admin page or
    /// the webhook upsert log). It proves the caller is authorised to dispatch to
    /// the target repository without requiring the app's private key.
    /// </summary>
    [Required]
    [JsonPropertyName("installationApiKey")]
    public string InstallationApiKey { get; set; } = string.Empty;

    /// <summary>Azure DevOps source repository URL (HTTPS clone URL).</summary>
    [Required]
    [JsonPropertyName("sourceRepoUrl")]
    public string SourceRepoUrl { get; set; } = string.Empty;

    /// <summary>Azure DevOps <c>$(System.AccessToken)</c> for authenticating against the ADO API.</summary>
    [Required]
    [JsonPropertyName("systemAccessToken")]
    public string SystemAccessToken { get; set; } = string.Empty;

    /// <summary>Azure DevOps pull-request identifier.</summary>
    [Required]
    [JsonPropertyName("pullRequestId")]
    public string PullRequestId { get; set; } = string.Empty;

    /// <summary>Commit SHA to sync.</summary>
    [Required]
    [JsonPropertyName("commitId")]
    public string CommitId { get; set; } = string.Empty;

    /// <summary>Target GitHub repository in <c>owner/repo</c> format.</summary>
    [Required]
    [JsonPropertyName("targetGitHubRepo")]
    public string TargetGitHubRepo { get; set; } = string.Empty;

    /// <summary>Branch name of the pull request (e.g. <c>feature/my-feature</c>).</summary>
    [JsonPropertyName("branchName")]
    public string? BranchName { get; set; }

    /// <summary>Pull-request title used when creating the GitHub PR.</summary>
    [JsonPropertyName("prTitle")]
    public string? PrTitle { get; set; }

    /// <summary>Pull-request body/description used when creating the GitHub PR.</summary>
    [JsonPropertyName("prBody")]
    public string? PrBody { get; set; }

    /// <summary>Base branch for the GitHub PR (defaults to <c>main</c>).</summary>
    [JsonPropertyName("targetBranch")]
    public string TargetBranch { get; set; } = "main";
}
