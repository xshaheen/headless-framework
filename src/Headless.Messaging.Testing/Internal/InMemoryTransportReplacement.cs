// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Testing.Internal;

/// <summary>
/// Takes the broker transport an application registered through <c>AddHeadlessMessaging</c> out of a service collection
/// and installs the in-memory transport in its place.
/// </summary>
/// <remarks>
/// <para>
/// The descriptors to remove come from the ledger <c>AddHeadlessMessaging</c> keeps of what each provider extension
/// added, so nothing registered by the application or by another extension is touched, even when it shares a service
/// type with the transport.
/// </para>
/// <para>
/// Not everything a transport extension added is the transport's own. When it is the first to configure options or a
/// health check, it also adds the shared infrastructure behind them (the open-generic <c>IOptions&lt;&gt;</c> services,
/// the startup validator, the health check service and its bookkeeping), which every later registration relies on and
/// does not add again. Only descriptors that name the provider are removed: a type, closed generic argument, instance, or
/// factory from the provider's assembly, one of the messaging transport contracts, or a health check registration.
/// </para>
/// </remarks>
internal static class InMemoryTransportReplacement
{
    /// <summary>The transport name the in-memory provider registers.</summary>
    public const string InMemoryTransportName = "InMemory";

    // Contracts every transport provider registers through its own extension. A descriptor of one of these types that a
    // transport extension added belongs to that transport, whichever assembly its implementation or factory lives in.
    private static readonly HashSet<Type> _TransportContracts =
    [
        typeof(MessageQueueMarkerService),
        typeof(MessagingProviderCapabilities),
        typeof(ITransport),
        typeof(IBusTransport),
        typeof(IQueueTransport),
        typeof(IReplyTransport),
        typeof(IConsumerClientFactory),
    ];

    public static void Apply(IServiceCollection services)
    {
        if (MessagingTestHarness.IsRegistered(services))
        {
            throw new InvalidOperationException(
                "ReplaceMessagingTransportWithInMemory() must run before AddMessagingTestHarness(): the harness wraps the "
                    + "transport registered when it runs, so a later replacement would leave it recording the broker "
                    + "transport. Call services.ReplaceMessagingTransportWithInMemory().AddMessagingTestHarness()."
            );
        }

        var ledger =
            MessagingExtensionRegistrations.Find(services)
            ?? throw new InvalidOperationException(
                "ReplaceMessagingTransportWithInMemory() found no messaging registration to replace. Call it after "
                    + "AddHeadlessMessaging(...); in a WebApplicationFactory, call it inside ConfigureTestServices, which "
                    + "runs after the application's own registrations."
            );

        var transports = ledger.Entries.Where(static entry => entry.TransportName is not null).ToArray();
        var brokers = transports.Where(static entry => !_IsInMemory(entry)).ToArray();
        var hasInMemory = brokers.Length != transports.Length;

        foreach (var broker in brokers)
        {
            var provider = broker.Extension.GetType().Assembly;

            foreach (var descriptor in broker.AddedDescriptors.Where(descriptor => _IsOwned(descriptor, provider)))
            {
                services.Remove(descriptor);
            }

            ledger.Forget(broker);
        }

        _EnsureNoUnrecordedTransport(services);

        if (hasInMemory)
        {
            return;
        }

        var replaced = brokers.Select(static broker => broker.TransportName!).ToArray();

        // A throwaway builder runs the in-memory provider's own registration, so the installed transport is exactly what
        // setup.UseInMemory() registers and stays in step with it.
        var scratch = new MessagingSetupBuilder(services, new MessagingOptions());
        scratch.UseInMemory();

        foreach (var extension in scratch.Extensions)
        {
            ledger.Record(services, new GuardedInMemoryExtension(extension, replaced));
        }
    }

    private static bool _IsInMemory(MessagingExtensionRegistration entry) =>
        string.Equals(entry.TransportName, InMemoryTransportName, StringComparison.Ordinal);

    /// <summary>
    /// A transport added straight to the collection, outside a provider extension, has no ledger entry, so its
    /// registrations cannot be told apart from anyone else's. Two transports would then fail host startup with a less
    /// useful message, so this fails here instead.
    /// </summary>
    private static void _EnsureNoUnrecordedTransport(IServiceCollection services)
    {
        var unrecorded = services
            .Where(static descriptor => descriptor.ServiceType == typeof(MessageQueueMarkerService))
            .Select(static descriptor => (descriptor.ImplementationInstance as MessageQueueMarkerService)?.Name)
            .Where(static name => !string.Equals(name, InMemoryTransportName, StringComparison.Ordinal))
            .Select(static name => $"'{name ?? "unnamed"}'")
            .ToArray();

        if (unrecorded.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"ReplaceMessagingTransportWithInMemory() cannot replace the {string.Join(", ", unrecorded)} transport: it was "
                + "registered directly on the service collection rather than through AddHeadlessMessaging's provider "
                + "setup, so its registrations cannot be told apart from the application's. Register the transport with "
                + "its setup.Use…() call."
        );
    }

    private static bool _IsOwned(ServiceDescriptor descriptor, Assembly provider)
    {
        // Each one registers a check of this provider: the shared health check service and its bookkeeping are added
        // through other service types, so dropping these never removes another package's check.
        if (
            _TransportContracts.Contains(descriptor.ServiceType)
            || descriptor.ServiceType == typeof(IConfigureOptions<HealthCheckServiceOptions>)
        )
        {
            return true;
        }

        // Open generics are the shared options and logging infrastructure; a provider never owns one.
        if (descriptor.ServiceType.IsGenericTypeDefinition)
        {
            return false;
        }

        var implementationType = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType
            : descriptor.ImplementationType;
        var instance = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationInstance
            : descriptor.ImplementationInstance;
        var factory = descriptor.IsKeyedService
            ? (Delegate?)descriptor.KeyedImplementationFactory
            : descriptor.ImplementationFactory;

        return _Names(descriptor.ServiceType, provider)
            || _Names(implementationType, provider)
            || _Names(instance?.GetType(), provider)
            || _Names((instance as Delegate)?.Method.DeclaringType, provider)
            || _Names(factory?.Method.DeclaringType, provider);
    }

    /// <summary>
    /// Whether <paramref name="type"/> comes from <paramref name="provider"/> or is closed over a type that does. The
    /// generic arguments catch options plumbing such as <c>IValidateOptions&lt;RabbitMqMessagingOptions&gt;</c>, and the
    /// compiler-generated closure behind a generic method's lambda, which carries the method's type arguments.
    /// </summary>
    private static bool _Names(Type? type, Assembly provider)
    {
        if (type is null)
        {
            return false;
        }

        if (type.Assembly == provider)
        {
            return true;
        }

        return type.IsGenericType && type.GenericTypeArguments.Any(argument => _Names(argument, provider));
    }

    /// <summary>
    /// Runs the in-memory provider's registration, then makes its capability contribution reject a host whose messages
    /// require routing affinity, naming the replacement as the cause.
    /// </summary>
    /// <remarks>
    /// Startup would refuse such a host anyway, since the in-memory transport maps no routing affinity, but its message
    /// only names the route. The test author needs to know the swap is what removed the affinity the broker provided.
    /// </remarks>
    private sealed class GuardedInMemoryExtension(IMessagesOptionsExtension inner, string[] replaced)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            var before = new HashSet<ServiceDescriptor>(services, ReferenceEqualityComparer.Instance);

            inner.AddServices(services);

            for (var index = 0; index < services.Count; index++)
            {
                var descriptor = services[index];

                if (
                    before.Contains(descriptor)
                    || descriptor.ServiceType != typeof(MessagingProviderCapabilities)
                    || descriptor.ImplementationInstance is not MessagingProviderCapabilities capabilities
                )
                {
                    continue;
                }

                services[index] = ServiceDescriptor.Singleton(sp =>
                {
                    _EnsureNoRoutingAffinity(sp);
                    return capabilities;
                });
            }
        }

        private void _EnsureNoRoutingAffinity(IServiceProvider sp)
        {
            var routes = sp.GetRequiredService<IMessageMetadataRegistry>()
                .GetAll()
                .Where(static metadata => metadata.RequiresRoutingAffinity)
                .Select(static metadata => $"'{metadata.Route.MessageName}' ({metadata.Route.Lane})")
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (routes.Length == 0)
            {
                return;
            }

            var replacedNames =
                replaced.Length == 0
                    ? "no broker transport"
                    : string.Join(", ", replaced.Select(static name => $"'{name}'"));

            throw new MessagingConfigurationException(
                $"Routing affinity is required for {string.Join(", ", routes)}, but the in-memory transport that "
                    + $"ReplaceMessagingTransportWithInMemory() installed in place of {replacedNames} has no routing "
                    + "affinity: it delivers in process and maps no partition or session keys. Test messages that need "
                    + "routing affinity against the real broker, for example in a Testcontainers fixture."
            );
        }
    }
}
