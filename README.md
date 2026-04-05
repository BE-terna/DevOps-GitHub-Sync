# DevOps-GitHub-Sync

An ASP.NET Core 10 web application that bridges **Azure DevOps** pull requests to **GitHub**. When a pipeline in Azure DevOps raises or updates a pull request, it posts a request to this service. The service authenticates via a **GitHub App** and dispatches a `repository_dispatch` event that triggers a GitHub Actions workflow to fetch the ADO branch and open (or update) a corresponding pull request on the target GitHub repository.

## How it works

```
Azure DevOps pipeline
        │
        │  POST /api/sync/trigger  (PR metadata + ADO Authorization header)
        ▼
DevOps-GitHub-Sync web app
        │
        │  Looks up GitHub App installation via GitHub API
        │  Verifies sourceRepoUrl is authorised (see Authorization below)
        │  Obtains short-lived installation access token (cached in-memory)
        │  Checks Sync-DevOps-GitHub.yml exists in target repo
        │
        │  POST /repos/{owner}/{repo}/dispatches  (repository_dispatch)
        ▼
GitHub Actions – Sync-DevOps-GitHub.yml
        │
        │  Fetches PR branch from Azure DevOps (using supplied Authorization header)
        │  Force-pushes branch to GitHub
        │  Creates / updates GitHub pull request
        ▼
GitHub pull request ✓
```

## Project structure

| Project | Description |
|---------|-------------|
| `Web/` | ASP.NET Core web app – controllers, services, views, options |
| `AppHost/` | .NET Aspire orchestration for local development |
| `ServiceDefaults/` | Shared Aspire configuration (OpenTelemetry, health checks, resilience) |
| `.github/workflows/` | CI/CD and sync GitHub Actions workflows |
| `docs/` | Setup guide and sample Azure DevOps pipeline |

## API endpoints

### `GET /github/installed`

Post-installation landing page. GitHub redirects the installer's browser here after they complete the GitHub App installation wizard, passing `?installation_id=<id>&setup_action=install` as query parameters.

The page fetches the installation details from the GitHub API and displays them along with a ready-to-use Azure DevOps pipeline snippet.

> This endpoint must be configured as the **Setup URL** in your GitHub App settings.

### `POST /api/github/webhook`

Receives GitHub App webhook events. Verifies the HMAC-SHA256 signature and logs installation lifecycle events (`created`, `deleted`, `suspended`, etc.).

> This endpoint must be configured as the **Webhook URL** in your GitHub App settings.

### `POST /api/sync/trigger`

Called by an Azure DevOps pipeline to request a sync. Authorizes the request by checking whether `sourceRepoUrl` is listed in a repository variable on the target GitHub repo, then fires a `repository_dispatch` event.

**Request body:**

```json
{
  "sourceRepoUrl":          "https://dev.azure.com/org/project/_git/repo",
  "adoAuthorizationHeader": "Bearer $(System.AccessToken)",
  "pullRequestId":          "42",
  "commitId":               "abc123def456",
  "targetGitHubRepo":       "my-org/my-repo",
  "branchName":             "feature/my-feature",
  "prTitle":                "My feature (from ADO PR #42)",
  "prBody":                 "Synced from Azure DevOps PR #42",
  "targetBranch":           "main"
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `sourceRepoUrl` | ✅ | HTTPS clone URL of the Azure DevOps repository |
| `adoAuthorizationHeader` | ✅ | Full HTTP Authorization header value for ADO authentication. Use `Bearer $(System.AccessToken)` for the built-in ADO pipeline token, or `Basic <base64(:PAT)>` for a Personal Access Token (base64-encode `:<PAT>` with colon prefix) |
| `pullRequestId` | ✅ | Azure DevOps pull-request identifier |
| `commitId` | ✅ | Source commit SHA |
| `targetGitHubRepo` | ✅ | Target GitHub repository in `owner/repo` format |
| `branchName` | | Branch to create on GitHub (defaults to `pr/<pullRequestId>`) |
| `prTitle` | | GitHub PR title (defaults to `Sync PR <id> from Azure DevOps`) |
| `prBody` | | GitHub PR body / description |
| `targetBranch` | | Base branch for the GitHub PR (defaults to `main`) |

**Authorization:** The target GitHub repository must be explicitly approved. Two methods are supported:

| Method | When | How |
|--------|------|-----|
| **Repository Actions variable** | App has `Actions variables: read` permission | Add a variable named `DEVOPS_GITHUB_SYNC_SOURCES` to the target repo (**Settings → Secrets and variables → Actions → Variables**) containing a newline-separated list of allowed ADO source URLs |
| **Custom repository property** | App does **not** have `Actions variables: read` permission (org accounts only) | Create a `GitSyncSource` custom property in the organisation schema (**Org Settings → Custom properties**), then set its value on each target repo with the allowed ADO source URLs |

The request is accepted only when `sourceRepoUrl` matches one of the URLs in the active method.

> The post-installation page (`/github/installed`) automatically detects which method applies and displays the correct setup instructions.

**Response codes:**

| Code | Meaning |
|------|---------|
| 200 | Sync dispatched successfully; body contains `traceId` |
| 400 | Malformed request body |
| 403 | No installation found for the target repo, variable missing, or source URL not in the allow-list |
| 404 | `Sync-DevOps-GitHub.yml` workflow not found in the target repository |
| 500 | Unexpected error (GitHub API failure, etc.) |

## Security model

| Concern | Mechanism |
|---------|-----------|
| Webhook authenticity | HMAC-SHA256 signature (`X-Hub-Signature-256`) verified with `WebhookSecret`; timing-safe comparison |
| Sync trigger auth – variables path | Source allow-list: the target repository must contain a `DEVOPS_GITHUB_SYNC_SOURCES` Actions variable listing permitted ADO source URLs; requests from unlisted sources are rejected with 403. Requires `Actions variables: read` permission. |
| Sync trigger auth – custom property path | Source allow-list via `GitSyncSource` custom repository property (org accounts only). Used automatically when the installation does not have `Actions variables: read` permission. Requires only the always-included `metadata: read` permission. |
| GitHub API auth | Short-lived GitHub App installation access tokens (RSA-signed JWT exchanged for token, cached in-memory, auto-refreshed) |
| Deployment auth | OIDC workload identity federation – no stored Azure secrets in GitHub |

## CI/CD

The `build-and-deploy.yml` workflow:

- **On every push / PR to `main`:** Builds and publishes the Web project; uploads the artifact.
- **On manual dispatch (`workflow_dispatch`):** If an `environment` input is provided, deploys the artifact to the Azure Web App configured in that GitHub Environment using OIDC.

See [Setup guide – Production environment](docs/setup.md#production-environment) for the required GitHub Environment variables.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A registered [GitHub App](https://docs.github.com/en/apps/creating-github-apps/about-creating-github-apps/about-creating-github-apps)

## Further reading

- **[Setup guide](docs/setup.md)** – step-by-step local and production environment setup
- **[Sample ADO pipeline](docs/ado-sync-pipeline.yml)** – ready-to-use Azure DevOps pipeline YAML
