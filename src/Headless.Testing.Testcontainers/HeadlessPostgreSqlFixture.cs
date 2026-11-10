// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// PostgreSQL test fixture on <see cref="TestImages.PostgreSql"/>, or the <see cref="Image"/> a subclass names, that gives
/// each fixture a database of its own.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConnectionString"/> points at <see cref="DatabaseName"/>, a database the fixture creates on start and
/// drops on dispose. Derive any further database from it with <see cref="DerivedDatabaseName"/> and create it with
/// <see cref="CreateDatabaseAsync"/>, so the fixture also drops it. Use <see cref="AdminConnectionString"/>
/// (the <c>postgres</c> database) only for server-level statements.
/// </para>
/// <para>
/// When <see cref="SharedTestContainers.IsEnabled"/> and <see cref="ShareContainer"/> are both true, every fixture
/// attaches to one long-lived PostgreSQL container instead of starting its own. The fixture holds a session advisory
/// lock while it lives, and a fixture that starts later drops the databases of any owner whose lock is free, so a
/// killed run leaves nothing behind for the next one.
/// </para>
/// <para>
/// Override <see cref="ConfigureContainer"/> only together with <see cref="ShareContainer"/> returning
/// <see langword="false"/>: a shared server keeps one configuration for every run.
/// </para>
/// </remarks>
[PublicAPI]
public class HeadlessPostgreSqlFixture : IAsyncLifetime
{
    // Several modules and their hosts share one server, each fixture with its own connection pools; the default of
    // 100 fails a start with 53300 long before memory runs short.
    private const string _MaxConnections = "max_connections=1000";

    private PostgreSqlContainer? _container;
    private PostgreSqlDatabaseOwnership? _ownership;
    private bool _shared;

    /// <summary>
    /// Gets whether this fixture may attach to the shared PostgreSQL container when sharing is on. Override it to
    /// return <see langword="false"/> for a fixture that stops, pauses, or reconfigures its server, or whose tests
    /// read server-wide state another run would disturb.
    /// </summary>
    protected virtual bool ShareContainer => true;

    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.PostgreSql"/> unless overridden. Override it for a
    /// server that needs extensions the default image lacks, such as PostGIS. The image is part of the shared
    /// container's name, so fixtures on different images share separate servers and never attach to each other's.
    /// </summary>
    /// <remarks>
    /// The image must keep the official <c>postgres</c> entrypoint: the fixture passes <c>-c max_connections</c> and
    /// sets the superuser through <c>POSTGRES_USER</c> and <c>POSTGRES_PASSWORD</c>.
    /// </remarks>
    protected virtual string Image => TestImages.PostgreSql;

    /// <summary>Gets the started container, for its host, mapped port, or command execution.</summary>
    public PostgreSqlContainer Container =>
        _container ?? throw new InvalidOperationException("The PostgreSQL fixture has not been initialized.");

    /// <summary>Gets the name of the database this fixture owns.</summary>
    public string DatabaseName => Ownership.OwnerId;

    /// <summary>Gets the connection string of the database this fixture owns.</summary>
    public string ConnectionString => ConnectionStringFor(DatabaseName);

    /// <summary>Gets a superuser connection string to the <c>postgres</c> database, for server-level statements only.</summary>
    public string AdminConnectionString => ConnectionStringFor("postgres");

    private PostgreSqlDatabaseOwnership Ownership =>
        _ownership ?? throw new InvalidOperationException("The PostgreSQL fixture has not been initialized.");

    /// <summary>Returns the connection string of <paramref name="database"/> on this fixture's server.</summary>
    public string ConnectionStringFor(string database)
    {
#pragma warning disable RS0030 // The one sanctioned call: it supplies host, port, and credentials, and the database is replaced.
        return new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { Database = database }.ToString();
#pragma warning restore RS0030
    }

    /// <summary>
    /// Returns the name of a further database this fixture owns, such as a second database a test needs beside
    /// <see cref="DatabaseName"/>. The fixture drops it on dispose once <see cref="CreateDatabaseAsync"/> created it.
    /// </summary>
    /// <param name="suffix">Lowercase letters, digits, and underscores that tell the database apart.</param>
    public string DerivedDatabaseName(string suffix)
    {
        return TestDatabaseOwner.Derived(DatabaseName, suffix);
    }

    /// <summary>Creates <paramref name="database"/>, a name from <see cref="DerivedDatabaseName"/>, unless it exists.</summary>
    public Task CreateDatabaseAsync(string database, CancellationToken cancellationToken = default)
    {
        return Ownership.CreateDatabaseAsync(database, cancellationToken);
    }

    /// <summary>Adjusts a private container's builder, for a fixture whose <see cref="ShareContainer"/> is false.</summary>
    protected virtual PostgreSqlBuilder ConfigureContainer(PostgreSqlBuilder builder)
    {
        return builder;
    }

    /// <summary>Starts or attaches to the PostgreSQL container, then creates the fixture's database.</summary>
    /// <exception cref="InvalidOperationException">
    /// A fixture that shares its container also overrides <see cref="ConfigureContainer"/>.
    /// </exception>
    protected virtual async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _shared = SharedTestContainers.IsEnabled && ShareContainer;

        if (ShareContainer && _OverridesConfigureContainer())
        {
            throw new InvalidOperationException(
                $"{GetType().Name} overrides ConfigureContainer, so it must also return false from ShareContainer: a shared server keeps one configuration for every run."
            );
        }

        _container = await SharedTestContainers.StartAsync(_Build, cancellationToken).ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(
                _container,
                PostgreSqlBuilder.PostgreSqlPort,
                allowRestart: !_shared,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        _ownership = await PostgreSqlDatabaseOwnership
            .CreateAsync(AdminConnectionString, cancellationToken)
            .ConfigureAwait(false);
    }

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return InitializeAsync();
    }

    /// <summary>Disposes the fixture through <see cref="DisposeAsyncCore"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Drops the fixture's databases, then removes its container unless the container is shared.</summary>
    /// <remarks>A shared container is left running for the next fixture; disposing it would stop it under every other run.</remarks>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_ownership is not null)
        {
            await _ownership.DisposeAsync().ConfigureAwait(false);
        }

        if (_container is not null && !_shared)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private PostgreSqlContainer _Build()
    {
        var image = Image;
        var builder = new PostgreSqlBuilder(image)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithDatabase("postgres")
            .WithCommand("-c", _MaxConnections);

        builder = _shared
            ? SharedTestContainers.AsShared<PostgreSqlBuilder, PostgreSqlContainer>(
                builder,
                SharedTestContainers.ContainerName("postgresql", image, _MaxConnections)
            )
            : ConfigureContainer(builder);

        return builder.Build();
    }

    private bool _OverridesConfigureContainer()
    {
        var method = GetType()
            .GetMethod(
                nameof(ConfigureContainer),
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(PostgreSqlBuilder)]
            );

        return method is not null && method.DeclaringType != typeof(HeadlessPostgreSqlFixture);
    }
}
