using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DevOps.GitHub.Sync.Data.Entities;

/// <summary>
/// Audit record for every sync request received from an Azure DevOps pipeline.
/// </summary>
[Table("SyncRequests")]
public class SyncRequest
{
    [Key]
    public int Id { get; set; }

    /// <summary>Azure DevOps source repository URL.</summary>
    [Required]
    [MaxLength(500)]
    public string SourceRepoUrl { get; set; } = string.Empty;

    /// <summary>Target GitHub repository in "owner/repo" format.</summary>
    [Required]
    [MaxLength(300)]
    public string TargetGitHubRepo { get; set; } = string.Empty;

    /// <summary>Azure DevOps pull-request identifier.</summary>
    [Required]
    [MaxLength(50)]
    public string PullRequestId { get; set; } = string.Empty;

    /// <summary>Branch name for the pull request.</summary>
    [MaxLength(300)]
    public string? BranchName { get; set; }

    /// <summary>Source commit identifier.</summary>
    [Required]
    [MaxLength(100)]
    public string CommitId { get; set; } = string.Empty;

    /// <summary>Final processing status: Pending, Dispatched, Failed.</summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    /// <summary>Error detail when Status = Failed.</summary>
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
