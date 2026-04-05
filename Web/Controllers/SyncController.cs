using DevOps.GitHub.Sync.Web.Models;
using DevOps.GitHub.Sync.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace DevOps.GitHub.Sync.Web.Controllers;

/// <summary>
/// API endpoint called by an Azure DevOps pipeline to trigger a GitHub sync.
/// </summary>
[ApiController]
[Route("api/sync")]
public class SyncController(
    GitHubAppService gitHub,
    ILogger<SyncController> logger) : ControllerBase
{
    internal const string ActivitySourceName = "DevOps.GitHub.Sync";

    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    private const string SyncWorkflowFileName = "Sync-DevOps-GitHub.yml";
    private const string SyncEventType = "Sync-DevOps-GitHub";

    /// <summary>
    /// Name of the GitHub Actions repository variable that holds the allowed
    /// Azure DevOps source repository URLs (one per line).
    /// The GitHub App installation must have the <c>Variables: Read</c> permission.
    /// </summary>
    private const string AllowedSourcesVariableName = "DEVOPS_GITHUB_SYNC_SOURCES";

    /// <summary>
    /// Name of the GitHub custom repository property that holds the allowed
    /// Azure DevOps source repository URLs.
    /// Used as a fallback when the installation does not have
    /// <c>Actions variables: read</c> permission.
    /// Requires the organisation to have defined this property in its schema and
    /// the <c>metadata: read</c> permission (always included).
    /// </summary>
    private const string GitSyncSourcePropertyName = "GitSyncSource";

    /// <summary>
    /// Accepts a sync request from an Azure DevOps pipeline and triggers the
    /// <c>Sync-DevOps-GitHub</c> workflow via a <c>repository_dispatch</c> event.
    /// </summary>
    /// <remarks>
    /// Expected JSON body: <see cref="SyncTriggerRequest"/>.
    ///
    /// Authorization: the target GitHub repository must contain a repository
    /// Actions variable named <c>DEVOPS_GITHUB_SYNC_SOURCES</c> whose value is a
    /// newline-separated list of allowed Azure DevOps source repository URLs.
    /// The request is accepted only when <c>sourceRepoUrl</c> appears in that list.
    ///
    /// Returns 200 on success, 403 if no installation covers the target repo or the
    /// source URL is not in the allowed list, 404 if the workflow file does not exist.
    /// </remarks>
    [HttpPost("trigger")]
    public async Task<IActionResult> Trigger(
        [FromBody] SyncTriggerRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // ── Parse owner/repo from the target ──────────────────────────────────
        var parts = request.TargetGitHubRepo.Split('/', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return BadRequest("targetGitHubRepo must be in 'owner/repo' format.");
        }

        var owner = parts[0];
        var repo = parts[1];

        using var activity = s_activitySource.StartActivity("sync.trigger");
        activity?.SetTag("sync.source_repo", request.SourceRepoUrl);
        activity?.SetTag("sync.target_repo", request.TargetGitHubRepo);
        activity?.SetTag("sync.pull_request_id", request.PullRequestId);
        activity?.SetTag("sync.commit_id", request.CommitId);
        activity?.SetTag("sync.branch_name", request.BranchName);

        // ── Find the GitHub App installation for the target repo ──────────────
        var installation = await gitHub.GetInstallationForRepoAsync(owner, repo, ct);
        if (installation is null)
        {
            logger.LogWarning(
                "Sync trigger rejected – no installation found for {Repo}.",
                Sanitize(request.TargetGitHubRepo));

            activity?.SetTag("sync.status", "Rejected");
            activity?.SetStatus(ActivityStatusCode.Error, "No GitHub App installation found");

            return StatusCode(403,
                $"No GitHub App installation found that covers {request.TargetGitHubRepo}.");
        }

        activity?.SetTag("sync.installation_id", installation.Id);

        // ── Obtain an installation access token ───────────────────────────────
        var accessToken = await gitHub.GetInstallationAccessTokenAsync(installation.Id, ct);

        // ── Validate source repository authorisation ──────────────────────────
        // Preferred path: read the DEVOPS_GITHUB_SYNC_SOURCES Actions variable.
        // Fallback path (when the installation lacks Variables: read permission):
        //   read the GitSyncSource custom repository property.
        if (installation.HasVariablesReadPermission)
        {
            var variableValue = await gitHub.GetRepoVariableAsync(
                owner, repo, AllowedSourcesVariableName, accessToken, ct);

            if (variableValue is null)
            {
                logger.LogWarning(
                    "Sync trigger rejected – variable '{Variable}' not found in {Repo}.",
                    AllowedSourcesVariableName,
                    Sanitize(request.TargetGitHubRepo));

                activity?.SetTag("sync.status", "Rejected");
                activity?.SetStatus(ActivityStatusCode.Error, $"Variable '{AllowedSourcesVariableName}' not found");

                return StatusCode(403,
                    $"Repository variable '{AllowedSourcesVariableName}' not found in {request.TargetGitHubRepo}. " +
                    "Add the variable with the allowed Azure DevOps source repository URLs (one per line).");
            }

            var result = CheckSourceAllowed(variableValue, request.SourceRepoUrl);
            if (result is not null)
            {
                logger.LogWarning(
                    "Sync trigger rejected – source repo is not in the allowed list for {Repo}.",
                    Sanitize(request.TargetGitHubRepo));

                activity?.SetTag("sync.status", "Rejected");
                activity?.SetStatus(ActivityStatusCode.Error, "Source repository not in allowed list");

                return StatusCode(403,
                    $"Source repository is not listed in '{AllowedSourcesVariableName}' " +
                    $"for {request.TargetGitHubRepo}.");
            }
        }
        else
        {
            // Fallback: custom repository property (metadata read is always included).
            var properties = await gitHub.GetRepoCustomPropertiesAsync(owner, repo, accessToken, ct);

            if (!properties.TryGetValue(GitSyncSourcePropertyName, out var gitSyncSourceValue)
                || gitSyncSourceValue is null)
            {
                logger.LogWarning(
                    "Sync trigger rejected – custom property '{Property}' not set on {Repo}.",
                    GitSyncSourcePropertyName,
                    Sanitize(request.TargetGitHubRepo));

                activity?.SetTag("sync.status", "Rejected");
                activity?.SetStatus(ActivityStatusCode.Error, $"Custom property '{GitSyncSourcePropertyName}' not set");

                return StatusCode(403,
                    $"The installation does not have 'Actions variables: read' permission and the " +
                    $"'{GitSyncSourcePropertyName}' custom property is not set on {request.TargetGitHubRepo}. " +
                    "Either grant the 'Actions variables: read' permission to the GitHub App installation, " +
                    $"or set the '{GitSyncSourcePropertyName}' custom property on the repository " +
                    "with the allowed Azure DevOps source repository URLs (one per line).");
            }

            var result = CheckSourceAllowed(gitSyncSourceValue, request.SourceRepoUrl);
            if (result is not null)
            {
                logger.LogWarning(
                    "Sync trigger rejected – source repo is not in the '{Property}' custom property for {Repo}.",
                    GitSyncSourcePropertyName,
                    Sanitize(request.TargetGitHubRepo));

                activity?.SetTag("sync.status", "Rejected");
                activity?.SetStatus(ActivityStatusCode.Error, "Source repository not in allowed list");

                return StatusCode(403,
                    $"Source repository is not listed in the '{GitSyncSourcePropertyName}' custom property " +
                    $"for {request.TargetGitHubRepo}.");
            }
        }

        try
        {
            // ── Verify workflow exists ────────────────────────────────────────
            var workflowExists = await gitHub.WorkflowExistsAsync(
                owner, repo, SyncWorkflowFileName, accessToken, ct);

            if (!workflowExists)
            {
                var msg = $"Workflow '{SyncWorkflowFileName}' not found in {request.TargetGitHubRepo}.";
                logger.LogWarning(
                    "Workflow '{WorkflowFile}' not found in {Repo}.",
                    SyncWorkflowFileName,
                    Sanitize(request.TargetGitHubRepo));

                activity?.SetTag("sync.status", "Failed");
                activity?.SetStatus(ActivityStatusCode.Error, msg);

                return NotFound(msg);
            }

            // ── Build dispatch payload ────────────────────────────────────────
            var payload = new
            {
                source_repo_url = request.SourceRepoUrl,
                ado_auth_header = request.AdoAuthorizationHeader,
                pull_request_id = request.PullRequestId,
                commit_id = request.CommitId,
                branch_name = request.BranchName ?? $"pr/{request.PullRequestId}",
                pr_title = request.PrTitle ?? $"Sync PR {request.PullRequestId} from Azure DevOps",
                pr_body = request.PrBody ?? string.Empty,
                target_branch = request.TargetBranch,
            };

            // ── Trigger repository_dispatch ───────────────────────────────────
            await gitHub.TriggerRepositoryDispatchAsync(
                owner, repo, accessToken, SyncEventType, payload, ct);

            logger.LogInformation(
                "Dispatched '{Event}' to {Repo} for PR {PrId}.",
                SyncEventType,
                Sanitize(request.TargetGitHubRepo),
                Sanitize(request.PullRequestId));

            activity?.SetTag("sync.status", "Dispatched");

            var traceId = Activity.Current?.TraceId.ToString();
            return Ok(new { message = "Sync dispatched successfully.", traceId });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error processing sync trigger for PR {PrId}.",
                Sanitize(request.PullRequestId));

            activity?.SetTag("sync.status", "Failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            return StatusCode(500, "An error occurred while processing the sync request.");
        }
    }

    /// <summary>Removes newline characters from a user-supplied value before it is written to a log.</summary>
    private static string Sanitize(string value)
    {
        return value.Replace("\r", string.Empty, StringComparison.Ordinal)
             .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks whether <paramref name="sourceRepoUrl"/> appears in <paramref name="allowList"/>.
    /// The allow list is a newline-, space-, comma-, or semicolon-separated string.
    /// Returns <c>null</c> when the source is allowed; a rejection reason when it is not.
    /// </summary>
    private static string? CheckSourceAllowed(string allowList, string sourceRepoUrl)
    {
        var allowed = allowList
            .Split(['\n', ' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allowed.Contains(sourceRepoUrl) ? null : "Source repository not in allowed list";
    }
}
