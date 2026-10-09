// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Headless.Checks;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>
/// Creates, verifies, or reconciles the JetStream streams Headless messages live on, under
/// <see cref="NatsMessagingOptions.StreamProvisioning"/>. A message lives on the declared stream
/// (<see cref="NatsMessagingOptions.Streams"/>) whose subjects cover its subject, or on a stream derived from its name.
/// Consumer clients run it at startup, the transport before the first publish to a stream, and the warm-up at host
/// start for every owned declared stream.
/// </summary>
/// <param name="options">The NATS options.</param>
/// <param name="backoffClock">
/// Measures publish-side back-off. A back-off waits real time, so this is never the application's registered clock;
/// tests pass a fake one.
/// </param>
internal sealed class NatsStreamProvisioner(IOptions<NatsMessagingOptions> options, TimeProvider? backoffClock = null)
{
    // A failed publish-side ensure is remembered for a back-off before a publish tries again, doubling to a ceiling:
    // every stream info, create, or update request goes through the cluster's meta leader, so a failing stream must not
    // turn each publish into another request. A configuration fault (a diverged or missing bound stream, a refused
    // create) cannot heal by retrying, so it backs off long; a transient fault (a timeout, a meta leader election, a
    // lost connection) backs off briefly, so publishes recover soon after the broker does.
    private static readonly Backoff _ConfigurationBackoff = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
    private static readonly Backoff _TransientBackoff = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

    private readonly NatsMessagingOptions _options = Argument.IsNotNull(options.Value);
    private readonly TimeProvider _backoffClock = backoffClock ?? TimeProvider.System;

    // One ensure per stream and required subject set per process. A Lazy keeps concurrent first publishes on one broker
    // round trip; a failure stays cached until its back-off ends, then the next publish tries again.
    private readonly ConcurrentDictionary<(string Stream, string Subjects), EnsureEntry> _ensured = new();

    public bool IsEnabled => _options.StreamProvisioning is not NatsStreamProvisioning.Disabled;

    /// <summary>Whether the catalog declares a stream Headless owns, so the warm-up has something to create.</summary>
    public bool HasOwnedDeclaredStreams => _options.Streams.Streams.Any(stream => stream.Owned);

    /// <summary>Returns the stream key of a message name: its first dot-separated segment.</summary>
    public static string StreamKey(string messageName)
    {
        var dot = messageName.IndexOf('.', StringComparison.Ordinal);

        return dot < 0 ? messageName : messageName[..dot];
    }

    /// <summary>
    /// Returns the logical subjects (without the lane prefix) a derived stream must carry for
    /// <paramref name="messageNames"/>, whose stream key is <paramref name="streamKey"/>.
    /// </summary>
    /// <remarks>
    /// A name that extends the key (<c>{key}.…</c>) contributes the key's wildcard, <c>{key}.&gt;</c>, rather than its own
    /// subject, which also covers its shards. Every host then asks for the same subject whichever of those names it
    /// publishes or consumes, so the first host to create the stream covers every later one and start order cannot fail
    /// a host under <see cref="NatsStreamProvisioning.Verify"/>. The bare key is added only for a name equal to it, plus
    /// the wildcard when that name is sharded, so an operator stream provisioned as <c>{key}.&gt;</c> verifies cleanly.
    /// </remarks>
    public static IReadOnlyList<string> BuildStreamSubjects(
        string streamKey,
        IEnumerable<string> messageNames,
        ISet<string> shardedMessageNames
    )
    {
        Argument.IsNotNullOrWhiteSpace(streamKey);
        Argument.IsNotNull(messageNames);
        Argument.IsNotNull(shardedMessageNames);

        var subjects = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keyPrefix = streamKey + ".";

        foreach (var messageName in messageNames)
        {
            if (messageName.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                add(keyPrefix + ">");
                continue;
            }

            add(messageName);

            if (shardedMessageNames.Contains(messageName))
            {
                add(messageName + ".>");
            }
        }

        return subjects;

        void add(string subject)
        {
            if (seen.Add(subject))
            {
                subjects.Add(subject);
            }
        }
    }

    /// <summary>
    /// Returns the stream <paramref name="messageName"/> lives on: the declared stream whose subjects cover its subject,
    /// or the stream derived from its name, carrying the subjects this message needs.
    /// </summary>
    public NatsStreamSpec Resolve(MessageLane lane, string messageName, bool isSharded = false)
    {
        if (_FindDeclared(lane, messageName) is { } declared)
        {
            return declared;
        }

        var key = StreamKey(messageName);
        var sharded = new HashSet<string>(StringComparer.Ordinal);
        if (isSharded)
        {
            sharded.Add(messageName);
        }

        return _Derived(lane, key, BuildStreamSubjects(key, [messageName], sharded));
    }

    /// <summary>Returns the name of the stream <paramref name="messageName"/> lives on.</summary>
    public string StreamName(MessageLane lane, string messageName) => Resolve(lane, messageName).Name;

    /// <summary>Ensures the streams for <paramref name="messageNames"/> on <paramref name="lane"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// A stream diverges from the configuration this host asks for and the mode does not allow fixing it, a bound stream
    /// is missing or does not carry its declared subjects, or a callback tried to change stream identity.
    /// </exception>
    public async Task EnsureAsync(
        INatsJSContext js,
        MessageLane lane,
        IReadOnlyCollection<string> messageNames,
        ISet<string> shardedMessageNames,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled)
        {
            return;
        }

        foreach (var spec in _ResolveGroups(lane, messageNames, shardedMessageNames))
        {
            await _EnsureStreamAsync(js, lane, spec, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ensures the stream one published message lives on, once per stream per process, and returns its state so the
    /// caller can tell when a Bus publish has no consumer to reach. A failed ensure fails every publish to that stream
    /// until its back-off ends.
    /// </summary>
    /// <returns>The stream's state, or <see langword="null"/> when provisioning is disabled.</returns>
    public Task<NatsStreamState?> EnsureForPublishAsync(
        INatsJSContext js,
        MessageLane lane,
        string messageName,
        bool isSharded,
        CancellationToken cancellationToken
    )
    {
        return IsEnabled
            ? _EnsureOnceAsync(js, lane, Resolve(lane, messageName, isSharded), cancellationToken)
            : Task.FromResult<NatsStreamState?>(null);
    }

    /// <summary>Ensures every owned declared stream, sharing the publish cache so a later publish finds it done.</summary>
    public async Task WarmUpAsync(INatsJSContext js, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        foreach (var spec in _options.Streams.Streams.Where(stream => stream.Owned))
        {
            // A warm-up failure (often a broker not reachable yet at host start) must not start a back-off that
            // fails the first publishes; the first publish simply tries again.
            await _EnsureOnceAsync(js, lane: null, spec, cancellationToken, backOffOnFailure: false)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Drops what this process remembers about a message's stream, so its next publish ensures it again.</summary>
    public void ForgetPublished(MessageLane lane, string messageName)
    {
        var stream = StreamName(lane, messageName);
        foreach (var key in _ensured.Keys)
        {
            if (string.Equals(key.Stream, stream, StringComparison.Ordinal))
            {
                _ensured.TryRemove(key, out _);
            }
        }
    }

    private NatsStreamSpec? _FindDeclared(MessageLane lane, string messageName)
    {
        var subject = NatsPhysicalAddress.Subject(lane, messageName);

        // Declared subjects never overlap one another (the catalog refuses it), so at most one stream matches.
        return _options.Streams.Streams.FirstOrDefault(stream =>
            stream.Subjects.Any(pattern => NatsStreamReconciliation.Matches(pattern, subject))
        );
    }

    private List<NatsStreamSpec> _ResolveGroups(
        MessageLane lane,
        IReadOnlyCollection<string> messageNames,
        ISet<string> shardedMessageNames
    )
    {
        // A declared stream is ensured once whichever of its names asked; names that share a derived stream are ensured
        // together with the union of their subjects.
        var specs = new Dictionary<string, NatsStreamSpec>(StringComparer.Ordinal);
        var derivedNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var name in messageNames)
        {
            if (_FindDeclared(lane, name) is { } declared)
            {
                specs.TryAdd(declared.Name, declared);
                continue;
            }

            var key = StreamKey(name);
            if (!derivedNames.TryGetValue(key, out var names))
            {
                derivedNames[key] = names = [];
            }

            names.Add(name);
        }

        foreach (var (key, names) in derivedNames)
        {
            var spec = _Derived(lane, key, BuildStreamSubjects(key, names, shardedMessageNames));
            specs.TryAdd(spec.Name, spec);
        }

        return [.. specs.Values];
    }

    private static NatsStreamSpec _Derived(MessageLane lane, string streamKey, IReadOnlyList<string> logicalSubjects)
    {
        return new NatsStreamSpec(
            NatsPhysicalAddress.Stream(lane, streamKey),
            Owned: true,
            [.. logicalSubjects.Select(subject => NatsPhysicalAddress.Subject(lane, subject))],
            NatsPhysicalAddress.Retention(lane),
            MaxAge: null,
            MaxBytes: null,
            Configure: null
        );
    }

    private async Task<NatsStreamState?> _EnsureOnceAsync(
        INatsJSContext js,
        MessageLane? lane,
        NatsStreamSpec spec,
        CancellationToken cancellationToken,
        bool backOffOnFailure = true
    )
    {
        var entry = _ensured.GetOrAdd(
            (spec.Name, string.Join(',', spec.Subjects)),
            static (_, clock) => new EnsureEntry(clock),
            _backoffClock
        );

        // Shared by every publisher of this stream, so no single caller's token may cancel it; StreamCreateTimeout
        // bounds it instead.
        var ensure = entry.Current(() => _EnsureStreamAsync(js, lane, spec, CancellationToken.None));

        try
        {
            return await ensure.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up waiting; the shared ensure keeps running for the next publisher.
            throw;
        }
        catch (Exception exception)
        {
            if (backOffOnFailure)
            {
                entry.Fail(ensure, exception);
            }
            else
            {
                entry.Discard(ensure);
            }

            throw;
        }
    }

    private async Task<NatsStreamState?> _EnsureStreamAsync(
        INatsJSContext js,
        MessageLane? lane,
        NatsStreamSpec spec,
        CancellationToken cancellationToken
    )
    {
        using var cts = _options.StreamCreateTimeout.ToCancellationTokenSource(cancellationToken);

        StreamInfo? live = null;

        try
        {
            live = (await js.GetStreamAsync(spec.Name, cancellationToken: cts.Token).ConfigureAwait(false)).Info;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404 || ex.Error.ErrCode == 10059)
        {
            // Stream does not exist yet; an owned stream is created below.
        }

        return spec.Owned
            ? await _EnsureOwnedAsync(js, spec, live, cts.Token).ConfigureAwait(false)
            : _CheckBound(lane, spec, live);
    }

    private static NatsStreamState _CheckBound(MessageLane? lane, NatsStreamSpec spec, StreamInfo? live)
    {
        // A bound stream belongs to whoever manages it outside the application: report what is wrong, never fix it.
        if (live is null)
        {
            throw new InvalidOperationException(
                $"NATS stream '{spec.Name}' is declared with Bind, so Headless never creates it, and it does not exist. "
                    + "Create it outside the application, or declare it with Own."
            );
        }

        var uncovered = NatsStreamReconciliation.FindUncoveredSubjects(spec.Subjects, live.Config.Subjects);

        if (uncovered.Count > 0)
        {
            throw new InvalidOperationException(
                $"NATS stream '{spec.Name}' does not carry {string.Join(", ", uncovered)}, which its Bind declaration "
                    + "names; JetStream delivers nothing to a consumer filter the stream does not carry."
            );
        }

        if (lane is MessageLane.Bus && live.Config.Retention is StreamConfigRetention.Workqueue)
        {
            throw new InvalidOperationException(
                $"NATS stream '{spec.Name}' uses work-queue retention, which refuses the separate durable consumer each "
                    + "Bus consumer group needs. Bind Bus messages to a stream with limits or interest retention."
            );
        }

        return new NatsStreamState(spec.Name, live.Config.Retention, live.State.ConsumerCount);
    }

    private async Task<NatsStreamState> _EnsureOwnedAsync(
        INatsJSContext js,
        NatsStreamSpec spec,
        StreamInfo? live,
        CancellationToken cancellationToken
    )
    {
        // Several hosts can contribute subjects to one derived stream, and an update REPLACES the subject list, so
        // union with whatever the stream already carries (from another host, or a pre-provisioned stream) to avoid
        // clobbering it.
        var subjects = new HashSet<string>(spec.Subjects, StringComparer.Ordinal);

        if (live?.Config.Subjects is { } existingSubjects)
        {
            subjects.UnionWith(existingSubjects);
        }

        // JetStream rejects a stream whose subject list contains overlapping entries, so drop any exact subject already
        // covered by a '.>' wildcard in the union (e.g. a pre-provisioned 'prefix.>' catch-all subsumes 'prefix.foo').
        var expectedSubjects = NatsConsumerClient.PruneOverlappingSubjects(subjects);
        var retention = spec.Retention ?? StreamConfigRetention.Limits;

        var config = new StreamConfig
        {
            Name = spec.Name,
            Subjects = [.. expectedSubjects],
            NoAck = false,
            // File storage is the production default. Override via StreamOptions
            // for dev/testing: config.Storage = StreamConfigStorage.Memory;
            Storage = StreamConfigStorage.File,
            Retention = retention,
        };

        // NATS's defaults keep a stream's messages without limit, so every owned stream gets an age limit unless the
        // application opts out with TimeSpan.Zero.
        var maxAge = spec.MaxAge ?? _options.DefaultStreamMaxAge;

        if (maxAge > TimeSpan.Zero)
        {
            config.MaxAge = maxAge;
        }

        if (spec.MaxBytes is { } maxBytes)
        {
            config.MaxBytes = maxBytes;
        }

        config.DuplicateWindow = _DuplicateWindow(spec, maxAge);

        // Snapshot either side of the callbacks so the comparison below can tell a field the operator
        // asserted from one the server will fill with its own default. Diffing an unasserted field would
        // report drift against every stream that exists.
        var beforeCallbacks = NatsStreamReconciliation.Snapshot(config);
        spec.Configure?.Invoke(config);
        _options.StreamOptions?.Invoke(config);
        var assertedFields = NatsStreamReconciliation.AssertedFields(
            beforeCallbacks,
            NatsStreamReconciliation.Snapshot(config)
        );

        // The provider sets these itself, so they are asserted whether or not a callback touched them.
        assertedFields.Add(nameof(StreamConfig.Storage));
        assertedFields.Add(nameof(StreamConfig.NoAck));
        assertedFields.Add(nameof(StreamConfig.Retention));
        assertedFields.Add(nameof(StreamConfig.DuplicateWindow));

        if (maxAge > TimeSpan.Zero)
        {
            assertedFields.Add(nameof(StreamConfig.MaxAge));
        }

        if (spec.MaxBytes is not null)
        {
            assertedFields.Add(nameof(StreamConfig.MaxBytes));
        }

        if (
            !string.Equals(config.Name, spec.Name, StringComparison.Ordinal)
            || config.Retention != retention
            || config.Subjects?.ToHashSet(StringComparer.Ordinal).SetEquals(expectedSubjects) != true
        )
        {
            throw new InvalidOperationException(
                $"NATS stream '{spec.Name}' identity, subjects, and retention are fixed by its declaration or its lane. "
                    + "StreamOptions and Configure may set storage, replicas, and limits but cannot change them."
            );
        }

        if (live is null)
        {
            // First-run creation happens in every enabled mode; only an existing stream is contentious.
            live = (await js.CreateStreamAsync(config, cancellationToken).ConfigureAwait(false)).Info;

            // CreateStreamAsync returns an existing same-name stream if another client won the race.
            // Treat that topology exactly like the pre-existing branch: preserve sibling subjects and
            // run the same verification/reconciliation decision instead of assuming this create won.
            if (live.Config.Subjects is { } racedSubjects)
            {
                subjects.UnionWith(racedSubjects);
                expectedSubjects = NatsConsumerClient.PruneOverlappingSubjects(subjects);
                config.Subjects = [.. expectedSubjects];
            }
        }

        var divergences = new List<StreamDivergence>(
            NatsStreamReconciliation.CompareFields(config, live.Config, assertedFields)
        );

        // A subject this host needs that the stream does not carry is not cosmetic drift: JetStream
        // delivers nothing, and reports no error, to a consumer filter that matches no subject.
        var uncovered = NatsStreamReconciliation.FindUncoveredSubjects(expectedSubjects, live.Config.Subjects);

        if (uncovered.Count > 0)
        {
            divergences.Add(
                new StreamDivergence(
                    nameof(StreamConfig.Subjects),
                    string.Join(", ", uncovered),
                    "not carried by the stream",
                    IsImmutable: false
                )
            );
        }

        var state = new NatsStreamState(spec.Name, retention, live.State.ConsumerCount);

        if (divergences.Count == 0)
        {
            return state;
        }

        var reconcilable = divergences.TrueForAll(divergence => !divergence.IsImmutable);

        if (_options.StreamProvisioning is NatsStreamProvisioning.Reconcile && reconcilable)
        {
            await js.UpdateStreamAsync(config, cancellationToken).ConfigureAwait(false);

            return state;
        }

        // Must stay an InvalidOperationException. ConsumerRegister.ExecuteAsync catches
        // BrokerConnectionException, flips the health flag, and returns, so raising a divergence as one
        // would hide it behind an unhealthy consumer instead of surfacing it. This also matches the
        // identity guard above, which already reports a configuration fault the same way.
        throw new InvalidOperationException(
            NatsStreamReconciliation.ComposeDivergenceMessage(spec.Name, divergences, _options.StreamProvisioning)
        );
    }

    // JetStream refuses a duplicate window longer than the stream's age limit. A declared window is the application's
    // explicit choice, so it fails loudly; the default window only shrinks to fit a short-lived stream.
    private TimeSpan _DuplicateWindow(NatsStreamSpec spec, TimeSpan maxAge)
    {
        if (spec.DuplicateWindow is { } declared)
        {
            if (maxAge > TimeSpan.Zero && declared > maxAge)
            {
                throw new InvalidOperationException(
                    $"NATS stream '{spec.Name}' duplicate window ({declared}) exceeds its MaxAge ({maxAge}); JetStream refuses it."
                );
            }

            return declared;
        }

        var window = _options.DefaultDuplicateWindow;

        return maxAge > TimeSpan.Zero && window > maxAge ? maxAge : window;
    }

    // The provisioner reports configuration faults as InvalidOperationException; JetStream reports a request it refuses
    // (an overlapping subject, an invalid setting) as a 4xx API error. Anything else, a 503 included, may heal.
    private static bool _IsConfigurationFault(Exception exception) =>
        exception is InvalidOperationException or NatsJSApiException { Error.Code: >= 400 and < 500 };

    /// <summary>One stream's publish-side ensure: the attempt in flight or done, and the back-off after a failure.</summary>
    private sealed class EnsureEntry(TimeProvider clock)
    {
        private readonly Lock _lock = new();
        private Lazy<Task<NatsStreamState?>>? _ensure;
        private ExceptionDispatchInfo? _failure;
        private long _retryAfter;
        private Backoff? _kind;
        private TimeSpan _backoff;

        public Lazy<Task<NatsStreamState?>> Current(Func<Task<NatsStreamState?>> start)
        {
            lock (_lock)
            {
                if (_failure is not null)
                {
                    // A monotonic timestamp, so a wall-clock change cannot shorten or extend the wait.
                    if (clock.GetTimestamp() < _retryAfter)
                    {
                        _failure.Throw();
                    }

                    _failure = null;
                    _ensure = null;
                }

                return _ensure ??= new Lazy<Task<NatsStreamState?>>(
                    start,
                    LazyThreadSafetyMode.ExecutionAndPublication
                );
            }
        }

        public void Discard(Lazy<Task<NatsStreamState?>> ensure)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_ensure, ensure) && _failure is null)
                {
                    _ensure = null;
                }
            }
        }

        public void Fail(Lazy<Task<NatsStreamState?>> ensure, Exception exception)
        {
            lock (_lock)
            {
                // Every waiter on a failed attempt lands here; only the first one starts the back-off.
                if (!ReferenceEquals(_ensure, ensure) || _failure is not null)
                {
                    return;
                }

                var kind = _IsConfigurationFault(exception) ? _ConfigurationBackoff : _TransientBackoff;

                // A fault of a different kind starts its own sequence: a broker that recovered into a diverged stream
                // should wait the configuration back-off from its start, not continue the transient one.
                if (!ReferenceEquals(kind, _kind))
                {
                    _kind = kind;
                    _backoff = kind.Initial;
                }

                _failure = ExceptionDispatchInfo.Capture(exception);
                _retryAfter = clock.GetTimestamp() + (long)(_backoff.TotalSeconds * clock.TimestampFrequency);
                _backoff = _backoff * 2 < kind.Max ? _backoff * 2 : kind.Max;
            }
        }
    }
}

/// <summary>The first wait and the ceiling of one kind of publish-side back-off.</summary>
internal sealed record Backoff(TimeSpan Initial, TimeSpan Max);

/// <summary>A stream as an ensure found it.</summary>
/// <param name="Stream">The stream name.</param>
/// <param name="Retention">The stream's retention policy.</param>
/// <param name="ConsumerCount">How many consumers the stream had when it was ensured.</param>
internal readonly record struct NatsStreamState(string Stream, StreamConfigRetention Retention, long ConsumerCount);
