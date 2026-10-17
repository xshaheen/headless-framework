// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>
/// The service descriptors each <see cref="IMessagesOptionsExtension"/> added to the collection, in the order the
/// extensions ran.
/// </summary>
/// <remarks>
/// A provider's registrations are otherwise indistinguishable from everyone else's once they are in the collection, so a
/// test host that must take one provider out again, such as replacing a broker transport with the in-memory one, could
/// only guess which descriptors were its own. The ledger records them at the moment they are added, which is the only
/// point the answer is known. It is a registration-time record only: nothing resolves it at run time.
/// </remarks>
internal sealed class MessagingExtensionRegistrations
{
    private readonly List<MessagingExtensionRegistration> _entries = [];

    /// <summary>The recorded extensions, in the order they ran.</summary>
    public IReadOnlyList<MessagingExtensionRegistration> Entries => _entries;

    /// <summary>The ledger kept in <paramref name="services"/>, or <see langword="null"/> when messaging never ran there.</summary>
    public static MessagingExtensionRegistrations? Find(IServiceCollection services)
    {
        Argument.IsNotNull(services);

        return services
            .Where(static descriptor => descriptor.ServiceType == typeof(MessagingExtensionRegistrations))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<MessagingExtensionRegistrations>()
            .FirstOrDefault();
    }

    /// <summary>The ledger kept in <paramref name="services"/>, added on first use.</summary>
    public static MessagingExtensionRegistrations GetOrAdd(IServiceCollection services)
    {
        var ledger = Find(services);

        if (ledger is not null)
        {
            return ledger;
        }

        ledger = new MessagingExtensionRegistrations();
        services.AddSingleton(ledger);

        return ledger;
    }

    /// <summary>Runs <paramref name="extension"/> against <paramref name="services"/> and records what it added.</summary>
    /// <remarks>
    /// The diff compares descriptor references, never shapes: a descriptor another registration added first, even one
    /// identical in shape, stays out of the extension's entry, so a shared <c>TryAdd</c> registration is never attributed
    /// to the extension that only found it already there.
    /// </remarks>
    public MessagingExtensionRegistration Record(IServiceCollection services, IMessagesOptionsExtension extension)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(extension);

        var before = new HashSet<ServiceDescriptor>(services, ReferenceEqualityComparer.Instance);

        extension.AddServices(services);

        var entry = new MessagingExtensionRegistration(
            extension,
            [.. services.Where(descriptor => !before.Contains(descriptor))]
        );
        _entries.Add(entry);

        return entry;
    }

    /// <summary>Drops <paramref name="entry"/> from the ledger once its descriptors left the collection.</summary>
    public void Forget(MessagingExtensionRegistration entry)
    {
        Argument.IsNotNull(entry);

        _entries.Remove(entry);
    }
}

/// <summary>One extension and the descriptors it added.</summary>
/// <param name="Extension">The extension that ran.</param>
/// <param name="AddedDescriptors">The descriptors present after it ran that were absent before, in collection order.</param>
internal sealed record MessagingExtensionRegistration(
    IMessagesOptionsExtension Extension,
    IReadOnlyList<ServiceDescriptor> AddedDescriptors
)
{
    /// <summary>
    /// The transport name the extension registered, or <see langword="null"/> when it is not a transport. A transport is
    /// the extension that adds a <see cref="MessageQueueMarkerService"/>; storage and coordination extensions add none.
    /// </summary>
    public string? TransportName =>
        AddedDescriptors
            .Where(static descriptor => descriptor.ServiceType == typeof(MessageQueueMarkerService))
            .Select(static descriptor => (descriptor.ImplementationInstance as MessageQueueMarkerService)?.Name)
            .FirstOrDefault(static name => name is not null);
}
