# Headless.Messaging.Dashboard.Jwt.Demo

ASP.NET Core demo for protecting the Messaging Dashboard with JWT bearer authentication.

## Shows

- JWT bearer authentication and an authorization policy.
- `UseDashboard(d => d.WithHostAuthentication(host => host.ViewPolicy = ...))`, mounted with `app.MapHeadlessMessagingDashboard()`.
- Exchanging the app's bearer token for the dashboard's session cookie with `POST {basePath}/api/auth/session`.
- In-memory messaging transport and storage.
- A hosted service that publishes demo messages.
- `/security/createToken` endpoint for demo token creation.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Dashboard.Jwt.Demo
```

Submit user `bob` with password `bob` on the demo login page. The page creates a JWT, posts it to `/messaging/dashboard/api/auth/session` as a bearer token with the `X-Requested-With` header, and opens the dashboard once the response is `204`. The token never goes into a URL or browser storage; the dashboard runs on its own HttpOnly session cookie, which ends at its idle timeout (30 minutes by default) or its absolute lifetime (8 hours), even though the JWT expires after 60 minutes.

## Production Note

The JWT setup disables issuer and audience validation for the demo. Validate issuer, audience, HTTPS, signing keys, and token transport before using this pattern in production.
