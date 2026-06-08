# Headless Ambient Transactions EntityFramework

Entity Framework Core Relational adapter for ambient transactions.

This package wraps `IDbContextTransaction` as an `IAmbientTransaction` and provides `DatabaseFacade.BeginAmbientTransaction*` helpers. It depends only on EF Core Relational and does not reference `Headless.Orm.EntityFramework`.
