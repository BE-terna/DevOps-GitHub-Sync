using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOps.GitHub.Sync.Data;
using DevOps.GitHub.Sync.Data.Entities;
using DevOps.GitHub.Sync.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace DevOps.GitHub.Sync.Web.Services;

/// <summary>
/// Handles GitHub App authentication (JWT + installation access tokens) and
/// GitHub API operations required by the sync workflow.
/// </summary>
public sealed class GitHubAppService
{
    private readonly GitHubAppOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppDbContext _db;
    private readonly ILogger<GitHubAppService> _logger;

    public GitHubAppService(
        IOptions<GitHubAppOptions> options,
        IHttpClientFactory httpClientFactory,
        AppDbContext db,
        ILogger<GitHubAppService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // JWT generation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a short-lived JWT signed with the App's RSA private key.
    /// Valid for up to 10 minutes as required by GitHub.
    /// </summary>
    public string CreateAppJwt()
    {
        var pem = _options.PrivateKeyPem
            .Replace("\\n", "\n", StringComparison.Ordinal);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem.AsSpan());

        var key = new RsaSecurityKey(rsa.ExportParameters(true));
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var now = DateTimeOffset.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(
            issuer: _options.AppId.ToString(),
            issuedAt: now.AddSeconds(-60).UtcDateTime,   // 60 s clock skew buffer
            expires: now.AddMinutes(9).UtcDateTime,
            signingCredentials: creds);

        return handler.WriteToken(token);
    }

    // -------------------------------------------------------------------------
    // Installation access token
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a valid installation access token for the given installation.
    /// Fetches a fresh token when the stored one is missing or within 5 minutes of expiry.
    /// </summary>
    public async Task<string> GetInstallationAccessTokenAsync(
        GitHubInstallation installation,
        CancellationToken ct = default)
    {
        const int refreshBufferMinutes = 5;

        if (installation.AccessToken is not null
            && installation.AccessTokenExpiresAt.HasValue
            && installation.AccessTokenExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(refreshBufferMinutes))
        {
            return installation.AccessToken;
        }

        var jwt = CreateAppJwt();
        var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        var response = await client.PostAsync(
            $"https://api.github.com/app/installations/{installation.InstallationId}/access_tokens",
            null, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(
            cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty access token response from GitHub.");

        installation.AccessToken = result.Token;
        installation.AccessTokenExpiresAt = result.ExpiresAt;
        installation.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return result.Token;
    }

    // -------------------------------------------------------------------------
    // Repository permission / installation lookup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the installation record that has access to the given GitHub repository.
    /// Returns null if the app has not been installed on that owner / repository.
    /// </summary>
    public async Task<GitHubInstallation?> FindInstallationForRepoAsync(
        string owner,
        string repo,
        CancellationToken ct = default)
    {
        // First try an exact match on account login (covers "all repositories" installs)
        var byLogin = await _db.GitHubInstallations
            .FirstOrDefaultAsync(i => i.AccountLogin == owner, ct);

        if (byLogin is not null)
            return byLogin;

        // Fall back: verify each installation via the GitHub API
        var installations = await _db.GitHubInstallations.ToListAsync(ct);
        foreach (var installation in installations)
        {
            if (installation.RepositorySelection == "all")
                continue; // already checked via AccountLogin above

            var token = await GetInstallationAccessTokenAsync(installation, ct);
            var client = _httpClientFactory.CreateClient("GitHub");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var repoResponse = await client.GetAsync(
                $"https://api.github.com/repos/{owner}/{repo}", ct);

            if (repoResponse.IsSuccessStatusCode)
                return installation;
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Workflow check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when a workflow file with the given name exists in the repository.
    /// </summary>
    public async Task<bool> WorkflowExistsAsync(
        string owner,
        string repo,
        string workflowFileName,
        string accessToken,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/contents/.github/workflows/{workflowFileName}",
            ct);

        return response.IsSuccessStatusCode;
    }

    // -------------------------------------------------------------------------
    // Repository dispatch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends a <c>repository_dispatch</c> event to the target GitHub repository.
    /// </summary>
    public async Task TriggerRepositoryDispatchAsync(
        string owner,
        string repo,
        string accessToken,
        string eventType,
        object clientPayload,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var body = new { event_type = eventType, client_payload = clientPayload };

        var response = await client.PostAsJsonAsync(
            $"https://api.github.com/repos/{owner}/{repo}/dispatches",
            body, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"GitHub dispatch failed ({(int)response.StatusCode}): {errorBody}");
        }
    }

    // -------------------------------------------------------------------------
    // Private response DTOs
    // -------------------------------------------------------------------------

    private sealed class AccessTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
