using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevOps.GitHub.Sync.Data;
using DevOps.GitHub.Sync.Data.Entities;
using DevOps.GitHub.Sync.Web.Models.Webhooks;
using DevOps.GitHub.Sync.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevOps.GitHub.Sync.Web.Controllers;

/// <summary>
/// Receives GitHub App webhook events.
/// Handles <c>installation</c> events to persist (or remove) installation records.
/// </summary>
[ApiController]
[Route("api/github/webhook")]
public class GitHubWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly GitHubAppOptions _options;
    private readonly ILogger<GitHubWebhookController> _logger;

    public GitHubWebhookController(
        AppDbContext db,
        IOptions<GitHubAppOptions> options,
        ILogger<GitHubWebhookController> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

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
            _logger.LogWarning("Webhook signature validation failed.");
            return Unauthorized("Invalid webhook signature.");
        }

        var eventType = Request.Headers["X-GitHub-Event"].ToString();
        _logger.LogInformation("Received GitHub webhook event: {Event}", eventType);

        if (eventType != "installation")
            return Ok();

        var payload = JsonSerializer.Deserialize<InstallationWebhookPayload>(rawBody);
        if (payload is null)
            return BadRequest("Could not parse webhook payload.");

        switch (payload.Action)
        {
            case "created":
            case "new_permissions_accepted":
                await UpsertInstallationAsync(payload, rawBody, ct);
                break;

            case "deleted":
            case "suspend":
                await RemoveInstallationAsync(payload.Installation.Id, ct);
                break;

            default:
                _logger.LogInformation("Unhandled installation action: {Action}", payload.Action);
                break;
        }

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

    private async Task UpsertInstallationAsync(
        InstallationWebhookPayload payload,
        string rawBody,
        CancellationToken ct)
    {
        var existing = await _db.GitHubInstallations
            .FirstOrDefaultAsync(i => i.InstallationId == payload.Installation.Id, ct);

        if (existing is null)
        {
            existing = new GitHubInstallation
            {
                InstallationId = payload.Installation.Id,
            };
            _db.GitHubInstallations.Add(existing);
        }

        existing.AccountLogin = payload.Installation.Account.Login;
        existing.AccountType = payload.Installation.Account.Type;
        existing.RepositorySelection = payload.Installation.RepositorySelection;
        existing.RawPayload = rawBody;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Installation {Id} ({Login}) upserted.",
            payload.Installation.Id,
            payload.Installation.Account.Login);
    }

    private async Task RemoveInstallationAsync(long installationId, CancellationToken ct)
    {
        var existing = await _db.GitHubInstallations
            .FirstOrDefaultAsync(i => i.InstallationId == installationId, ct);

        if (existing is not null)
        {
            _db.GitHubInstallations.Remove(existing);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Installation {Id} removed.", installationId);
        }
    }
}
