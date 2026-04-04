using DevOps.GitHub.Sync.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Controllers;

/// <summary>
/// Handles browser-facing GitHub App flows – specifically the post-installation
/// redirect that GitHub sends after a user installs (or updates) the app.
/// </summary>
public class GitHubAppController : Controller
{
    private readonly AppDbContext _db;

    public GitHubAppController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Landing page after a GitHub App installation or update.
    ///
    /// GitHub redirects here as:
    ///   GET /github/installed?installation_id=&lt;id&gt;&amp;setup_action=install
    /// or
    ///   GET /github/installed?installation_id=&lt;id&gt;&amp;setup_action=update
    ///
    /// If the webhook has already been processed the page shows the installation
    /// API key that the operator must store as a secret in Azure DevOps.
    /// If the webhook has not yet arrived (rare race condition) the page renders a
    /// "still processing" message that auto-refreshes every few seconds.
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
        var installation = await _db.GitHubInstallations
            .FirstOrDefaultAsync(i => i.InstallationId == installationId, ct);

        var vm = new InstallationViewModel
        {
            Found = installation is not null,
            InstallationId = installationId,
            SetupAction = setupAction,
            AccountLogin = installation?.AccountLogin,
            AccountType = installation?.AccountType,
            RepositorySelection = installation?.RepositorySelection,
            ApiKey = installation?.ApiKey,
        };

        return View(vm);
    }
}
