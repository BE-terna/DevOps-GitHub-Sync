using DevOps.GitHub.Sync.Web.Models;
using DevOps.GitHub.Sync.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DevOps.GitHub.Sync.Web.Controllers;

/// <summary>
/// Handles browser-facing GitHub App flows – specifically the post-installation
/// redirect that GitHub sends after a user installs (or updates) the app.
/// </summary>
public class GitHubAppController(GitHubAppService gitHub) : Controller
{

    /// <summary>
    /// Landing page after a GitHub App installation or update.
    ///
    /// GitHub redirects here as:
    ///   GET /github/installed?installation_id=&lt;id&gt;&amp;setup_action=install
    /// or
    ///   GET /github/installed?installation_id=&lt;id&gt;&amp;setup_action=update
    ///
    /// Fetches installation details directly from the GitHub API using the App JWT.
    /// When the installation does not have <c>Actions variables: read</c> permission
    /// the method additionally checks whether the organisation has defined the
    /// <c>GitSyncSource</c> custom property, and surfaces appropriate instructions.
    /// </summary>
    [HttpGet("github/installed")]
    public async Task<IActionResult> Installed(
        [FromQuery(Name = "installation_id")] long installationId,
        [FromQuery(Name = "setup_action")] string setupAction = "install",
        [FromQuery(Name = "code")] string? code = null,
        CancellationToken ct = default)
    {
        // Note: `code` is an OAuth user-access-token code that GitHub includes when
        // a redirect_uri is configured on the app. This app does not implement GitHub
        // OAuth user authentication so the code is intentionally ignored; it is
        // accepted here purely to prevent a query-string mismatch 400.
        var installation = await gitHub.GetInstallationAsync(installationId, ct);

        var vm = new InstallationViewModel
        {
            Found = installation is not null,
            InstallationId = installationId,
            SetupAction = setupAction,
            AccountLogin = installation?.AccountLogin,
            AccountType = installation?.AccountType,
            RepositorySelection = installation?.RepositorySelection,
            SuspendedAt = installation?.SuspendedAt,
            HasVariablesReadPermission = installation?.HasVariablesReadPermission ?? false,
        };

        if (installation is not null)
        {
            if (installation.HasVariablesReadPermission)
            {
                vm.ApprovalSetupStatus = SourceApprovalSetupStatus.HasVariablesPermission;
            }
            else if (string.Equals(installation.AccountType, "Organization", StringComparison.OrdinalIgnoreCase))
            {
                // Metadata (repo contents) read is always included; use the installation
                // access token to query the org's custom-property schema.
                var accessToken = await gitHub.GetInstallationAccessTokenAsync(installationId, ct);
                var schema = await gitHub.GetOrgPropertySchemaAsync(installation.AccountLogin, accessToken, ct);

                vm.ApprovalSetupStatus = schema switch
                {
                    null => SourceApprovalSetupStatus.SchemaCheckFailed,
                    var s when s.Contains("GitSyncSource") => SourceApprovalSetupStatus.OrgPropertyDefined,
                    _ => SourceApprovalSetupStatus.OrgPropertyNotDefined,
                };
            }
            else
            {
                // Personal (user-owned) accounts do not support custom properties.
                vm.ApprovalSetupStatus = SourceApprovalSetupStatus.UserAccountUnsupported;
            }
        }

        return View(vm);
    }
}
