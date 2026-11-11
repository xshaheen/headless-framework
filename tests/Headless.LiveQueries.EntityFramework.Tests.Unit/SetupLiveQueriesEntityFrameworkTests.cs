// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupLiveQueriesEntityFrameworkTests : TestBase
{
    [Fact]
    public void should_attach_interceptors_to_the_tracked_context_and_to_no_other()
    {
        var services = _Services();
        services.AddHeadlessLiveQueries(static setup => setup.UseEntityFramework<LiveDb>());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        _Interceptors<LiveDb>(scope.ServiceProvider).Should().HaveCount(3);
        _Interceptors<NotesDb>(scope.ServiceProvider).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_attach_interceptors_to_a_context_created_by_a_factory(bool pooled)
    {
        // given - tracking registered before the context, which either order must allow
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(static setup => setup.UseEntityFramework<LiveDb>());

        if (pooled)
        {
            services.AddPooledDbContextFactory<LiveDb>(static options => options.UseSqlite("DataSource=:memory:"));
        }
        else
        {
            services.AddDbContextFactory<LiveDb>(static options => options.UseSqlite("DataSource=:memory:"));
        }

        using var provider = services.BuildServiceProvider(validateScopes: true);

        // when
        using var context = provider.GetRequiredService<IDbContextFactory<LiveDb>>().CreateDbContext();

        // then
        var core = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();
        core!.Interceptors.Should().HaveCount(3);
    }

    [Fact]
    public void should_attach_once_and_apply_every_configuration_when_one_context_is_tracked_twice()
    {
        var services = _Services();
        services.AddHeadlessLiveQueries(static setup =>
            setup
                .UseEntityFramework<LiveDb>(static options => options.MaxCachedCommands = 10)
                .UseEntityFramework<LiveDb>(static (options, _) => options.MaxCachedCommands *= 2)
        );

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        _Interceptors<LiveDb>(scope.ServiceProvider).Should().HaveCount(3);
        _Options<LiveDb>(provider).MaxCachedCommands.Should().Be(20);
    }

    [Fact]
    public void should_keep_each_tracked_contexts_own_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notes:MaxCachedCommands"] = "7" })
            .Build();
        var services = _Services();
        services.AddHeadlessLiveQueries(setup =>
            setup
                .UseEntityFramework<LiveDb>(static options => options.MaxCachedCommands = 50)
                .UseEntityFramework<NotesDb>(configuration.GetSection("Notes"))
        );

        using var provider = services.BuildServiceProvider(validateScopes: true);

        _Options<LiveDb>(provider).MaxCachedCommands.Should().Be(50);
        _Options<NotesDb>(provider).MaxCachedCommands.Should().Be(7);
    }

    [Fact]
    public void should_reject_a_command_memory_of_zero()
    {
        var services = _Services();
        services.AddHeadlessLiveQueries(static setup =>
            setup.UseEntityFramework<LiveDb>(static options => options.MaxCachedCommands = 0)
        );

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var read = () => _Options<LiveDb>(provider);

        read.Should().Throw<OptionsValidationException>().WithMessage("*MaxCachedCommands*");
    }

    [Fact]
    public void should_start_no_background_work()
    {
        var services = _Services();

        services.AddHeadlessLiveQueries(static setup => setup.UseEntityFramework<LiveDb>());

        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }

    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LiveDb>(static options => options.UseSqlite("DataSource=:memory:"));
        services.AddDbContext<NotesDb>(static options => options.UseSqlite("DataSource=:memory:"));

        return services;
    }

    private static IEnumerable<object> _Interceptors<TContext>(IServiceProvider services)
        where TContext : DbContext
    {
        var core = services.GetRequiredService<DbContextOptions<TContext>>().FindExtension<CoreOptionsExtension>();

        return core?.Interceptors ?? [];
    }

    private static LiveQueriesEntityFrameworkOptions _Options<TContext>(IServiceProvider services)
    {
        return services
            .GetRequiredService<IOptionsMonitor<LiveQueriesEntityFrameworkOptions>>()
            .Get(typeof(TContext).FullName);
    }
}
