# Headless.Messaging.Redis.SqlServer.Demo

ASP.NET Core demo for Redis transport with SQL Server messaging storage.

## Shows

- Redis transport setup through `UseRedis(...)`.
- SQL Server messaging storage through `UseSqlServer(...)`.
- Queue consumer registration with `OnQueue<TConsumer>()`.
- Dashboard registration with `WithNoAuth()`.
- Swagger UI in Development.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Redis.SqlServer.Demo
```

Run reachable Redis and SQL Server instances first. The demo uses fixed local/container connection values in source.

## Production Note

The dashboard is unauthenticated, the Redis consume error handler rethrows, and connection values are demo placeholders.
