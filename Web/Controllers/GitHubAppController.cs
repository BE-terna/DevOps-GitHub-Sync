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
        };

        return View(vm);
    }
}
