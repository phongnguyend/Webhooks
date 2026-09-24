# Azure infrastructure

`main.bicep` deploys into an existing resource group. Each deployment creates a Linux App Service plan and .NET 10 API, a Static Web App, an Azure SQL server/database, and a Standard Service Bus namespace. Optional topic and queue names can be supplied in the parameter files.

Defaults: B1 App Service, Free Static Web Apps, Basic SQL, Standard Service Bus. These resources incur Azure charges. Regions and SKUs are configurable; Static Web Apps uses a separate supported region. Resource names include environment and a deterministic suffix.

## Configure and deploy

### Naming and tags

Set `namePrefix` (default `webhookrouter`). Resources use `<prefix>-<environment>-<stable-suffix>-<purpose>`, for example `webhookrouter-dev-<suffix>-api`, `-web`, `-plan`, `-sql`, and `-bus`. The database defaults to `<prefix>-<environment>-db`. Firewall rules and nested deployment names also include the project and environment.

Every taggable resource receives `project` (the prefix), `application` (`WebhookRouter`), `environment`, `managedBy` (`Bicep`), and a resource-specific `component`. Set optional `additionalTags` in either parameter file, for example `{"owner": "platform-team", "costCenter": "engineering"}`. Identification tags cannot be overridden by those extra tags. Filter Azure Portal resources by `project=webhookrouter` and `environment=dev` to find this deployment.

Child resources that do not support tags (Service Bus topics/queues, firewall rules, app settings, and role assignments) are identified through their parent. Service Bus entity names remain exactly as supplied so route configuration stays predictable.

Keep the prefix stable after deploying: changing resource names creates new resources. If upgrading an existing deployment whose database is named `WebhookRouter`, explicitly set `sqlDatabaseName` to `WebhookRouter` to retain that database; the new prefixed default is for new deployments.

Keep identity and Google client values as placeholders in the checked-in parameter files. Supply actual values through environment variables at deployment time; do not save them in JSON or documentation. The deploying identity needs resource creation and role assignment permissions (for example Contributor plus Role Based Access Control Administrator).

```powershell
az login
az account set --subscription <subscription-id>
az group create --name rg-webhookrouter-dev --location southeastasia

# Populate these environment variables locally or through your CI environment.
# SQL_ADMINISTRATOR_PRINCIPAL_TYPE must be User or Group.
$requiredVariables = @('SQL_ADMINISTRATOR_OBJECT_ID', 'SQL_ADMINISTRATOR_DISPLAY_NAME', 'SQL_ADMINISTRATOR_PRINCIPAL_TYPE', 'GOOGLE_CLIENT_ID', 'JWT_SIGNING_KEY')
foreach ($variable in $requiredVariables) {
  if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
    throw "Missing environment variable: $variable"
  }
}
$parameterOverrides = @(
  "sqlAdministratorObjectId=$env:SQL_ADMINISTRATOR_OBJECT_ID"
  "sqlAdministratorDisplayName=$env:SQL_ADMINISTRATOR_DISPLAY_NAME"
  "sqlAdministratorPrincipalType=$env:SQL_ADMINISTRATOR_PRINCIPAL_TYPE"
  "googleClientId=$env:GOOGLE_CLIENT_ID"
  "jwtSigningKey=$env:JWT_SIGNING_KEY"
)
az deployment group what-if --resource-group rg-webhookrouter-dev --template-file infra/main.bicep --parameters '@infra/parameters.dev.json' @parameterOverrides
az deployment group create --name webhookrouter-dev --resource-group rg-webhookrouter-dev --template-file infra/main.bicep --parameters '@infra/parameters.dev.json' @parameterOverrides
```

For test, use `rg-webhookrouter-test`, `webhookrouter-test`, and `@infra/parameters.test.json`. These commands provision infrastructure only; they do not publish application code.

## One-time SQL access setup

The API uses its system-assigned managed identity with Entra-only SQL authentication. Azure role assignments do not create SQL database users. Before starting the deployed API, connect to the output `sqlServerHostName` and **the application database** as the configured administrator user or a member of the configured administrator group. Temporarily permit your workstation's public IP in the SQL firewall to run this setup, then remove that rule.

Use the deployment's `appServiceName` output below:

```sql
CREATE USER [<appServiceName>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<appServiceName>];
ALTER ROLE db_datawriter ADD MEMBER [<appServiceName>];
ALTER ROLE db_ddladmin ADD MEMBER [<appServiceName>];
```

The DDL role is needed because the current API applies EF migrations on startup. Setup must succeed before its first startup; otherwise it cannot migrate or serve requests. For a separate migration deployment process, remove runtime DDL privileges after moving migration execution out of startup. The SQL firewall permits the App Service's possible outbound IPs; rerun infrastructure deployment if the hosting plan's outbound addresses change. This template uses public endpoints with restricted SQL firewall rules, not private networking.

## Application deployment configuration

The API now issues its own JWTs. Before releasing this version to existing resources, set `Authentication__Jwt__SigningKey` in App Service environment settings (or use a Key Vault reference), or redeploy Bicep with `JWT_SIGNING_KEY` supplied as above. Generate at least 32 cryptographically random bytes and base64-encode them; keep a separate stable key per environment. Never commit the value, put it in frontend `VITE_` variables, or print it in CI logs. Bicep accepts it as a secure parameter and configures environment-specific issuer/audience values. App startup fails if the signing key is missing or invalid. The application release workflow does not configure this secret; it must exist before deployment. See the root README for a key-generation example. No Azure settings are changed automatically by this code update.

- Publish the backend with `dotnet publish backend/WebhookRouter/WebhookRouter.csproj -c Release` and deploy its publish directory to the `appServiceName` output. The template configures SQL, Google client ID, CORS, HTTPS, WebSockets, and `/healthz`.
- Build the frontend with the values in the `frontendBuildVariables` output and deploy `frontend/dist` to `staticWebAppName`. Vite embeds these values during the build; Static Web App runtime settings cannot change them. The CI build is a compilation check and is not an environment-configured deployment artifact.
- Register the output `frontendUrl` as a Google OAuth authorized JavaScript origin, and the exact `VITE_GOOGLE_REDIRECT_URI` (including trailing slash) as an authorized redirect URI. Use `additionalFrontendOrigins` for custom UI domains and update the frontend build variables accordingly.
- Both Azure environments run ASP.NET Core as `Production`, so test/dev resources do not load the LocalDB development connection string.
- The API identity receives **Azure Service Bus Data Sender** on the deployed namespace. Select Managed Identity and the `serviceBusNamespace` output when configuring routes. For other namespaces, grant the same role there separately. Local Service Bus authentication remains enabled to support connection-string routes; no keys are output or committed.
- `serviceBusTopics` and `serviceBusQueues` default to empty arrays. Add destination names before deployment or provision them separately. Topic consumers also need subscriptions, which this template does not create.

## CI

The two workflows under `.github/workflows` run independently on relevant pushes, pull requests, or manual dispatch. Frontend uses Node 24, `npm ci`, and the Vite build. Backend uses .NET 10, restores the renamed solution, and builds Release. Neither needs Azure credentials or a database, and neither deploys resources.

## Manual application release

`.github/workflows/release.yml` builds and releases both applications. It runs **only** via **Actions → Release to Azure → Run workflow**, with a branch and `dev` or `test` selection. Merge the workflow into the default branch first so GitHub displays the Run workflow button. It does not provision infrastructure or change parameter files.

Create GitHub environments named `dev` and `test` under repository **Settings → Environments**. Configure these separately for each environment:

| Type | Name | Value |
| --- | --- | --- |
| Variable | `AZURE_WEBAPP_NAME` | Bicep `appServiceName` output |
| Variable | `API_URL` | Bicep `apiUrl` output, no trailing slash |
| Variable | `FRONTEND_URL` | Bicep `frontendUrl` output or configured custom UI origin, no trailing slash |
| Secret | `AZURE_CLIENT_ID` | Client ID of the release service principal or user-assigned managed identity |
| Secret | `AZURE_TENANT_ID` | Microsoft Entra tenant ID |
| Secret | `AZURE_SUBSCRIPTION_ID` | Target subscription ID |
| Secret | `GOOGLE_CLIENT_ID` | Same Google OAuth client ID configured on the API |
| Secret | `AZURE_STATIC_WEB_APPS_API_TOKEN` | Deployment token from the target Static Web App's **Manage deployment token** page |

Google's client ID is public in the built frontend, even when supplied as a GitHub secret. Never provide a Google client secret to Vite. The redirect URI is `FRONTEND_URL` plus `/`; register that exact URI with Google and configure the API's CORS origin accordingly.

Configure [Azure OIDC federation](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure) for the release identity, trusting this repository's selected GitHub environment (`dev` or `test`), issuer `https://token.actions.githubusercontent.com`, and audience `api://AzureADTokenExchange`. Use the actual subject format for your repository's OIDC configuration; newer repositories can use immutable repository/owner IDs. Create one federated credential per environment. Grant the identity Website Contributor scoped to the target App Service; it does not need SQL access or subscription-wide Contributor. No Azure client secret or App Service publish profile is required.

Restrict allowed deployment branches in each GitHub environment and add required reviewers where available. Every manually selected branch deploys to the selected Azure resource's primary site, not a Static Web Apps preview. Do not allow untrusted branches to access deployment secrets.

Before the first release, complete **One-time SQL access setup** above. The backend applies additive EF migrations at startup using its App Service managed identity; this workflow does not drop the database or run migrations from the GitHub runner. Ensure backups exist before releases involving schema changes.

Both apps are built before any deployment. The API deploys first, then `/healthz` is checked with retries before the prebuilt UI is uploaded. Releases for the same environment are serialized. A release is not atomic: if the frontend upload fails, the backend may already be updated; inspect the failed run and rerun after fixing the issue. There is no automatic database rollback. Existing CI workflows remain build-only.

## Local validation

```powershell
az bicep build --file infra/main.bicep --stdout
```

Run the resource-group `what-if` above with real parameter values to verify subscription permissions, resource availability, and deployment changes before creating resources.
