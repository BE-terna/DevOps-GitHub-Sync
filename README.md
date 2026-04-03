# DevOps-GitHub-Sync

An ASP.NET Core 10 web application that bridges **Azure DevOps** pull requests to **GitHub**. When a pipeline in Azure DevOps raises or updates a pull request, it posts a request to this service. The service authenticates via a **GitHub App**, verifies the caller's identity using a per-installation API key, and dispatches a `repository_dispatch` event that triggers a GitHub Actions workflow to fetch the ADO branch and open (or update) a corresponding pull request on the target GitHub repository.

## How it works

```
Azure DevOps pipeline
        │
        │  POST /api/sync/trigger  (installationApiKey + PR metadata)
        ▼
DevOps-GitHub-Sync web app
        │
        │  Validates API key → looks up GitHub App installation
        │  Verifies installation covers target repo
        │  Obtains short-lived installation access token
        │  Checks Sync-DevOps-GitHub.yml exists in target repo
        │
        │  POST /repos/{owner}/{repo}/dispatches  (repository_dispatch)
        ▼
GitHub Actions – Sync-DevOps-GitHub.yml
        │
        │  Fetches PR branch from Azure DevOps (Basic auth with ADO token)
        │  Force-pushes branch to GitHub
        │  Creates / updates GitHub pull request
        ▼
GitHub pull request ✓
```

## Project structure

| Project | Description |
|---------|-------------|
| `Web/` | ASP.NET Core web app – controllers, services, views, options |
| `Data/` | EF Core DbContext, entities (`GitHubInstallation`, `SyncRequest`) |
| `AppHost/` | .NET Aspire orchestration for local development (Azure SQL container) |
| `ServiceDefaults/` | Shared Aspire configuration (OpenTelemetry, health checks, resilience) |
| `.github/workflows/` | CI/CD and sync GitHub Actions workflows |

## API endpoints

### `POST /api/github/webhook`

Receives GitHub App webhook events. Handles `installation` events to record (or remove) the GitHub App installation in the database and generate the `installationApiKey` that Azure DevOps pipelines will use.

> This endpoint must be configured as the **Webhook URL** in your GitHub App settings.

### `POST /api/sync/trigger`

Called by an Azure DevOps pipeline to request a sync. Authenticates the caller using the `installationApiKey`, verifies the installation has access to the target repository, then fires a `repository_dispatch` event on the target GitHub repo.

**Request body:**

```json
{
  "installationApiKey": "<non-guessable GUID from GitHubInstallations table>",
  "sourceRepoUrl":      "https://dev.azure.com/org/project/_git/repo",
  "systemAccessToken":  "$(System.AccessToken)",
  "pullRequestId":      "42",
  "commitId":           "abc123def456",
  "targetGitHubRepo":   "my-org/my-repo",
  "branchName":         "feature/my-feature",
  "prTitle":            "My feature (from ADO PR #42)",
  "prBody":             "Synced from Azure DevOps PR #42",
  "targetBranch":       "main"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `installationApiKey` | ✅ | API key from the `GitHubInstallations` table (see [Setup guide](docs/setup.md)) |
| `sourceRepoUrl` | ✅ | HTTPS clone URL of the Azure DevOps repository |
| `systemAccessToken` | ✅ | `$(System.AccessToken)` – ADO pipeline token used by the sync workflow to fetch the branch |
| `pullRequestId` | ✅ | Azure DevOps pull-request identifier |
| `commitId` | ✅ | Source commit SHA |
| `targetGitHubRepo` | ✅ | Target GitHub repository in `owner/repo` format |
| `branchName` | | Branch to create on GitHub (defaults to `pr/<pullRequestId>`) |
| `prTitle` | | GitHub PR title (defaults to `Sync PR <id> from Azure DevOps`) |
| `prBody` | | GitHub PR body / description |
| `targetBranch` | | Base branch for the GitHub PR (defaults to `main`) |

**Response codes:**

| Code | Meaning |
|------|---------|
| 200 | Sync dispatched successfully |
| 400 | Malformed request body |
| 401 | `installationApiKey` is missing or not found |
| 403 | The matched installation does not have access to the target repository |
| 404 | `Sync-DevOps-GitHub.yml` workflow not found in the target repository |
| 500 | Unexpected error (GitHub API failure, etc.) |

## Security model

| Concern | Mechanism |
|---------|-----------|
| Webhook authenticity | HMAC-SHA256 signature (`X-Hub-Signature-256`) verified with `WebhookSecret`; timing-safe comparison |
| Sync trigger auth | Per-installation `installationApiKey` (GUID) required in every request; prevents callers with only a leaked ADO token from pushing to arbitrary repos |
| GitHub API auth | Short-lived GitHub App installation access tokens (RSA-signed JWT exchanged for token, cached, auto-refreshed) |
| Deployment auth | OIDC workload identity federation – no stored Azure secrets in GitHub |

## CI/CD

The `build-and-deploy.yml` workflow:

- **On every push / PR to `main`:** Builds and publishes the Web project; uploads the artifact.
- **On manual dispatch (`workflow_dispatch`):** If an `environment` input is provided, deploys the artifact to the Azure Web App configured in that GitHub Environment using OIDC.

See [Setup guide – Production environment](docs/setup.md#production-environment) for the required GitHub Environment variables.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Azure SQL Server (local: via Aspire / Docker; production: Azure SQL)
- A registered [GitHub App](https://docs.github.com/en/apps/creating-github-apps/about-creating-github-apps/about-creating-github-apps)

## Further reading

- **[Setup guide](docs/setup.md)** – step-by-step local and production environment setup
