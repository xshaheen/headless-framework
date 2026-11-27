// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Nats;

/// <summary>
/// Returns the NATS subjects this host publishes to, with the host's isolation prefix (<c>Headless:Prefix</c>) applied,
/// so code outside Headless (a stream declared by hand, a worker in another language) can subscribe to them without
/// rebuilding the subject shape.
/// </summary>
/// <remarks>
/// Resolve it from the container after <c>UseNats(...)</c>; it is a singleton. A derived subject has the shape
/// <c>{root}.{lane}.{message-name}</c>, where the root is <c>headless</c>, or <c>{prefix}.headless</c> under a host
/// prefix, and the lane token is <c>bus</c> or <c>queue</c>. A message sharded with <c>SubjectShard(...)</c> publishes
/// to <c>{subject}.{shard}</c>, so subscribe to <c>{subject}.&gt;</c> to receive every shard.
/// </remarks>
public sealed class NatsSubjectResolver
{
    private readonly string _hostPrefix;
    private readonly NatsExternalSubjects _externalSubjects;
    private readonly IMessageMetadataRegistry? _metadata;

    internal NatsSubjectResolver(
        string hostPrefix,
        NatsExternalSubjects externalSubjects,
        IMessageMetadataRegistry? metadata
    )
    {
        _hostPrefix = hostPrefix;
        _externalSubjects = externalSubjects;
        _metadata = metadata;
        Root = NatsPhysicalAddress.SubjectRoot(hostPrefix);
    }

    /// <summary>
    /// Gets the first tokens of every subject this host derives: <c>headless</c>, or <c>{prefix}.headless</c> when the
    /// host sets <c>Headless:Prefix</c>.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Returns the root of one lane's derived subjects: <c>{root}.bus</c> or <c>{root}.queue</c>. Append <c>.&gt;</c>
    /// for a filter that covers every message the host publishes on the lane, or more tokens and <c>*</c> wildcards
    /// for a narrower filter.
    /// </summary>
    /// <param name="lane">The lane.</param>
    /// <returns>The lane root, without a trailing <c>.</c>.</returns>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="lane"/> is not a defined lane.</exception>
    public string LaneRoot(MessageLane lane)
    {
        Argument.IsInEnum(lane);

        return NatsPhysicalAddress.LaneRoot(_hostPrefix, lane);
    }

    /// <summary>Returns the subject this host publishes a message to on <paramref name="lane"/>.</summary>
    /// <param name="lane">The lane the message is sent on.</param>
    /// <param name="messageName">
    /// The effective message name, as it travels: with <c>MessagingOptions.MessageNamePrefix</c> already applied.
    /// </param>
    /// <returns>The subject, such as <c>headless.bus.orders.placed</c> or <c>wt1.headless.bus.orders.placed</c>.</returns>
    /// <remarks>A sharded message publishes to this subject plus <c>.{shard}</c>; see <see cref="Subject(MessageLane, string, string)"/>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="messageName"/> is <see langword="null"/>, empty, or white space.</exception>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="lane"/> is not a defined lane.</exception>
    /// <exception cref="InvalidOperationException">
    /// The message is bound to an external subject with <c>ExternalSubject(...)</c>, so it has no derived subject.
    /// </exception>
    public string Subject(MessageLane lane, string messageName)
    {
        Argument.IsInEnum(lane);
        Argument.IsNotNullOrWhiteSpace(messageName);
        _EnsureDerived(lane, messageName);

        return NatsPhysicalAddress.Subject(_hostPrefix, lane, messageName);
    }

    /// <summary>
    /// Returns the subject this host publishes one shard of a message to, for a contract declared with
    /// <c>SubjectShard(...)</c>.
    /// </summary>
    /// <param name="lane">The lane the message is sent on.</param>
    /// <param name="messageName">The effective message name, with <c>MessagingOptions.MessageNamePrefix</c> applied.</param>
    /// <param name="shard">The shard token the contract's selector returns.</param>
    /// <returns>The subject <c>{subject}.{shard}</c>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="messageName"/> is <see langword="null"/>, empty, or white space, or <paramref name="shard"/> is not
    /// a single safe subject token.
    /// </exception>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="lane"/> is not a defined lane.</exception>
    /// <exception cref="InvalidOperationException">The message is bound to an external subject.</exception>
    public string Subject(MessageLane lane, string messageName, string shard)
    {
        Argument.IsInEnum(lane);
        Argument.IsNotNullOrWhiteSpace(messageName);
        Argument.IsNotNull(shard);
        _EnsureDerived(lane, messageName);

        string validShard;

        try
        {
            validShard = NatsSubjectShard.Validate(shard)!;
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException(ex.Message, nameof(shard), ex);
        }

        return NatsPhysicalAddress.Subject(_hostPrefix, lane, $"{messageName}.{validShard}");
    }

    /// <summary>
    /// Returns the subject this host publishes <typeparamref name="TMessage"/> to on <paramref name="lane"/>, using the
    /// message name the messaging configuration assigns the contract, <c>MessageNamePrefix</c> included.
    /// </summary>
    /// <typeparam name="TMessage">The message contract declared with <c>Message&lt;TMessage&gt;(...)</c>.</typeparam>
    /// <param name="lane">The lane the contract is declared on.</param>
    /// <returns>The subject the transport publishes the contract to.</returns>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="lane"/> is not a defined lane.</exception>
    /// <exception cref="InvalidOperationException">
    /// The contract is not declared on <paramref name="lane"/>, is declared under more than one name there, or is bound
    /// to an external subject.
    /// </exception>
    public string Subject<TMessage>(MessageLane lane)
    {
        Argument.IsInEnum(lane);

        var names = (_metadata?.GetAll() ?? [])
            .Where(route => route.Route.Lane == lane && route.MessageType == typeof(TMessage))
            .Select(route => route.Route.MessageName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return names.Length switch
        {
            1 => Subject(lane, names[0]),
            0 => throw new InvalidOperationException(
                $"Message type {typeof(TMessage).FullName} is not declared on the {lane} lane, so it has no NATS subject. "
                    + "Declare it with ConfigureMessaging(m => m.Message<T>(...)), or pass its message name."
            ),
            _ => throw new InvalidOperationException(
                $"Message type {typeof(TMessage).FullName} is declared on the {lane} lane under more than one name "
                    + $"({string.Join(", ", names)}). Pass the message name instead."
            ),
        };
    }

    private void _EnsureDerived(MessageLane lane, string messageName)
    {
        if (_externalSubjects.Find(lane, messageName) is { } binding)
        {
            throw new InvalidOperationException(
                $"Message '{messageName}' on the {lane} lane is bound to the external NATS subject '{binding.Filter}', "
                    + "so it has no derived subject."
            );
        }
    }
}
