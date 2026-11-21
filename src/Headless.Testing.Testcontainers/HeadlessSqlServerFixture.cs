// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// SQL Server test fixture that automatically selects the correct container image based on CPU architecture, and
/// gives each fixture a database of its own.
/// Uses <see cref="TestImages.AzureSqlEdge"/> on ARM64 (Apple Silicon) and
/// <see cref="TestImages.MsSqlServer"/> on x86_64, unless <see cref="ImageOverrideVariable"/> names another image.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConnectionString"/> points at <see cref="DatabaseName"/>, a database the fixture creates on start and
/// drops on dispose. Derive any further database from it with <see cref="DerivedDatabaseName"/> and create it with
/// <see cref="CreateDatabaseAsync"/>, so the fixture also drops it. Use <see cref="AdminConnectionString"/>
/// (<c>master</c>) only for server-level statements.
/// </para>
/// <para>
/// When <see cref="SharedTestContainers.IsEnabled"/> and <see cref="ShareContainer"/> are both true, every fixture
/// attaches to one long-lived SQL Server container instead of starting its own. The fixture holds a session lock while
/// it lives, and a fixture that starts later drops the databases of any owner whose lock is free, so a killed run
/// leaves nothing behind for the next one.
/// </para>
/// <para>
/// After the container log reports readiness, <see cref="PortCheck"/> logs in through the published port, for up to two
/// minutes on a slow host, and proves the server that answers is this container's.
/// </para>
/// <para>
/// xUnit calls <see cref="IAsyncLifetime.InitializeAsync"/> explicitly, and a subclass overrides
/// <see cref="InitializeAsync"/> to seed its database after <c>base.InitializeAsync()</c> returns, and
/// overrides <see cref="DisposeAsyncCore"/> for teardown.
/// </para>
/// </remarks>
[PublicAPI]
public class HeadlessSqlServerFixture : IAsyncLifetime
{
    private const string _Password = "YourStrong@Passw0rd";
    private const int _SqlServerPort = 1433;

    /// <summary>
    /// The environment variable that, when set, replaces the architecture-selected image. It lets an ARM64 host run
    /// the suites against real SQL Server 2022 under x86_64 emulation (for example
    /// <c>mcr.microsoft.com/mssql/server:2022-latest</c> under Rosetta), since Azure SQL Edge is a different engine
    /// build. The image must log "SQL Server is now ready" and accept the <c>sa</c> password the fixture sets.
    /// </summary>
    public const string ImageOverrideVariable = "HEADLESS_SQLSERVER_IMAGE";

    // Use Azure SQL Edge for ARM64 (e.g., Apple Silicon), SQL Server 2022 for x86_64
    private static readonly string _DefaultImage =
        Environment.GetEnvironmentVariable(ImageOverrideVariable) is { Length: > 0 } image ? image
        : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? TestImages.AzureSqlEdge
        : TestImages.MsSqlServer;

    private IContainer? _container;
    private SqlServerDatabaseOwnership? _ownership;
    private bool _shared;

    /// <summary>
    /// Gets whether this fixture may attach to the shared SQL Server container when sharing is on. Override it to
    /// return <see langword="false"/> for a fixture that stops, pauses, or reconfigures its server, or whose tests
    /// read server-wide state another run would disturb.
    /// </summary>
    protected virtual bool ShareContainer => true;

    /// <summary>
    /// Gets the image the fixture runs. By default it is the image <see cref="ImageOverrideVariable"/> names, else
    /// <see cref="TestImages.AzureSqlEdge"/> on ARM64 and <see cref="TestImages.MsSqlServer"/> on x86_64. Override it
    /// to pin a fixture to one engine build. The image is part of the shared container's name, so fixtures on
    /// different images share separate servers and never attach to each other's.
    /// </summary>
    /// <remarks>The image must log "SQL Server is now ready" and accept the <c>sa</c> password the fixture sets.</remarks>
    protected virtual string Image => _DefaultImage;

    /// <summary>
    /// Gets the check that proves the container's published port reaches this container's server, by its machine name.
    /// Start a private container that a test stopped through its <see cref="PublishedPortCheck.StartAsync"/>, so a
    /// restart that lands on another host listener's port moves on.
    /// </summary>
    public PublishedPortCheck PortCheck { get; } =
        new(_SqlServerPort, PublishedPortProbes.SqlServerMachineName(_Password));

    /// <summary>Gets the started container, for its host, mapped port, or command execution.</summary>
    public IContainer Container =>
        _container ?? throw new InvalidOperationException("The SQL Server fixture has not been initialized.");

    /// <summary>Gets the name of the database this fixture owns.</summary>
    public string DatabaseName => Ownership.OwnerId;

    /// <summary>Gets the connection string of the database this fixture owns.</summary>
    public string ConnectionString => ConnectionStringFor(DatabaseName);

    /// <summary>Gets a <c>sa</c> connection string to <c>master</c>, for server-level statements only.</summary>
    public string AdminConnectionString => ConnectionStringFor("master");

    private SqlServerDatabaseOwnership Ownership =>
        _ownership ?? throw new InvalidOperationException("The SQL Server fixture has not been initialized.");

    /// <summary>Returns the connection string of <paramref name="database"/> on this fixture's server.</summary>
    public string ConnectionStringFor(string database)
    {
        var container =
            _container ?? throw new InvalidOperationException("The SQL Server fixture has not been initialized.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{container.Hostname},{container.GetMappedPublicPort(_SqlServerPort)}",
            InitialCatalog = database,
            UserID = "sa",
            Password = _Password,
            TrustServerCertificate = true,
        };

        return builder.ConnectionString;
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

    /// <summary>
    /// Starts or attaches to the SQL Server container, waits until its own server accepts a login through the published
    /// port, then creates the fixture's database.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The published port never reached this container's server: on any of three starts of a private container, or at
    /// once for a shared one.
    /// </exception>
    protected virtual async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _shared = SharedTestContainers.IsEnabled && ShareContainer;
        _container = await SharedTestContainers.StartAsync(_Build, cancellationToken).ConfigureAwait(false);
        await PortCheck.VerifyAsync(_container, allowRestart: !_shared, cancellationToken).ConfigureAwait(false);
        _ownership = await SqlServerDatabaseOwnership
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
    /// <remarks>
    /// Mirrors Testcontainers' own <c>ContainerFixture</c> teardown for a private container: a single
    /// <c>DisposeAsync</c> removes it, and Ryuk removes it if the test process dies first. A shared container is left
    /// running for the next fixture; disposing it would stop it under every other run.
    /// </remarks>
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

    private IContainer _Build()
    {
        var image = Image;
        var builder = new ContainerBuilder(image)
            .WithPortBinding(_SqlServerPort, assignRandomHostPort: true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", _Password)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready"));

        if (_shared)
        {
            builder = SharedTestContainers.AsShared<ContainerBuilder, IContainer>(
                builder,
                SharedTestContainers.ContainerName("sqlserver", image, "sa")
            );
        }

        return builder.Build();
    }
}
