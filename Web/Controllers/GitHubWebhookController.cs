using DevOps.GitHub.Sync.Web.Models.Webhooks;
using DevOps.GitHub.Sync.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DevOps.GitHub.Sync.Web.Controllers;

/// <summary>
/// Receives GitHub App webhook events and logs installation lifecycle changes.
/// </summary>
[ApiController]
[Route("api/github/webhook")]
public class GitHubWebhookController(
    IOptions<GitHubAppOptions> options,
    ILogger<GitHubWebhookController> logger) : ControllerBase
{
    private readonly GitHubAppOptions _options = options.Value;

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // ── Read raw body ──────────────────────────────────────────────────────
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        // ── Verify HMAC-SHA256 signature ──────────────────────────────────────
        if (!VerifySignature(rawBody, Request.Headers["X-Hub-Signature-256"].ToString()))
        {
            logger.LogWarning("Webhook signature validation failed.");
            return Unauthorized("Invalid webhook signature.");
        }

        var eventType = Request.Headers["X-GitHub-Event"].ToString();
        logger.LogInformation("Received GitHub webhook event: {Event}", Sanitize(eventType));

        if (eventType != "installation")
        {
            return Ok();
        }

        var payload = JsonSerializer.Deserialize<InstallationWebhookPayload>(rawBody);
        if (payload is null)
        {
            return BadRequest("Could not parse webhook payload.");
        }

        logger.LogInformation(
            "Installation {Action}: id={InstallationId}, account={AccountLogin} ({AccountType}), selection={RepositorySelection}.",
            Sanitize(payload.Action),
            payload.Installation.Id,
            Sanitize(payload.Installation.Account.Login),
            Sanitize(payload.Installation.Account.Type),
            Sanitize(payload.Installation.RepositorySelection));

        return Ok();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool VerifySignature(string body, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret)
            || string.IsNullOrWhiteSpace(signatureHeader)
            || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var secret = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(bodyBytes);
        var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()));
    }

    /// <summary>Removes newline characters from a user-supplied value before it is written to a log.</summary>
    private static string Sanitize(string value)
    {
        return value.Replace("\r", string.Empty, StringComparison.Ordinal)
             .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}
