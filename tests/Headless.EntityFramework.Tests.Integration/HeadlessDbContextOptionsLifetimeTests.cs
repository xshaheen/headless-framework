// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
/// Without an explicit options lifetime, a non-pooled context shares one options instance across scopes unless a
/// scoped input could feed the options.
/// </summary>
public sealed class HeadlessDbContextOptionsLifetimeTests : TestBase
{
    [Fact]
    public async Task should_share_options_across_scopes_when_only_singleton_interceptors_are_registered()
    {
        // given
        await using var harness = Harness.Create(services =>
            services.AddSingleton<IInterceptor>(new NoopCommandInterceptor())
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        first.Should().BeSameAs(second);
        first.FindExtension<CoreOptionsExtension>()!.Interceptors.Should().ContainSingle();
    }

    [Fact]
    public async Task should_build_options_per_scope_when_a_scoped_interceptor_is_registered_after_the_context()
    {
        // given
        await using var harness = Harness.Create(after: services =>
            services.AddScoped<IInterceptor, NoopCommandInterceptor>()
        );

        // when
        await using var firstScope = harness.CreateScope();
        await using var secondScope = harness.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<DbContextOptions<OptionsDbContext>>();
        var second = secondScope.ServiceProvider.GetRequiredService<DbContextOptions<OptionsDbContext>>();

        // then
        first.Should().NotBeSameAs(second);
        first
            .FindExtension<CoreOptionsExtension>()!
            .Interceptors.Should()
            .ContainSingle()
            .Which.Should()
            .BeSameAs(firstScope.ServiceProvider.GetRequiredService<IInterceptor>());
    }

    [Fact]
    public async Task should_build_options_per_scope_when_another_options_configuration_is_registered()
    {
        // given
        await using var harness = Harness.Create(after: services =>
            services.ConfigureDbContext<OptionsDbContext>((_, options) => options.EnableDetailedErrors())
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        first.Should().NotBeSameAs(second);
        first.FindExtension<CoreOptionsExtension>()!.DetailedErrorsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task should_build_options_per_scope_when_the_action_creates_its_connection()
    {
        // given
        await using var harness = Harness.Create(
            configure: static (_, options) => options.UseSqlite(new SqliteConnection("Data Source=:memory:"))
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        first.Should().NotBeSameAs(second);
        RelationalOptionsExtension
            .Extract(first)
            .Connection.Should()
            .NotBeSameAs(RelationalOptionsExtension.Extract(second).Connection);
    }

    [Fact]
    public async Task should_build_options_per_scope_when_the_action_creates_an_interceptor()
    {
        // given
        await using var harness = Harness.Create(
            configure: static (connection, options) =>
                options.UseSqlite(connection).AddInterceptors(new NoopCommandInterceptor())
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public async Task should_share_options_when_the_action_adds_an_interceptor_it_captured()
    {
        // given
        var interceptor = new NoopCommandInterceptor();
        await using var harness = Harness.Create(
            configure: (connection, options) => options.UseSqlite(connection).AddInterceptors(interceptor)
        );

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        first.Should().BeSameAs(second);
    }

    [Theory]
    [InlineData(ServiceLifetime.Scoped, false)]
    [InlineData(ServiceLifetime.Singleton, true)]
    public async Task should_honor_an_explicit_options_lifetime(ServiceLifetime lifetime, bool shared)
    {
        // given
        await using var harness = Harness.Create(optionsLifetime: lifetime);

        // when
        var first = harness.ResolveOptions();
        var second = harness.ResolveOptions();

        // then
        ReferenceEquals(first, second).Should().Be(shared);
    }

    [Fact]
    public async Task should_build_the_same_extensions_as_ef_core_scoped_options()
    {
        // given
        await using var shared = Harness.Create(services =>
            services.AddSingleton<IInterceptor>(new NoopCommandInterceptor())
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
    public async Task should_create_and_save_through_the_factory_with_shared_options_under_scope_validation()
    {
        // given
        await using var harness = Harness.Create(validateScopes: true);
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
        harness.ResolveOptions().Should().BeSameAs(harness.ResolveOptions());
    }

    private sealed class NoopCommandInterceptor : DbCommandInterceptor;

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
