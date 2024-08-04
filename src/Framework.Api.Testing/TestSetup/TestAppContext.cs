using System.Security.Claims;
using Framework.BuildingBlocks.Constants;
using MartinCostello.Logging.XUnit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Respawn;
using Respawn.Graph;
using Xunit.Abstractions;

namespace Framework.Api.Testing.TestSetup;

// TODO: Support multiple databases connections and make this more generic
public sealed class TestAppContext<TEntryPoint> : ITestOutputHelperAccessor, IDisposable, IAsyncDisposable
    where TEntryPoint : class
{
    private readonly string _connectionString;
    private readonly AsyncLazy<Respawner> _respawner;

    public ITestOutputHelper? OutputHelper { get; set; }

    public IServiceScopeFactory ScopeFactory { get; }

    public WebApplicationFactory<TEntryPoint> Factory { get; }

    public TestAppContext(
        Action<WebHostBuilderContext, IServiceCollection>? configureServices = null,
        Action<IWebHostBuilder>? configureHost = null
    )
    {
        Factory = CreateDefaultFactory(configureServices, configureHost);
        _connectionString = getConnectionString();
        _respawner = new(() => _CreateRespawnerAsync(_connectionString));
        ScopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();

        return;

        string getConnectionString()
        {
            var configuration = Factory.Services.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetRequiredConnectionString("SQL");

            return connectionString;
        }
    }

    public async Task ResetAsync()
    {
        var respawner = await _respawner;
        await respawner.ResetAsync(_connectionString);
    }

    private static Task<Respawner> _CreateRespawnerAsync(string connectionString)
    {
        return Respawner.CreateAsync(
            connectionString,
            new RespawnerOptions { TablesToIgnore = [new Table(HistoryRepository.DefaultTableName)], }
        );
    }

    #region Factories

    public DbContextExecutor<TDbContext> GetDbExecutor<TDbContext>()
        where TDbContext : DbContext => new(ScopeFactory);

    public WebApplicationFactory<TEntryPoint> CreateDefaultFactory(
        Action<WebHostBuilderContext, IServiceCollection>? configureServices,
        Action<IWebHostBuilder>? configureHost
    )
    {
        using var factory = new WebApplicationFactory<TEntryPoint>();

        factory.ClientOptions.AllowAutoRedirect = false;

        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(EnvironmentNames.Test);
            builder.ConfigureLogging(loggingBuilder => loggingBuilder.ClearProviders().AddXUnit(this));
            configureHost?.Invoke(builder);

            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
    }

    public TestAppContext<TEntryPoint> WithServiceConfig(
        Action<WebHostBuilderContext, IServiceCollection> configureServices
    )
    {
        return new TestAppContext<TEntryPoint>(configureServices: configureServices);
    }

    public TestAppContext<TEntryPoint> WithHostBuilder(Action<IWebHostBuilder> configureHost)
    {
        return new TestAppContext<TEntryPoint>(configureHost: configureHost);
    }

    #endregion

    #region Execute Scope

    public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> func, ClaimsPrincipal? claimsPrincipal = null)
    {
        await using var scope = ScopeFactory.CreateAsyncScope(claimsPrincipal);

        await func(scope.ServiceProvider);
    }

    public async Task<TResult> ExecuteScopeAsync<TResult>(
        Func<IServiceProvider, Task<TResult>> func,
        ClaimsPrincipal? claimsPrincipal = null
    )
    {
        await using var scope = ScopeFactory.CreateAsyncScope(claimsPrincipal);

        return await func(scope.ServiceProvider);
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        Factory.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync().ConfigureAwait(false);
    }

    #endregion
}
