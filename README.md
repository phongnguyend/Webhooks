# Webhook Hub

An ASP.NET Core and React application for managing tenant-specific webhook routes. Incoming payloads are published to configured topics on one shared Azure Service Bus namespace and shown in a live SignalR event inspector.

## Features

- Create, edit, delete, enable, and disable tenants.
- Create, edit, delete, enable, and disable topic routes within each tenant.
- Map every route to a destination topic on a shared Azure Service Bus.
- Automatically derive every Azure Service Bus topic name as `{tenantId}-{topicKey}`.
- Authenticate to Service Bus with either managed identity or a connection string.
- Persist configuration with EF Core and SQL Server.
- No user authentication is enabled yet.

## Local setup

Requirements: .NET 10, Node.js, and SQL Server LocalDB (or another local SQL Server instance).

The development database connection is in `backend/WebhookServer/appsettings.Development.json`. Override it with `ConnectionStrings__DefaultConnection` if LocalDB is not available.

Configure the shared Service Bus through user secrets or environment variables. Connection-string mode:

```powershell
$env:ServiceBus__UseManagedIdentity = "false"
$env:ServiceBus__ConnectionString = "Endpoint=sb://..."
```

Managed-identity mode (Azure CLI or Visual Studio credentials are also considered locally by `DefaultAzureCredential`):

```powershell
$env:ServiceBus__UseManagedIdentity = "true"
$env:ServiceBus__FullyQualifiedNamespace = "my-namespace.servicebus.windows.net"
```

The identity needs the **Azure Service Bus Data Sender** role on the namespace or destination topics. Topic entities must already exist; this app configures routes but does not provision Service Bus resources.

Start the API and frontend:

```powershell
dotnet run --project backend/WebhookServer
```

```powershell
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`. EF Core applies database migrations when the API starts.

## Production configuration

Use environment variables or your hosting platform's secure configuration rather than committing secrets:

```text
ConnectionStrings__DefaultConnection=<Azure SQL connection string>
ServiceBus__UseManagedIdentity=true
ServiceBus__FullyQualifiedNamespace=my-namespace.servicebus.windows.net
Cors__AllowedOrigins__0=https://webhooks.example.com
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
