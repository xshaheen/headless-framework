// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Nats;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Fluent builder for NATS JetStream publish options applied to a single message type.</summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class NatsMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _subjectShardSelector;

    /// <summary>
    /// Appends a dynamic shard token to the NATS subject, producing <c>{subject}.{shard}</c>.
    /// Use this to fan messages across multiple stream subjects for horizontal scaling.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the shard token from the message instance.
    /// The token must be a single safe NATS subject token: it must be non-empty, at most 256 characters,
    /// and cannot contain <c>.</c>, <c>*</c>, <c>&gt;</c>, whitespace, or control characters. A non-null
    /// token that violates any of these rules causes an <see cref="InvalidOperationException"/> to be
    /// thrown at publish time rather than being silently dropped.
    /// Return <see langword="null"/> to skip sharding for this message.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public NatsMessageConfigBuilder<TMessage> SubjectShard(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _subjectShardSelector = selector;
        return this;
    }

    internal NatsMessageConfig<TMessage> Build()
    {
        return new(_subjectShardSelector);
    }
}

internal sealed class NatsMessageConfig<TMessage>(Func<TMessage, string?>? subjectShardSelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    // A message contract merges with an identical redeclaration from another module, so two configs holding the same
    // selector are equal and two different selectors conflict.
    private readonly Func<TMessage, string?>? _selector = subjectShardSelector;

    public override bool Equals(object? obj) =>
        obj is NatsMessageConfig<TMessage> other && Equals(_selector, other._selector);

    public override int GetHashCode() => _selector?.GetHashCode() ?? 0;

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        subjectShardSelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    NatsMessagingHeaders.SubjectShard,
                    message => NatsSubjectShard.Validate(subjectShardSelector((TMessage)message))
                ),
            ];
}
