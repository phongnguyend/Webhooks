# Webhooks

A small ASP.NET Core webhook receiver with a live React event inspector.

## Run locally

Start the API:

```powershell
dotnet run --project backend/WebhookServer
```

In a second terminal, start the frontend:

```powershell
cd frontend
npm install
npm run dev
```

Open `http://localhost:5173`. The frontend loads previous requests and listens
for new events through SignalR at `/hubs/events`.

Send a sample webhook:

```powershell
Invoke-RestMethod -Method Post `
  -Uri http://localhost:5229/tenants/demo/topics/orders `
  -ContentType application/json `
  -Body '{"orderId":"ORD-1042","status":"paid"}'
```

Set `VITE_API_URL` when the API is hosted somewhere other than
`http://localhost:5229`.
