# Headless.Messaging.Kafka.PostgreSql.Demo

ASP.NET Core demo for Kafka transport with PostgreSQL messaging storage.

## Shows

- Queue consumer registration with `OnQueue<TConsumer>()`.
- Raw PostgreSQL storage through `UsePostgreSql(connectionString)`.
- Kafka transport through `UseKafka(...)`.
- Dashboard registration with `WithNoAuth()`.
- Explicit PostgreSQL and EF commit-coordination registration for raw storage examples.
- Controller endpoints for coordinated publishing patterns.

## Run

```bash
dotnet run --project demo/Headless.Messaging.Kafka.PostgreSql.Demo
```

Run local PostgreSQL and Kafka first. The connection string and Kafka broker address are hard-coded in the demo source.

## Production Note

The raw PostgreSQL storage path needs explicit commit coordination. The dashboard is unauthenticated, and local container values are demo placeholders.
