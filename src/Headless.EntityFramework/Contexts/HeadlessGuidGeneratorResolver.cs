// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>
/// Picks the <see cref="IGuidGenerator"/> that produces a key for a context: the application's generator keyed by the
/// provider's GUID sort order, else its unkeyed generator, else the framework default for that order.
/// </summary>
/// <remarks>
/// Every lookup is cached because key generation runs once per added row. The provider's sort order is memoized per
/// <see cref="IDatabaseProvider"/> instance (a singleton of EF Core's internal service provider) because reading
/// <see cref="IDatabaseProvider.Name"/> builds a new <c>AssemblyName</c> each time. The generator is memoized per
/// application service provider, weakly, so a provider that is disposed and collected takes its entry with it.
/// </remarks>
internal static class HeadlessGuidGeneratorResolver
{
    private const string _SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

    private static readonly IGuidGenerator _Version7Generator = new SequentialGuidGenerator(
        SequentialGuidType.Version7
    );

    private static readonly IGuidGenerator _SqlServerGenerator = new SequentialGuidGenerator(
        SequentialGuidType.SqlServer
    );

    private static readonly ConditionalWeakTable<IDatabaseProvider, StrongBox<SequentialGuidType>> _ProviderKeys = [];

    private static readonly ConditionalWeakTable<
        IDatabaseProvider,
        StrongBox<SequentialGuidType>
    >.CreateValueCallback _CreateProviderKey = static provider => new(_GetKey(provider.Name));

    private static readonly ConditionalWeakTable<IServiceProvider, ApplicationGenerators> _ApplicationGenerators = [];

    private static readonly ConditionalWeakTable<
        IServiceProvider,
        ApplicationGenerators
    >.CreateValueCallback _CreateApplicationGenerators = static services => new(services);

    public static IGuidGenerator Resolve(DbContext context)
    {
        var key = _ProviderKeys.GetValue(context.GetService<IDatabaseProvider>(), _CreateProviderKey).Value;
        var applicationServices = context.GetApplicationServices();

        if (applicationServices is null)
        {
            return _GetFallback(key);
        }

        return _ApplicationGenerators.GetValue(applicationServices, _CreateApplicationGenerators).Get(key);
    }

    private static SequentialGuidType _GetKey(string? providerName)
    {
        return string.Equals(providerName, _SqlServerProviderName, StringComparison.Ordinal)
            ? SequentialGuidType.SqlServer
            : SequentialGuidType.Version7;
    }

    private static IGuidGenerator _GetFallback(SequentialGuidType key)
    {
        return key == SequentialGuidType.SqlServer ? _SqlServerGenerator : _Version7Generator;
    }

    /// <summary>
    /// The generators one application provider supplies, resolved on first use per sort order. The keyed-or-unkeyed
    /// answer is fixed for a built provider, so a benign race resolves the same instance twice at most.
    /// </summary>
    private sealed class ApplicationGenerators(IServiceProvider services)
    {
        private IGuidGenerator? _version7;
        private IGuidGenerator? _sqlServer;

        public IGuidGenerator Get(SequentialGuidType key)
        {
            return key == SequentialGuidType.SqlServer ? _sqlServer ??= _Resolve(key) : _version7 ??= _Resolve(key);
        }

        private IGuidGenerator _Resolve(SequentialGuidType key)
        {
            return services.GetKeyedService<IGuidGenerator>(key)
                ?? services.GetService<IGuidGenerator>()
                ?? _GetFallback(key);
        }
    }
}
