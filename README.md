# Webhook Router

An ASP.NET Core and React application for managing tenant-specific webhook routes. Each route can publish to any Azure Service Bus namespace and topic or queue, and successful messages are shown in a live SignalR event inspector.

## Features

- Create, edit, delete, enable, and disable tenants.
- Create, edit, delete, enable, and disable topic routes within each tenant.
- Map every route to any Azure Service Bus namespace and topic or queue.
- Select managed identity or connection-string authentication per topic route.
- Persist configuration with EF Core and SQL Server.
- Sign in from the React UI with Google Identity Services and use the Google ID token directly as the API bearer token.
- Isolate tenants, configuration, event history, and live events by application user.

## Local setup

Requirements: .NET 10, Node.js, and SQL Server LocalDB (or another local SQL Server instance).

The development database connection is in `backend/WebhookRouter/appsettings.Development.json`. Override it with `ConnectionStrings__DefaultConnection` if LocalDB is not available.

Create a Google OAuth web client and register this authorized redirect URI:

```text
http://localhost:5173/
```

Configure the same public Google client ID in the API and UI. The redirect URI defaults to the current UI page, or it can be set explicitly:

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>" --project backend/WebhookRouter
$env:VITE_GOOGLE_CLIENT_ID = "<client-id>"
$env:VITE_GOOGLE_REDIRECT_URI = "http://localhost:5173/"
```

The custom login button starts Google's full-page OpenID Connect redirect flow. Google returns an ID token in the URL fragment; the UI validates the redirect state and token nonce, removes the fragment from browser history, and sends the token in the `Authorization: Bearer` header. The API validates Google's signature, issuer, token lifetime, and audience through Google's OpenID Connect metadata. On first login, it creates a `Users` row whose username and email match the verified Google email. Application relationships use the database-generated `Users.Id`; the Google subject identifier is stored only in the Identity `UserLogins` table. When the Google token expires, the UI returns to the sign-in screen.

Configure the namespace, destination type, entity name, and authentication method for each topic route in the UI. For managed identity, Azure CLI or Visual Studio credentials are also considered locally by `DefaultAzureCredential`. The identity needs the **Azure Service Bus Data Sender** role on the destination namespace, topic, or queue. Destination entities must already exist; this app configures routes but does not provision Service Bus resources.

Connection strings are stored in SQL Server so the API can publish, but are never returned by its management endpoints. Protect database access and use narrowly scoped Service Bus credentials.

Start the API and frontend:

```powershell
dotnet run --project backend/WebhookRouter
```

```powershell
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`. EF Core applies database migrations when the API starts.

## User administration

The application currently supports **Global Admin** and **User** roles using ASP.NET Core Identity. New and existing accounts without a role receive User. Tenant Admin is reserved for a future change.

Global Admins can open **Users** to create accounts by email, edit first/last name and phone, assign User and/or Global Admin roles, and enable or disable accounts. Users cannot access these administration endpoints. Administrators cannot disable themselves, remove their own Global Admin role, or disable/demote the last enabled Global Admin. Tenant ownership rules still apply to both roles.

Preconfigured accounts have no password and receive no invitation email. Their first Google login with the matching verified email links to the existing database user ID and preserves configured profile details and roles. Email is set at creation and cannot be changed afterward, even before the first Google login. Unregistered Google users still receive a new User account, as before. Tenant Admin is not yet assignable.

Disabled accounts are rejected on subsequent authenticated API requests, even with an unexpired Google token, and receive no new live events. Disabling an account does not disable its public webhook routes; disable tenants/topics separately to stop routing.

To bootstrap an administrator, first sign in once to create the application account, then restart the API with a process-scoped environment variable:

```powershell
$env:BOOTSTRAP_GLOBAL_ADMIN_EMAIL = "<existing-google-login-email>"
dotnet run --project backend/WebhookRouter
# After stopping the setup process, remove the variable before normal startup:
Remove-Item Env:BOOTSTRAP_GLOBAL_ADMIN_EMAIL
```

The role assignment persists in SQL. Never commit the administrator email to configuration files. Refresh the UI after assigning the role. Migrations preserve existing users and enable them by default.

## Production configuration

See [infra/README.md](infra/README.md) for Azure infrastructure, dev/test parameters, deployment setup, and the frontend/backend CI workflows.

Use environment variables or your hosting platform's secure configuration rather than committing secrets:

```text
ConnectionStrings__DefaultConnection=<Azure SQL connection string>
Authentication__Google__ClientId=<google-client-id>
Cors__AllowedOrigins__0=https://webhooks.example.com
VITE_GOOGLE_CLIENT_ID=<google-client-id at frontend build time>
VITE_GOOGLE_REDIRECT_URI=https://webhooks.example.com/
```

The default production SQL example uses `Authentication=Active Directory Default`, so the app identity also needs access to the Azure SQL database.

## Webhook endpoint

After creating an enabled tenant and topic route in the UI, send messages to:

```text
POST /tenants/{tenant-id}/topics/{topic-key}
```

Example:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5229/tenants/00000000-0000-0000-0000-000000000000/topics/orders `
  -ContentType application/json `
  -Body '{"orderId":"ORD-1042","status":"paid"}'
```

The API returns `202 Accepted` only after Service Bus accepts the message. Missing routes return `404`, disabled routes return `403`, and Service Bus publishing failures return `502`.
