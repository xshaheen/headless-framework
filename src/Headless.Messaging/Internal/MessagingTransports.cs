// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>The host's transports: the default transport and every <c>setup.AddTransport(name)</c>.</summary>
internal sealed class MessagingTransports
{
    private readonly Dictionary<string, MessagingTransport> _named;

    private MessagingTransports(MessagingTransport @default, IReadOnlyList<MessagingTransport> named)
    {
        Default = @default;
        Named = named;
        _named = named.ToDictionary(static t => t.Name!, StringComparer.OrdinalIgnoreCase);
        All = [@default, .. named];
    }

    /// <summary>The transport <c>setup.Use…</c> registered.</summary>
    public MessagingTransport Default { get; }

    /// <summary>The named transports, in registration order.</summary>
    public IReadOnlyList<MessagingTransport> Named { get; }

    /// <summary>The default transport, then the named ones in registration order.</summary>
    public IReadOnlyList<MessagingTransport> All { get; }

    /// <summary>
    /// Finds the transport <paramref name="name"/> names: the default one for <see langword="null"/> or its reserved name,
    /// otherwise the named one, compared case-insensitively.
    /// </summary>
    public bool TryGet(string? name, [NotNullWhen(true)] out MessagingTransport? transport)
    {
        if (MessagingSetupBuilder.IsDefaultTransport(name))
        {
            transport = Default;
            return true;
        }

        return _named.TryGetValue(name!, out transport);
    }

    /// <summary>The transport <paramref name="name"/> names.</summary>
    /// <exception cref="MessagingConfigurationException">The host registers no transport named <paramref name="name"/>.</exception>
    public MessagingTransport Get(string? name) =>
        TryGet(name, out var transport)
            ? transport
            : throw new MessagingConfigurationException(
                $"No transport named '{name}' is registered on this host. Register it with setup.AddTransport(\"{name}\")."
            );

    internal static MessagingTransports Create(IServiceProvider services) =>
        new(
            MessagingTransport.CreateDefault(services),
            [
                .. services
                    .GetServices<MessagingTransportRegistration>()
                    .Select(registration => MessagingTransport.CreateNamed(services, registration.Name)),
            ]
        );
}
