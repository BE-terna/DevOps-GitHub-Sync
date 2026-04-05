using DevOps.GitHub.Sync.Web.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace DevOps.GitHub.Sync.Web.Services;

/// <summary>
/// Handles GitHub App authentication (JWT + installation access tokens) and
/// GitHub API operations required by the sync workflow.
/// Installation access tokens are cached in-memory and refreshed automatically.
/// </summary>
public sealed class GitHubAppService(
    IOptions<GitHubAppOptions> options,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<GitHubAppService> logger)
{
    private readonly GitHubAppOptions _options = options.Value;
    private const string TokenCacheKeyPrefix = "GH_Token_";
    private const int RefreshBufferMinutes = 5;

    // ── JWT generation ────────────────────────────────────────────────────────

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

    // ── Installation lookup via GitHub API ────────────────────────────────────

    /// <summary>
    /// Returns the GitHub App installation that covers the given repository,
    /// by calling <c>GET /repos/{owner}/{repo}/installation</c>.
    /// Returns <c>null</c> when no installation covers the repository.
    /// </summary>
    public async Task<AppInstallation?> GetInstallationForRepoAsync(
        string owner,
        string repo,
        CancellationToken ct = default)
    {
        var jwt = CreateAppJwt();
        var client = httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        var response = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/installation", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<InstallationApiResponse>(
            cancellationToken: ct);

        return result is null ? null
            : new AppInstallation(result.Id, result.Account.Login, result.Account.Type, result.RepositorySelection, result.SuspendedAt);
    }

    /// <summary>
    /// Returns the GitHub App installation by its identifier,
    /// by calling <c>GET /app/installations/{installationId}</c>.
    /// Returns <c>null</c> when the installation does not exist.
    /// </summary>
    public async Task<AppInstallation?> GetInstallationAsync(
        long installationId,
        CancellationToken ct = default)
    {
        var jwt = CreateAppJwt();
        var client = httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        var response = await client.GetAsync(
            $"https://api.github.com/app/installations/{installationId}", ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<InstallationApiResponse>(
            cancellationToken: ct);

        return result is null ? null
            : new AppInstallation(result.Id, result.Account.Login, result.Account.Type, result.RepositorySelection, result.SuspendedAt);
    }

    // ── Installation access token ─────────────────────────────────────────────

    /// <summary>
    /// Returns a valid installation access token for the given installation ID.
    /// Tokens are cached in-memory and refreshed when within 5 minutes of expiry.
    /// </summary>
    public async Task<string> GetInstallationAccessTokenAsync(
        long installationId,
        CancellationToken ct = default)
    {
        var cacheKey = TokenCacheKeyPrefix + installationId;

        if (cache.TryGetValue<string>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var jwt = CreateAppJwt();
        var client = httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);

        var response = await client.PostAsync(
            $"https://api.github.com/app/installations/{installationId}/access_tokens",
            null, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(
            cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty access token response from GitHub.");

        cache.Set(cacheKey, result.Token, result.ExpiresAt.AddMinutes(-RefreshBufferMinutes));

        return result.Token;
    }

    // ── Workflow check ────────────────────────────────────────────────────────

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
        var client = httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/contents/.github/workflows/{workflowFileName}",
            ct);

        return response.IsSuccessStatusCode;
    }

    // ── Repository Actions variables ──────────────────────────────────────────

    /// <summary>
    /// Reads a GitHub Actions repository variable by name.
    /// Returns the variable's value, or <c>null</c> if the variable does not exist.
    /// </summary>
    public async Task<string?> GetRepoVariableAsync(
        string owner,
        string repo,
        string variableName,
        string accessToken,
        CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync(
            $"https://api.github.com/repos/{owner}/{repo}/actions/variables/{variableName}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RepoVariableResponse>(
            cancellationToken: ct);

        return result?.Value;
    }

    // ── Repository dispatch ───────────────────────────────────────────────────

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
        var client = httpClientFactory.CreateClient("GitHub");
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

    // ── Public DTOs ───────────────────────────────────────────────────────────

    /// <summary>Minimal view of a GitHub App installation returned by the GitHub API.</summary>
    public sealed record AppInstallation(
        long Id,
        string AccountLogin,
        string AccountType,
        string RepositorySelection,
        DateTimeOffset? SuspendedAt = null
        );

    // ── Private response DTOs ─────────────────────────────────────────────────

    private sealed class InstallationApiResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("account")]
        public InstallationAccount Account { get; set; } = new();

        [JsonPropertyName("repository_selection")]
        public string RepositorySelection { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("suspended_at")]
        public DateTimeOffset? SuspendedAt { get; set; }
    }

    private sealed class InstallationAccount
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    private sealed class AccessTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class RepoVariableResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }
}
