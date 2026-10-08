// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>
/// The JetStream streams an application declares by name, each owned by Headless or bound to a stream managed
/// elsewhere. A message lives on the declared stream whose subjects cover its subject; a message no declared stream
/// covers gets a stream Headless derives from its name.
/// </summary>
/// <remarks>
/// Declare a stream when its name, subjects, or limits matter to operators, or when it is provisioned outside the
/// application (the NATS CLI, Terraform, the NACK Kubernetes controller) and Headless must never change it.
/// </remarks>
[PublicAPI]
public sealed class NatsStreamCatalog
{
    // nats-server rejects stream names containing whitespace, '.', '*', '>', '/', or '\' (they become file names and
    // subject tokens), and Headless reserves the names it derives.
    private static readonly char[] _InvalidNameCharacters = ['.', '*', '>', '/', '\\'];

    // Subject spaces Headless or the server owns: replies, JetStream's API, system events, and inboxes.
    private static readonly string[] _ReservedSubjects = ["headless.reply.>", "$JS.>", "$SYS.>", "_INBOX.>"];

    private readonly List<NatsStreamSpec> _streams = [];

    internal IReadOnlyList<NatsStreamSpec> Streams => _streams;

    /// <summary>
    /// Declares a stream Headless creates and keeps in shape under <see cref="NatsMessagingOptions.StreamProvisioning"/>.
    /// It defaults to limits retention, which suits both lanes, bounded by
    /// <see cref="NatsMessagingOptions.DefaultStreamMaxAge"/>.
    /// </summary>
    /// <param name="name">The JetStream stream name.</param>
    /// <param name="configure">Sets the subjects the stream carries (required) and optionally its retention and limits.</param>
    /// <returns>The same catalog for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// The name is invalid, already declared, or reserved, or the stream declares no subject or a reserved one.
    /// </exception>
    public NatsStreamCatalog Own(string name, Action<NatsStreamBuilder> configure)
    {
        Argument.IsNotNull(configure);
        _ValidateName(name);

        var builder = new NatsStreamBuilder();
        configure(builder);
        _ValidateSubjects(name, builder.StreamSubjects);

        _streams.Add(
            new NatsStreamSpec(
                name,
                Owned: true,
                [.. builder.StreamSubjects],
                builder.StreamRetention ?? StreamConfigRetention.Limits,
                builder.StreamMaxAge,
                builder.StreamMaxBytes,
                builder.ConfigureStream
            )
        );
        return this;
    }

    /// <summary>
    /// Declares a stream managed outside the application. Headless never creates, updates, or deletes it; it checks
    /// that the stream exists and carries the subjects of the messages that use it.
    /// </summary>
    /// <param name="name">The JetStream stream name.</param>
    /// <param name="subjects">The subjects the stream carries, so Headless knows which messages live on it.</param>
    /// <returns>The same catalog for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// The name is invalid, already declared, or reserved, or no subject or a reserved one is given.
    /// </exception>
    public NatsStreamCatalog Bind(string name, params string[] subjects)
    {
        Argument.IsNotNull(subjects);
        _ValidateName(name);
        _ValidateSubjects(name, subjects);

        _streams.Add(
            new NatsStreamSpec(
                name,
                Owned: false,
                [.. subjects],
                Retention: null,
                MaxAge: null,
                MaxBytes: null,
                Configure: null
            )
        );
        return this;
    }

    private void _ValidateName(string name)
    {
        Argument.IsNotNullOrWhiteSpace(name);

        if (name.IndexOfAny(_InvalidNameCharacters) >= 0 || name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"NATS stream name '{name}' cannot contain whitespace, '.', '*', '>', '/', or '\\'.",
                nameof(name)
            );
        }

        if (name.StartsWith("headless-", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"NATS stream name '{name}' is reserved: Headless derives the 'headless-' names itself.",
                nameof(name)
            );
        }

        if (_streams.Exists(stream => string.Equals(stream.Name, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"NATS stream '{name}' is already declared.", nameof(name));
        }
    }

    private void _ValidateSubjects(string name, IReadOnlyCollection<string> subjects)
    {
        if (subjects.Count == 0)
        {
            throw new ArgumentException($"NATS stream '{name}' must declare at least one subject.", nameof(subjects));
        }

        foreach (var subject in subjects)
        {
            if (string.IsNullOrWhiteSpace(subject) || subject.Split('.').Any(string.IsNullOrEmpty))
            {
                throw new ArgumentException(
                    $"NATS stream '{name}' has an invalid subject '{subject}'.",
                    nameof(subjects)
                );
            }

            if (_ReservedSubjects.Any(reserved => NatsStreamReconciliation.Overlaps(reserved, subject)))
            {
                throw new ArgumentException(
                    $"NATS stream '{name}' subject '{subject}' overlaps a reserved subject space ({string.Join(", ", _ReservedSubjects)}).",
                    nameof(subjects)
                );
            }

            // JetStream refuses a second stream whose subjects overlap an existing one, so two declarations that
            // overlap could never both exist, and a message could not tell which one it lives on.
            var overlapping = _streams.Find(stream =>
                stream.Subjects.Any(other => NatsStreamReconciliation.Overlaps(other, subject))
            );
            if (overlapping is not null)
            {
                throw new ArgumentException(
                    $"NATS stream '{name}' subject '{subject}' overlaps stream '{overlapping.Name}'.",
                    nameof(subjects)
                );
            }
        }
    }
}

/// <summary>Configures a stream declared with <see cref="NatsStreamCatalog.Own"/>.</summary>
[PublicAPI]
public sealed class NatsStreamBuilder
{
    private readonly List<string> _subjects = [];

    internal IReadOnlyCollection<string> StreamSubjects => _subjects;

    internal StreamConfigRetention? StreamRetention { get; private set; }

    internal TimeSpan? StreamMaxAge { get; private set; }

    internal long? StreamMaxBytes { get; private set; }

    internal Action<StreamConfig>? ConfigureStream { get; private set; }

    /// <summary>Adds subjects the stream carries; NATS wildcards (<c>*</c>, <c>&gt;</c>) are allowed.</summary>
    /// <param name="subjects">The subjects, such as <c>headless.queue.orders.&gt;</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    public NatsStreamBuilder Subjects(params string[] subjects)
    {
        Argument.IsNotNull(subjects);
        _subjects.AddRange(subjects);
        return this;
    }

    /// <summary>Sets the retention policy. Defaults to <see cref="StreamConfigRetention.Limits"/>.</summary>
    /// <param name="retention">The retention policy.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// <see cref="StreamConfigRetention.Workqueue"/> refuses the several durable consumers the Bus lane creates, and
    /// <see cref="StreamConfigRetention.Interest"/> discards a message no consumer exists for yet.
    /// </remarks>
    public NatsStreamBuilder Retention(StreamConfigRetention retention)
    {
        StreamRetention = retention;
        return this;
    }

    /// <summary>Sets how long the stream keeps a message. Defaults to <see cref="NatsMessagingOptions.DefaultStreamMaxAge"/>.</summary>
    /// <param name="maxAge">The maximum message age; <see cref="TimeSpan.Zero"/> keeps messages without an age limit.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> is negative.</exception>
    public NatsStreamBuilder MaxAge(TimeSpan maxAge)
    {
        StreamMaxAge = Argument.IsPositiveOrZero(maxAge);
        return this;
    }

    /// <summary>Sets the most bytes the stream stores before discarding its oldest messages.</summary>
    /// <param name="maxBytes">The byte limit.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is not positive.</exception>
    public NatsStreamBuilder MaxBytes(long maxBytes)
    {
        StreamMaxBytes = Argument.IsPositive(maxBytes);
        return this;
    }

    /// <summary>
    /// Adjusts any other stream setting (storage, replicas, discard policy). The name, subjects, and retention stay as
    /// declared.
    /// </summary>
    /// <param name="configure">The callback, run after the declared settings and before <see cref="NatsMessagingOptions.StreamOptions"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    public NatsStreamBuilder Configure(Action<StreamConfig> configure)
    {
        ConfigureStream = Argument.IsNotNull(configure);
        return this;
    }
}

/// <summary>One stream the provisioner ensures or checks: declared in the catalog, or derived from a message name.</summary>
internal sealed record NatsStreamSpec(
    string Name,
    bool Owned,
    IReadOnlyList<string> Subjects,
    StreamConfigRetention? Retention,
    TimeSpan? MaxAge,
    long? MaxBytes,
    Action<StreamConfig>? Configure
);
