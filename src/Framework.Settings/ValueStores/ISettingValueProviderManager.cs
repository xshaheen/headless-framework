using Framework.Settings.ValueProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Settings.ValueStores;

public interface ISettingValueProviderManager
{
    List<ISettingValueProvider> Providers { get; }
}

public sealed class SettingValueProviderManager : ISettingValueProviderManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FrameworkSettingOptions _options;
    private readonly Lazy<List<ISettingValueProvider>> _lazyProviders;

    public SettingValueProviderManager(IServiceProvider serviceProvider, IOptions<FrameworkSettingOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _lazyProviders = new(_GetProviders, true);
    }

    public List<ISettingValueProvider> Providers => _lazyProviders.Value;

    private List<ISettingValueProvider> _GetProviders()
    {
        var providers = _options
            .ValueProviders.Select(type => (_serviceProvider.GetRequiredService(type) as ISettingValueProvider)!)
            .ToList();

        var multipleProviders = providers.GroupBy(p => p.Name).FirstOrDefault(x => x.Count() > 1);

        if (multipleProviders is null)
        {
            return providers;
        }

        var providersText = multipleProviders.Select(p => p.GetType().FullName!).JoinAsString(Environment.NewLine);

        throw new InvalidOperationException(
            $"Duplicate setting value provider name detected: {multipleProviders.Key}. Providers:{Environment.NewLine}{providersText}"
        );
    }
}
