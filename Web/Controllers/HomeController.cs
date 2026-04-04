using System.Diagnostics;
using DevOps.GitHub.Sync.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Web.Models;

namespace Web.Controllers;

public class HomeController : Controller
{
    private readonly GitHubAppOptions _gitHubOptions;

    public HomeController(IOptions<GitHubAppOptions> gitHubOptions)
    {
        _gitHubOptions = gitHubOptions.Value;
    }

    public IActionResult Index()
    {
        // Build the GitHub App installation URL with a redirect_uri so that after the
        // user installs the app GitHub sends them back to /github/installed where the
        // generated API key is displayed.
        var slug = _gitHubOptions.AppSlug;
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var callbackUrl = Url.Action("Installed", "GitHubApp", null, Request.Scheme)!;
            ViewData["GitHubInstallUrl"] =
                $"https://github.com/apps/{slug}/installations/new" +
                $"?redirect_uri={Uri.EscapeDataString(callbackUrl)}";
        }
        else
        {
            // AppSlug not configured – fall back to the GitHub Apps directory.
            ViewData["GitHubInstallUrl"] = "https://github.com/apps";
        }

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
