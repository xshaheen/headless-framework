# Framework.Sql.Abstraction

This package provides the core abstractions for SQL database interactions within the framework. It defines the interfaces necessary for implementing database-agnostic data access layers, enabling flexible dependency injection and testing.

## Interfaces

### `ISqlConnectionFactory`

Defines the contract for a factory that creates and manages SQL database connections.

-   `GetConnectionString()`: Returns the configured connection string.
-   `GetOpenConnectionAsync(CancellationToken)`: Returns an open `DbConnection`. Implementations may reuse an existing connection if appropriate.
-   `CreateNewConnectionAsync(CancellationToken)`: Creates and opens a new `DbConnection`.
-   Implements `IAsyncDisposable` for proper resource cleanup.

### `IConnectionStringChecker`

Defines a contract for checking the validity of a connection string and the existence of the target database.

-   `CheckAsync(string connectionString)`: Asynchronously checks if the database server is reachable and if the specific database exists. Returns a tuple `(bool Connected, bool DatabaseExists)`.
