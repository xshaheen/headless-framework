// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A non-pooled context builds its options per scope by default, and shares one build across scopes only when the
/// caller asks for singleton options.
/// </summary>
public sealed class HeadlessDbContextOptionsLifetimeTests : TestBase
{
    // Shared options give each scope a copy that differs only in its application provider, so sharing shows in the
    // provider extension: one instance across scopes, where options built per scope build a new one each time.
    private static bool _ShareExtensions(DbContextOptions first, DbContextOptions second) =>
        ReferenceEquals(RelationalOptionsExtension.Extract(first), RelationalOptionsExtension.Extract(second));

    [Fact]
    public async Task should_build_options_per_scope_by_default()
    {
        // given: an action that counts its runs
        var calls = 0;
        await using var harness = Harness.Create(
            configure: (connection, options) =>
            {
                calls++;
                options.UseSqlite(connection);
            }
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        _ShareExtensions(first, second).Should().BeFalse();
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped, false)]
    [InlineData(ServiceLifetime.Singleton, true)]
    public async Task should_honor_an_explicit_options_lifetime(ServiceLifetime lifetime, bool shared)
    {
        // given
        await using var harness = Harness.Create(
            services => services.AddSingleton<IInterceptor>(new NoopCommandInterceptor()),
            optionsLifetime: lifetime
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        _ShareExtensions(first, second).Should().Be(shared);
        first.FindExtension<CoreOptionsExtension>()!.Interceptors.Should().ContainSingle();
    }

    [Fact]
    public async Task should_run_the_options_action_once_with_singleton_options()
    {
        // given
        var calls = 0;
        await using var harness = Harness.Create(
            optionsLifetime: ServiceLifetime.Singleton,
            configure: (connection, options) =>
            {
                calls++;
                options.UseSqlite(connection);
            }
        );

        // when
        harness.ResolveOptions();
        harness.ResolveOptions();
        harness.ResolveOptions();

        // then
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_apply_options_configurations_registered_after_the_context_with_singleton_options()
    {
        // given
        await using var harness = Harness.Create(
            after: services =>
                services.ConfigureDbContext<OptionsDbContext>(
                    (_, options) => options.EnableDetailedErrors(),
                    ServiceLifetime.Singleton
                ),
            optionsLifetime: ServiceLifetime.Singleton
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        _ShareExtensions(first, second).Should().BeTrue();
        first.FindExtension<CoreOptionsExtension>()!.DetailedErrorsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task should_build_the_same_extensions_as_ef_core_scoped_options()
    {
        // given
        await using var shared = Harness.Create(
            services => services.AddSingleton<IInterceptor>(new NoopCommandInterceptor()),
            optionsLifetime: ServiceLifetime.Singleton
        );
        await using var scoped = Harness.Create(
            services => services.AddSingleton<IInterceptor>(new NoopCommandInterceptor()),
            optionsLifetime: ServiceLifetime.Scoped
        );

        // when
        var sharedOptions = shared.ResolveOptions();
        var scopedOptions = scoped.ResolveOptions();

        // then
        sharedOptions
            .Extensions.Select(e => e.GetType())
            .Should()
            .BeEquivalentTo(scopedOptions.Extensions.Select(e => e.GetType()));
        RelationalOptionsExtension.Extract(sharedOptions).Connection.Should().NotBeNull();
        RelationalOptionsExtension
            .Extract(sharedOptions)
            .Connection.Should()
            .BeOfType(RelationalOptionsExtension.Extract(scopedOptions).Connection!.GetType());
    }

    [Fact]
    public async Task should_adopt_the_request_scope_when_a_context_is_created_by_hand_over_shared_options()
    {
        // given
        await using var harness = Harness.Create(optionsLifetime: ServiceLifetime.Singleton);
        var scope = harness.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<OptionsDbContext>>();

        // when
        await using var db = new OptionsDbContext(options);

        // then: the context resolves its collaborators from the request scope, not from a private one
        ((IHeadlessDbContext)db)
            .ServiceProvider.Should()
            .BeSameAs(scope.ServiceProvider);
    }

    [Fact]
    public async Task should_resolve_a_scoped_guid_generator_per_scope_with_shared_options()
    {
        // given
        await using var harness = Harness.Create(
            before: services =>
                services.AddKeyedScoped<IGuidGenerator, CountingGuidGenerator>(SequentialGuidType.Version7),
            optionsLifetime: ServiceLifetime.Singleton
        );
        var firstScope = harness.CreateScope();
        var secondScope = harness.CreateScope();

        // when
        firstScope.ServiceProvider.GetRequiredService<OptionsDbContext>().Add(new OptionsRow { Id = Guid.Empty });
        secondScope.ServiceProvider.GetRequiredService<OptionsDbContext>().Add(new OptionsRow { Id = Guid.Empty });

        // then: each scope's own generator served its key
        _Generator(firstScope).Calls.Should().Be(1);
        _Generator(secondScope).Calls.Should().Be(1);

        static CountingGuidGenerator _Generator(AsyncServiceScope scope) =>
            (CountingGuidGenerator)
                scope.ServiceProvider.GetRequiredKeyedService<IGuidGenerator>(SequentialGuidType.Version7);
    }

    [Fact]
    public async Task should_reuse_one_internal_service_provider_across_scopes_with_shared_options()
    {
        // given
        await using var harness = Harness.Create(optionsLifetime: ServiceLifetime.Singleton);

        // when: each scope's options carry that scope as the application provider
        var first = harness.CreateScope().ServiceProvider.GetRequiredService<OptionsDbContext>();
        var second = harness.CreateScope().ServiceProvider.GetRequiredService<OptionsDbContext>();

        // then: EF Core keys its internal provider without the application provider, so both reuse one
        // IModelSource is a singleton of EF Core's internal provider, so one instance means one internal provider.
        first.GetService<IModelSource>().Should().BeSameAs(second.GetService<IModelSource>());
    }

    [Fact]
    public async Task should_create_and_save_through_the_factory_with_shared_options_under_scope_validation()
    {
        // given
        await using var harness = Harness.Create(optionsLifetime: ServiceLifetime.Singleton, validateScopes: true);
        var factory = harness.Root.GetRequiredService<IDbContextFactory<OptionsDbContext>>();

        // when
        await using (var db = await factory.CreateDbContextAsync(AbortToken))
        {
            await db.Database.EnsureCreatedAsync(AbortToken);
            db.Add(new OptionsRow { Id = Guid.CreateVersion7() });
            await db.SaveChangesAsync(AbortToken);
        }

        // then
        await using var reader = await factory.CreateDbContextAsync(AbortToken);
        (await reader.Set<OptionsRow>().CountAsync(AbortToken)).Should().Be(1);
        _ShareExtensions(harness.ResolveOptions(), harness.ResolveOptions()).Should().BeTrue();
    }

    private sealed class NoopCommandInterceptor : DbCommandInterceptor;

    private sealed class CountingGuidGenerator : IGuidGenerator
    {
        public int Calls { get; private set; }

        public Guid Create()
        {
            Calls++;

            return Guid.CreateVersion7();
        }
    }

    public sealed class OptionsRow : Entity<Guid>;

    public sealed class OptionsDbContext(DbContextOptions<OptionsDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<OptionsRow> Rows => Set<OptionsRow>();

        public override string DefaultSchema => "";
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];

        public ServiceProvider Root { get; private set; } = null!;

        public static Harness Create(
            Action<IServiceCollection>? before = null,
            Action<IServiceCollection>? after = null,
            ServiceLifetime? optionsLifetime = null,
            bool validateScopes = false,
            Action<SqliteConnection, DbContextOptionsBuilder>? configure = null
        )
        {
            var harness = new Harness();
            harness._connection.Open();
            configure ??= static (connection, options) => options.UseSqlite(connection);
            var services = new ServiceCollection();
            services.AddLogging();
            before?.Invoke(services);
            if (optionsLifetime is { } lifetime)
            {
                services.AddHeadlessDbContext<OptionsDbContext>(
                    options => configure(harness._connection, options),
                    optionsLifetime: lifetime
                );
            }
            else
            {
                services.AddHeadlessDbContext<OptionsDbContext>(options => configure(harness._connection, options));
            }

            after?.Invoke(services);
            harness.Root = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = validateScopes, ValidateOnBuild = validateScopes }
            );

            return harness;
        }

        public AsyncServiceScope CreateScope()
        {
            var scope = Root.CreateAsyncScope();
            _scopes.Add(scope);

            return scope;
        }

        public DbContextOptions<OptionsDbContext> ResolveOptions() =>
            CreateScope().ServiceProvider.GetRequiredService<DbContextOptions<OptionsDbContext>>();

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
            {
                await scope.DisposeAsync();
            }

            await Root.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
