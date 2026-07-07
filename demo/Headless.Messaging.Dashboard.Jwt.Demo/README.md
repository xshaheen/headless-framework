# Headless.Messaging.Dashboard.Jwt.Demo

ASP.NET Core demo for protecting the Messaging Dashboard with JWT bearer authentication.

## Shows

- JWT bearer authentication and an authorization policy.
- `UseDashboard(d => d.WithHostAuthentication(policy))`.
- In-memory messaging transport and storage.
- A hosted service that publishes demo messages.
- `/security/createToken` endpoint for demo token creation.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Dashboard.Jwt.Demo
```

Create a token by posting user `bob` with password `bob` to `/security/createToken`, then use it when opening dashboard endpoints.

## Production Note

The JWT setup disables issuer and audience validation and allows query-string tokens for the demo. Validate issuer, audience, HTTPS, signing keys, and token transport before using this pattern in production.
