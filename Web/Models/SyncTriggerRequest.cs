using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace DevOps.GitHub.Sync.Web.Models;

/// <summary>
/// Request body posted by an Azure DevOps pipeline to trigger a GitHub sync.
/// </summary>
public sealed class SyncTriggerRequest
{
    /// <summary>Azure DevOps source repository URL (HTTPS clone URL).</summary>
    [Required]
    [JsonPropertyName("sourceRepoUrl")]
    public string SourceRepoUrl { get; set; } = string.Empty;

    /// <summary>
    /// Full HTTP Authorization header value used by the sync workflow to authenticate against
    /// the Azure DevOps REST API and git remote.
    /// Accepted formats:
    /// <list type="bullet">
    ///   <item><description><c>Bearer $(System.AccessToken)</c> – built-in ADO pipeline token (recommended)</description></item>
    ///   <item><description><c>Basic &lt;base64(:PAT)&gt;</c> – Personal Access Token; base64-encode <c>:&lt;PAT&gt;</c> (colon prefix, empty username)</description></item>
    /// </list>
    /// </summary>
    [Required]
    [JsonPropertyName("adoAuthorizationHeader")]
    public string AdoAuthorizationHeader { get; set; } = string.Empty;

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
