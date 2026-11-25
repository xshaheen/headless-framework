// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Enrichment;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Internal;

/// <summary>
/// Registers the redacting logger factory as the last <see cref="ILoggerFactory"/>, over whichever factory was
/// registered before it.
/// </summary>
/// <remarks>
/// <para>
/// The factory the app registered stays in the collection untouched, and a redacting registration is appended after
/// it. Microsoft's <c>EnableEnrichment</c>, <c>EnableRedaction</c>, and buffering add their factory with
/// <c>TryAddEnumerable</c>, which compares implementation types: rewriting the existing registration into a factory
/// delegate would make a later call add an unwrapped factory that the container then resolves. Kept as is, the later
/// call finds its own registration and adds nothing.
/// </para>
/// <para>
/// When the wrapped factory takes enrichers (Microsoft's extended factory does), each one is handed over behind
/// <see cref="NameClassifyingLogEnricher"/>, because enrichment runs after the redacting logger. Enrichers registered
/// after this call are included: they are read when the factory is built.
/// </para>
/// </remarks>
internal static class RedactingLoggerFactoryRegistration
{
    public static void Register(IServiceCollection services)
    {
        // Each call moves the redacting registration to the end, so it wraps whatever factory came last before it.
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (_IsRedacting(services[i]))
            {
                services.RemoveAt(i);
            }
        }

        services.Add(
            ServiceDescriptor.Singleton<ILoggerFactory, RedactingLoggerFactory>(provider => _Create(provider, services))
        );
    }

    private static RedactingLoggerFactory _Create(IServiceProvider provider, IServiceCollection services)
    {
        var redactorProvider = provider.GetService<IRedactorProvider>();
        var propertyRedactors = provider.GetService<LogPropertyRedactors>() ?? LogPropertyRedactors.Empty;
        var inner = _CreateInner(provider, services, propertyRedactors);

        return new RedactingLoggerFactory(
            inner,
            redactorProvider,
            [.. provider.GetServices<ILogPathRedactor>()],
            LogTextRedactorRegistration.All(provider),
            propertyRedactors.Redactors
        );
    }

    private static ILoggerFactory _CreateInner(
        IServiceProvider provider,
        IServiceCollection services,
        LogPropertyRedactors propertyRedactors
    )
    {
        var descriptor =
            services.LastOrDefault(static d =>
                !d.IsKeyedService && d.ServiceType == typeof(ILoggerFactory) && !_IsRedacting(d)
            )
            ?? throw new InvalidOperationException(
                "AddHeadlessRedaction found no ILoggerFactory to wrap. Register logging (AddLogging) first."
            );

        if (descriptor.ImplementationInstance is ILoggerFactory instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return (ILoggerFactory)factory(provider);
        }

        var type = descriptor.ImplementationType!;

        if (propertyRedactors.Count == 0 || !_TakesEnrichers(type))
        {
            return (ILoggerFactory)ActivatorUtilities.CreateInstance(provider, type);
        }

        ILogEnricher[] enrichers =
        [
            .. provider
                .GetServices<ILogEnricher>()
                .Select(e => (ILogEnricher)new NameClassifyingLogEnricher(e, propertyRedactors)),
        ];
        IStaticLogEnricher[] staticEnrichers =
        [
            .. provider
                .GetServices<IStaticLogEnricher>()
                .Select(e => (IStaticLogEnricher)new NameClassifyingStaticLogEnricher(e, propertyRedactors)),
        ];

        return (ILoggerFactory)
            ActivatorUtilities.CreateInstance(
                provider,
                type,
                (IEnumerable<ILogEnricher>)enrichers,
                (IEnumerable<IStaticLogEnricher>)staticEnrichers
            );
    }

    private static bool _TakesEnrichers(Type type) =>
        type.GetConstructors()
            .Any(static c =>
                c.GetParameters().Any(static p => p.ParameterType == typeof(IEnumerable<ILogEnricher>))
                && c.GetParameters().Any(static p => p.ParameterType == typeof(IEnumerable<IStaticLogEnricher>))
            );

    private static bool _IsRedacting(ServiceDescriptor descriptor) =>
        !descriptor.IsKeyedService
        && descriptor.ServiceType == typeof(ILoggerFactory)
        && descriptor.ImplementationFactory?.GetType().GenericTypeArguments is [_, var result]
        && result == typeof(RedactingLoggerFactory);
}
