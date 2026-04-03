using DevOps.GitHub.Sync.Data;
using DevOps.GitHub.Sync.Data.Entities;
using DevOps.GitHub.Sync.Web.Models;
using DevOps.GitHub.Sync.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DevOps.GitHub.Sync.Web.Controllers;

/// <summary>
/// API endpoint called by an Azure DevOps pipeline to trigger a GitHub sync.
/// </summary>
[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private const string SyncWorkflowFileName = "Sync-DevOps-GitHub.yml";
    private const string SyncEventType = "Sync-DevOps-GitHub";

    private readonly AppDbContext _db;
    private readonly GitHubAppService _gitHub;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        AppDbContext db,
        GitHubAppService gitHub,
        ILogger<SyncController> logger)
    {
        _db = db;
        _gitHub = gitHub;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a sync request from an Azure DevOps pipeline and triggers the
    /// <c>Sync-DevOps-GitHub</c> workflow via a <c>repository_dispatch</c> event.
    /// </summary>
    /// <remarks>
    /// Expected JSON body: <see cref="SyncTriggerRequest"/>.
    /// Returns 200 on success, 403 if the app has no permission on the target repo,
    /// 404 if the workflow file does not exist in the target repo.
    /// </remarks>
    [HttpPost("trigger")]
    public async Task<IActionResult> Trigger(
        [FromBody] SyncTriggerRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Parse owner/repo from the target
        var parts = request.TargetGitHubRepo.Split('/', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            return BadRequest("targetGitHubRepo must be in 'owner/repo' format.");

        var owner = parts[0];
        var repo = parts[1];

        // ── Audit record ───────────────────────────────────────────────────────
        var auditRecord = new SyncRequest
        {
            SourceRepoUrl = request.SourceRepoUrl,
            TargetGitHubRepo = request.TargetGitHubRepo,
            PullRequestId = request.PullRequestId,
            CommitId = request.CommitId,
            BranchName = request.BranchName,
            Status = "Pending",
        };
        _db.SyncRequests.Add(auditRecord);
        await _db.SaveChangesAsync(ct);

        try
        {
            // ── Find installation that has access to the target repo ────────────
            var installation = await _gitHub.FindInstallationForRepoAsync(owner, repo, ct);
            if (installation is null)
            {
                _logger.LogWarning(
                    "No GitHub App installation found with access to {Repo}.",
                    Sanitize(request.TargetGitHubRepo));

                auditRecord.Status = "Failed";
                auditRecord.ErrorMessage =
                    $"The GitHub App is not installed on {request.TargetGitHubRepo} or does not have access.";
                auditRecord.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                return StatusCode(403, auditRecord.ErrorMessage);
            }

            // ── Get installation access token ─────────────────────────────────
            var accessToken = await _gitHub.GetInstallationAccessTokenAsync(installation, ct);

            // ── Verify workflow exists ────────────────────────────────────────
            var workflowExists = await _gitHub.WorkflowExistsAsync(
                owner, repo, SyncWorkflowFileName, accessToken, ct);

            if (!workflowExists)
            {
                var msg = $"Workflow '{SyncWorkflowFileName}' not found in {request.TargetGitHubRepo}.";
                _logger.LogWarning(
                    "Workflow '{WorkflowFile}' not found in {Repo}.",
                    SyncWorkflowFileName,
                    Sanitize(request.TargetGitHubRepo));

                auditRecord.Status = "Failed";
                auditRecord.ErrorMessage = msg;
                auditRecord.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                return NotFound(msg);
            }

            // ── Build dispatch payload ────────────────────────────────────────
            var payload = new
            {
                source_repo_url = request.SourceRepoUrl,
                ado_token = request.SystemAccessToken,
                pull_request_id = request.PullRequestId,
                commit_id = request.CommitId,
                branch_name = request.BranchName ?? $"pr/{request.PullRequestId}",
                pr_title = request.PrTitle ?? $"Sync PR {request.PullRequestId} from Azure DevOps",
                pr_body = request.PrBody ?? string.Empty,
                target_branch = request.TargetBranch,
            };

            // ── Trigger repository_dispatch ───────────────────────────────────
            await _gitHub.TriggerRepositoryDispatchAsync(
                owner, repo, accessToken, SyncEventType, payload, ct);

            _logger.LogInformation(
                "Dispatched '{Event}' to {Repo} for PR {PrId}.",
                SyncEventType,
                Sanitize(request.TargetGitHubRepo),
                Sanitize(request.PullRequestId));

            auditRecord.Status = "Dispatched";
            auditRecord.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(new { message = "Sync dispatched successfully.", syncRequestId = auditRecord.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing sync trigger for PR {PrId}.",
                Sanitize(request.PullRequestId));

            auditRecord.Status = "Failed";
            auditRecord.ErrorMessage = ex.Message;
            auditRecord.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            return StatusCode(500, "An error occurred while processing the sync request.");
        }
    }

    /// <summary>Removes newline characters from a user-supplied value before it is written to a log.</summary>
    private static string Sanitize(string value) =>
        value.Replace("\r", string.Empty, StringComparison.Ordinal)
             .Replace("\n", string.Empty, StringComparison.Ordinal);
}
