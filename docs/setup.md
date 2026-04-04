# Environment Setup Guide

This guide covers how to set up DevOps-GitHub-Sync for **local debugging** and for a **production deployment** on Azure.

---

## Table of contents

- [Prerequisites](#prerequisites)
- [GitHub App setup](#github-app-setup)
- [Local development environment](#local-development-environment)
- [Production environment](#production-environment)

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.x | Required for all scenarios |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Any recent | Required for local SQL container (Aspire) |
| [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) | Any recent | Required for production setup |
| [GitHub CLI (`gh`)](https://cli.github.com/) | Any recent | Optional – useful for testing |

---

## GitHub App setup

The application authenticates to GitHub using a **GitHub App**. You need to create one before running the service in any environment.

### 1 – Register a new GitHub App

1. Go to **GitHub → Settings → Developer settings → GitHub Apps → New GitHub App** (or your organisation's settings if the app should be owned by an org).
2. Fill in the fields:

   | Field | Value |
   |-------|-------|
   | **GitHub App name** | `DevOps-GitHub-Sync` (or any name) |
   | **Homepage URL** | Your service URL (or any URL) |
   | **Callback URL / Setup URL** | `https://<your-service-hostname>/github/installed` |
   | **Webhook URL** | `https://<your-service-hostname>/api/github/webhook` |
   | **Webhook secret** | A strong random string – note it down as `WebhookSecret` |
   | **Repository permissions → Contents** | Read & write |
   | **Repository permissions → Pull requests** | Read & write |
   | **Subscribe to events → Installation** | ✅ |
   | **Where can this GitHub App be installed?** | Any account (or only this account) |

   > **Setup URL vs Webhook URL:** The *Setup URL* (`/github/installed`) is where GitHub redirects
   > the browser after a user completes the installation wizard. The *Webhook URL* (`/api/github/webhook`)
   > is a separate background POST that GitHub sends at the same time. Both must be configured.

3. Click **Create GitHub App**.
4. Note down the **App ID** shown on the app's settings page.

### 2 – Generate a private key

On the GitHub App settings page scroll to **Private keys** and click **Generate a private key**. Save the downloaded `.pem` file – you will need it shortly.

Convert newlines to the `\n` literal so the value can be stored as a single-line string (required for environment variables / Key Vault):

```bash
# macOS / Linux
awk 'NF {sub(/\r/, ""); printf "%s\\n",$0;}' private-key.pem
```

### 3 – Install the app on the target repository (or organisation)

Navigate to **GitHub App settings → Install App** and install it on the account / repositories that should receive synced pull requests.

After you click **Install**, GitHub sends an `installation` webhook POST to `/api/github/webhook` which records the installation in the database, and redirects your browser to the **Setup URL** (`/github/installed`) where a confirmation is displayed.

> **Note:** There is an inherent race between the webhook POST and the browser redirect. If the
> page loads before the webhook has been processed it will show a "processing" spinner and
> auto-refresh every few seconds. This typically resolves within a second or two.

### 4 – Add the `DEVOPS_GITHUB_SYNC_SOURCES` variable to each target GitHub repository

For every GitHub repository that will receive synced pull requests, create a repository Actions variable:

1. In the repository go to **Settings → Secrets and variables → Actions → Variables**.
2. Click **New repository variable**.
3. Set **Name** to `DEVOPS_GITHUB_SYNC_SOURCES`.
4. Set **Value** to a newline-separated list of allowed Azure DevOps source repository URLs, for example:

```
https://dev.azure.com/myorg/project/_git/repo-a
https://dev.azure.com/myorg/project/_git/repo-b
```

The sync service reads this variable via the GitHub API and only triggers the workflow when `sourceRepoUrl` in the request matches one of the listed URLs.

---

## Local development environment

The project uses [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview) for local orchestration. Aspire starts a SQL Server container and wires the Web project to it automatically.

### 1 – Install the Aspire workload

```bash
dotnet workload install aspire
```

### 2 – Clone the repository

```bash
git clone https://github.com/BE-terna/DevOps-GitHub-Sync.git
cd DevOps-GitHub-Sync
```

### 3 – Store GitHub App secrets using .NET User Secrets

User secrets are scoped to the solution via the shared `UserSecretsId` in `Directory.Build.props`. Run these commands from the repository root:

```bash
# GitHub App ID (numeric)
dotnet user-secrets set "GitHubApp:AppId" "123456" --project Web

# GitHub App URL slug (the last segment of https://github.com/apps/<slug>)
dotnet user-secrets set "GitHubApp:AppSlug" "devops-github-sync" --project Web

# RSA private key (newlines as \n literal)
dotnet user-secrets set "GitHubApp:PrivateKeyPem" "-----BEGIN RSA PRIVATE KEY-----\nMIIE...\n-----END RSA PRIVATE KEY-----" --project Web

# Webhook secret (the value you set in the GitHub App settings)
dotnet user-secrets set "GitHubApp:WebhookSecret" "your-webhook-secret" --project Web
```

> **Tip:** User secrets are stored outside the repository directory and are never committed.

### 4 – Start the application via Aspire

```bash
dotnet run --project AppHost
```

Aspire will:
1. Pull and start a SQL Server container.
2. Create the `DevOpsGitHubSync` database.
3. Start the Web project and inject the connection string automatically.
4. Apply EF Core migrations on startup.
5. Open the Aspire Dashboard (URL printed in the console) where you can inspect logs, traces, and resource health.

The Web application will be available at **`https://localhost:7014`** (or `http://localhost:5013`).

### 5 – Expose the webhook endpoint (optional)

To receive real GitHub webhook events and test the post-installation redirect during local development, expose the local port using a tunnelling tool such as [ngrok](https://ngrok.com/) or [dev tunnels](https://learn.microsoft.com/en-us/azure/developer/dev-tunnels/overview):

```bash
ngrok http 5013
```

Update **both URL fields** in your GitHub App settings to the generated HTTPS URL:

| Field | Value |
|-------|-------|
| **Setup URL** | `https://xxxx.ngrok.io/github/installed` |
| **Webhook URL** | `https://xxxx.ngrok.io/api/github/webhook` |

### 6 – Run without Aspire (advanced)

If you prefer to run the Web project directly against an existing SQL Server instance, set the connection string manually:

```bash
dotnet user-secrets set "ConnectionStrings:DevOpsGitHubSync" \
  "Server=localhost,1433;Database=DevOpsGitHubSync;User Id=sa;Password=YourPassword;TrustServerCertificate=True" \
  --project Web

dotnet run --project Web
```

### Build and test

```bash
# Restore (first time or after package changes)
dotnet restore

# Build
dotnet build

# Publish (mimics CI – outputs to /tmp/publish)
dotnet publish Web/Web.csproj -c Release -o /tmp/publish
```

---

## Production environment

### Azure resources

Provision the following resources before deploying the application.

| Resource | Purpose |
|----------|---------|
| **Azure App Service** (Web App) | Hosts the ASP.NET Core application |
| **Azure SQL Server + Database** | Stores installations and audit records |
| **Azure Key Vault** (recommended) | Stores the GitHub App private key and webhook secret |
| **Application Insights** (optional) | OpenTelemetry-based monitoring |

### Application settings

Configure the following settings on the Azure Web App (**Configuration → Application settings**). Use Key Vault references for sensitive values.

| Setting key | Value | Sensitive |
|-------------|-------|-----------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | No |
| `GitHubApp:AppId` | Numeric GitHub App ID | No |
| `GitHubApp:AppSlug` | URL slug of the GitHub App (e.g. `devops-github-sync`) | No |
| `GitHubApp:PrivateKeyPem` | PEM key with `\n` literal newlines | **Yes** – use Key Vault reference |
| `GitHubApp:WebhookSecret` | Webhook secret string | **Yes** – use Key Vault reference |
| `ConnectionStrings:DevOpsGitHubSync` | Azure SQL connection string | **Yes** – use Key Vault reference |

**Example Key Vault reference** (in App Service Application Settings):

```
@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/GitHubAppPrivateKey/)
```

### Database

EF Core migrations are applied automatically on application startup. No manual migration step is required. Ensure the App Service's managed identity (or the connection string user) has `db_owner` or at minimum `db_ddladmin` + `db_datawriter` + `db_datareader` permissions on the target database.

### CI/CD deployment with GitHub Actions

Deployments are performed by the `build-and-deploy.yml` workflow using **OIDC workload identity federation** – no Azure client secrets are stored in GitHub.

#### 1 – Create a User-Assigned Managed Identity (or App Registration)

```bash
az identity create \
  --name devops-github-sync-deploy \
  --resource-group <your-rg>
```

Note down the **Client ID** and **Principal ID**.

#### 2 – Assign the deployment role

Grant the identity the **Website Contributor** role (or `Contributor` scoped to the App Service):

```bash
az role assignment create \
  --assignee <principal-id> \
  --role "Website Contributor" \
  --scope "/subscriptions/<sub-id>/resourceGroups/<rg>/providers/Microsoft.Web/sites/<app-name>"
```

#### 3 – Configure OIDC federated credential

```bash
az identity federated-credential create \
  --identity-name devops-github-sync-deploy \
  --resource-group <your-rg> \
  --name github-actions \
  --issuer "https://token.actions.githubusercontent.com" \
  --subject "repo:BE-terna/DevOps-GitHub-Sync:environment:<environment-name>" \
  --audiences "api://AzureADMultiTenantApp"
```

> Replace `<environment-name>` with the GitHub Environment name you will use (e.g. `production`).

#### 4 – Create a GitHub Environment with variables

In your GitHub repository go to **Settings → Environments → New environment** and create the environment (e.g. `production`). Add the following **variables** (not secrets – OIDC uses variables):

| Variable | Value |
|----------|-------|
| `AZURE_CLIENT_ID` | Client ID of the managed identity / app registration |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `AZURE_WEBAPP_NAME` | Name of the Azure Web App resource |

#### 5 – Trigger a deployment

Go to **GitHub → Actions → Build and Deploy → Run workflow**, enter the environment name (e.g. `production`), and click **Run workflow**.

The workflow will:
1. Build and publish the Web project.
2. Log in to Azure via OIDC using the environment variables.
3. Deploy the published artifact to the configured Azure Web App.

### Service URLs

After deployment, configure **both URL fields** in your GitHub App settings:

| Field | Value |
|-------|-------|
| **Setup URL** | `https://<your-web-app-hostname>/github/installed` |
| **Webhook URL** | `https://<your-web-app-hostname>/api/github/webhook` |

### Production checklist

- [ ] GitHub App created with correct permissions and events
- [ ] Setup URL set to `https://<hostname>/github/installed`
- [ ] Webhook URL set to `https://<hostname>/api/github/webhook`
- [ ] Private key and webhook secret stored in Key Vault
- [ ] App Service Application Settings configured (incl. Key Vault references)
- [ ] Azure SQL database provisioned; App Service identity has required DB permissions
- [ ] OIDC federated credential configured for the GitHub Environment
- [ ] GitHub Environment variables set (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_WEBAPP_NAME`)
- [ ] First deployment triggered and succeeded
- [ ] GitHub App installed on target repositories; Setup URL page (`/github/installed`) confirmed working
- [ ] `DEVOPS_GITHUB_SYNC_SOURCES` repository variable added to each target GitHub repository with the allowed ADO source URLs
