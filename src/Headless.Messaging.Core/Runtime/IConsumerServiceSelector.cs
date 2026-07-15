// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Runtime;

/// <summary>
/// Selects the consumer method(s) that should handle an incoming message. The default implementation
/// discovers <see cref="IConsume{TMessage}"/> registrations; consumers and dashboards resolve this
/// contract (via <see cref="MethodMatcherCache"/>) to inspect the registered consumer topology.
/// </summary>
[PublicAPI]
public interface IConsumerServiceSelector
{
    /// <summary>
    /// Selects the full set of <see cref="ConsumerExecutorDescriptor"/> candidates registered in the host.
    /// </summary>
    /// <returns>The registered consumer descriptors; empty when no consumers are registered.</returns>
    IReadOnlyList<ConsumerExecutorDescriptor> SelectCandidates();

    /// <summary>
    /// Selects the best <see cref="ConsumerExecutorDescriptor"/> candidate from <paramref name="candidates"/>
    /// for the supplied message name, honoring exact matches before wildcard patterns.
    /// </summary>
    /// <param name="key">The message name (or exchange routing key) to match.</param>
    /// <param name="candidates">The candidate descriptors to match against.</param>
    /// <returns>The matching descriptor, or <see langword="null"/> when none matches.</returns>
    ConsumerExecutorDescriptor? SelectBestCandidate(string key, IReadOnlyList<ConsumerExecutorDescriptor> candidates);

    /// <summary>Clears any cached candidate data, forcing re-evaluation on the next selection.</summary>
    void Invalidate();
}
