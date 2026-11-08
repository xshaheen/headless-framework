# Headless.Messaging.Dashboard.Auth.Demo

ASP.NET Core demo for host authentication of the Headless dashboard's Messaging module.

## Shows

- `AddHeadlessDashboard(d => d.WithHostAuthentication(...).AddMessaging())` with `ViewPolicy` and `ChallengeScheme`, mounted with `MapHeadlessDashboard()`.
- OpenID Connect plus cookie authentication: the dashboard's Sign in link challenges OpenID Connect once and returns with the dashboard's own session cookie.
- Host mode over a custom authentication handler that reads an `X-Base-Token` header, exchanged once for a dashboard session.
- Anonymous dashboard registration as a commented alternative.
- In-memory messaging transport and storage.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Dashboard.Auth.Demo
```

Open `https://localhost:5001/headless/messaging` and choose **Sign in**. The active startup path accepts a user signed in through OpenID Connect or through the custom scheme. Other variants are methods in `Startup`; select one by changing the call in `ConfigureServices`.

To sign in through the custom scheme instead, exchange its header for a session from the browser console on the dashboard's origin, then reload the dashboard:

```js
await fetch('/headless/api/auth/session', {
  method: 'POST',
  headers: { 'X-Base-Token': 'xxx', 'X-Requested-With': 'XMLHttpRequest' },
});
```

## Production Note

This demo uses public demo identity-provider values and in-memory messaging. Replace auth settings, require HTTPS, and use durable providers for production.
