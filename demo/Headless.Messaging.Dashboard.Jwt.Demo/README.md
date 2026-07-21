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

Submit user `bob` with password `bob` on the demo login page. The demo opens the dashboard with the token in a URL fragment, which the Host-auth login consumes and removes before navigating.

## Production Note

The JWT setup disables issuer and audience validation for the demo. Validate issuer, audience, HTTPS, signing keys, and token transport before using this pattern in production.
