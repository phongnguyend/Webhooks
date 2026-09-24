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
$requiredVariables = @('SQL_ADMINISTRATOR_OBJECT_ID', 'SQL_ADMINISTRATOR_DISPLAY_NAME', 'SQL_ADMINISTRATOR_PRINCIPAL_TYPE', 'GOOGLE_CLIENT_ID')
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

- Publish the backend with `dotnet publish backend/WebhookRouter/WebhookRouter.csproj -c Release` and deploy its publish directory to the `appServiceName` output. The template configures SQL, Google client ID, CORS, HTTPS, WebSockets, and `/healthz`.
- Build the frontend with the values in the `frontendBuildVariables` output and deploy `frontend/dist` to `staticWebAppName`. Vite embeds these values during the build; Static Web App runtime settings cannot change them. The CI build is a compilation check and is not an environment-configured deployment artifact.
- Register the output `frontendUrl` as a Google OAuth authorized JavaScript origin, and the exact `VITE_GOOGLE_REDIRECT_URI` (including trailing slash) as an authorized redirect URI. Use `additionalFrontendOrigins` for custom UI domains and update the frontend build variables accordingly.
- Both Azure environments run ASP.NET Core as `Production`, so test/dev resources do not load the LocalDB development connection string.
- The API identity receives **Azure Service Bus Data Sender** on the deployed namespace. Select Managed Identity and the `serviceBusNamespace` output when configuring routes. For other namespaces, grant the same role there separately. Local Service Bus authentication remains enabled to support connection-string routes; no keys are output or committed.
- `serviceBusTopics` and `serviceBusQueues` default to empty arrays. Add destination names before deployment or provision them separately. Topic consumers also need subscriptions, which this template does not create.

## CI

The two workflows under `.github/workflows` run independently on relevant pushes, pull requests, or manual dispatch. Frontend uses Node 24, `npm ci`, and the Vite build. Backend uses .NET 10, restores the renamed solution, and builds Release. Neither needs Azure credentials or a database, and neither deploys resources.

## Local validation

```powershell
az bicep build --file infra/main.bicep --stdout
```

Run the resource-group `what-if` above with real parameter values to verify subscription permissions, resource availability, and deployment changes before creating resources.
