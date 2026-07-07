# Headless.Messaging.RabbitMq.SqlServer.Demo

ASP.NET Core demo for RabbitMQ transport with SQL Server messaging storage.

## Shows

- EF Core messaging storage through `UseEntityFramework<AppDbContext>()`.
- RabbitMQ transport through `UseRabbitMq(...)`.
- Dashboard registration with `WithNoAuth()`.
- EF-backed transactional outbox behavior.
- Raw ADO coordination for the `/coordinated/adonet` path.
- Delayed publish and rollback examples in controllers.

## Run

```bash
dotnet run --project demo/Headless.Messaging.RabbitMq.SqlServer.Demo
```

Run local SQL Server and RabbitMQ first. The demo source includes a SQL Server container command and uses fixed local connection values.

## Production Note

The EF storage path enables transactional outbox wiring by default. The dashboard is unauthenticated, and local connection strings are demo placeholders.
