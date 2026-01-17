using Framework.Abstractions;
using Framework.Core;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Persistence;
using Framework.Messages.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public abstract class TestHost : IDisposable
{
    protected IServiceCollection Services { get; private set; } = null!;
    protected string ConnectionString { get; private set; } = null!;
    private IServiceProvider _provider = null!;
    private IServiceProvider? _scopedProvider;

    public TestHost()
    {
        _CreateServiceCollection();
        PreBuildServices();
        _BuildServices();
        PostBuildServices();
    }

    protected IServiceProvider Provider => _scopedProvider ?? _provider;

    private void _CreateServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();

        ConnectionString = ConnectionUtil.GetConnectionString();
        services.AddOptions<CapOptions>();
        services.Configure<PostgreSqlOptions>(x =>
        {
            x.ConnectionString = ConnectionString;
        });
        services.AddSingleton<PostgreSqlDataStorage>();
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        Services = services;
    }

    protected virtual void PreBuildServices() { }

    private void _BuildServices()
    {
        _provider = Services.BuildServiceProvider();
    }

    protected virtual void PostBuildServices() { }

    public IDisposable CreateScope()
    {
        var scope = CreateScope(_provider);
        var loc = scope.ServiceProvider;
        _scopedProvider = loc;
        return new DelegateDisposable(() =>
        {
            if (_scopedProvider == loc)
            {
                _scopedProvider = null;
            }
            scope.Dispose();
        });
    }

    public IServiceScope CreateScope(IServiceProvider provider)
    {
        var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return scope;
    }

    public T GetService<T>()
        where T : notnull => Provider.GetRequiredService<T>();

    public virtual void Dispose()
    {
        (_provider as IDisposable)?.Dispose();
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
