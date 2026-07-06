# Headless.Messaging.Dashboard.Auth.Demo

ASP.NET Core demo for Messaging Dashboard host authentication.

## Shows

- `UseDashboard(d => d.WithHostAuthentication(policy))`.
- OpenID Connect plus cookie authentication for dashboard access.
- A custom authentication scheme variant.
- Anonymous dashboard registration as a commented alternative.
- In-memory messaging transport and storage.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Dashboard.Auth.Demo
```

The active startup path combines OpenID Connect and the custom authentication scheme. Other variants are kept as methods in `Startup` and can be selected by changing the call in `ConfigureServices`.

## Production Note

This demo uses public demo identity-provider values and in-memory messaging. Replace auth settings, require HTTPS, and use durable providers for production.
