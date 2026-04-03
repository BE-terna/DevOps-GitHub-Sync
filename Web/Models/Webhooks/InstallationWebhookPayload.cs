using System.Text.Json.Serialization;

namespace DevOps.GitHub.Sync.Web.Models.Webhooks;

/// <summary>Root object of a GitHub <c>installation</c> webhook event.</summary>
public sealed class InstallationWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("installation")]
    public InstallationPayload Installation { get; set; } = new();
}

public sealed class InstallationPayload
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("account")]
    public AccountPayload Account { get; set; } = new();

    [JsonPropertyName("repository_selection")]
    public string RepositorySelection { get; set; } = string.Empty;
}

public sealed class AccountPayload
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
