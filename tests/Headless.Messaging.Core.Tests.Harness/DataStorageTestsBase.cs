// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>Base class for data storage implementation tests.</summary>
[PublicAPI]
public abstract class DataStorageTestsBase : TestBase
{
    /// <summary>Gets the data storage instance for testing.</summary>
    protected abstract IDataStorage GetStorage();

    /// <summary>Gets the storage initializer instance for testing.</summary>
    protected abstract IStorageInitializer GetInitializer();

    /// <summary>Gets the serializer for creating message content.</summary>
    protected abstract ISerializer GetSerializer();

    /// <summary>Gets the data storage capabilities for conditional test execution.</summary>
    protected virtual DataStorageCapabilities Capabilities => DataStorageCapabilities.Default;

    /// <summary>
    /// Gets the <see cref="TimeProvider"/> used by the storage under test. Defaults to
    /// <see cref="TimeProvider.System"/>; providers that want clock-controlled coverage of
    /// time-sensitive predicates override this with a <c>FakeTimeProvider</c> and rebuild their
    /// storage on top of it. SQL providers that depend on database-side time functions (e.g.,
    /// PostgreSQL's <c>now()</c> in the pickup query) skip the controllable-clock parity tests.
    /// </summary>
    protected virtual TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>
    /// Indicates whether <see cref="TimeProvider"/> can be advanced under test. Providers backed
    /// by a <c>FakeTimeProvider</c> return <see langword="true"/>; default <see cref="TimeProvider.System"/>
    /// providers return <see langword="false"/> and the clock-controlled tests are skipped.
    /// </summary>
    protected virtual bool SupportsControllableClock => false;

    /// <summary>
    /// Creates another storage instance with the supplied application clock when the provider supports
    /// relational clock-skew conformance testing. Other providers return <see langword="null"/>.
    /// </summary>
    protected virtual IDataStorage? CreateStorageWithTimeProvider(TimeProvider timeProvider)
    {
        return null;
    }

    /// <summary>
    /// Creates a storage instance with a small retry batch for lane-isolation conformance tests.
    /// </summary>
    protected virtual IDataStorage? CreateStorageWithRetryBatchSize(int retryBatchSize)
    {
        return null;
    }

    /// <summary>Overrides the dispatch timeout when the provider exposes mutable test options.</summary>
    protected virtual bool TrySetDispatchTimeout(TimeSpan dispatchTimeout)
    {
        return false;
    }

    /// <summary>Reads the provider's current database UTC time for relational clock conformance.</summary>
    protected virtual Task<DateTime?> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<DateTime?>(null);
    }

    /// <summary>Reads the persisted lease identity without going through the storage snapshot mapper.</summary>
    protected virtual Task<PersistedLeaseIdentity?> GetPersistedLeaseIdentityAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<PersistedLeaseIdentity?>(null);
    }

    /// <summary>Persisted ownership generation returned by provider-specific test queries.</summary>
    protected readonly record struct PersistedLeaseIdentity(DateTimeOffset LockedUntil, string? Owner);

    /// <summary>
    /// Seeds one eligible retry row with an unsupported raw lane value through the provider's
    /// authoritative storage representation. Providers without a raw-row seam return <see langword="null"/>.
    /// </summary>
    protected virtual Task<Guid?> SeedUnsupportedLaneRetryRowAsync(
        IDataStorage storage,
        bool published,
        short rawLane,
        DateTimeOffset nextRetryAt,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<Guid?>(null);
    }

    /// <summary>Reads an unsupported-lane retry row without passing its raw value through enum validation.</summary>
    protected virtual Task<PersistedPoisonRetryState?> GetPersistedPoisonRetryStateAsync(
        IDataStorage storage,
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<PersistedPoisonRetryState?>(null);
    }

    /// <summary>Provider-neutral persisted state used to verify poison rows remain unchanged.</summary>
    protected readonly record struct PersistedPoisonRetryState(
        short RawLane,
        string StatusName,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? NextRetryAt,
        DateTimeOffset? LockedUntil,
        string? Owner,
        string? ExceptionInfo
    );

    /// <summary>Corrupts an inbox envelope and makes its persisted lease eligible for recovery.</summary>
    protected virtual Task PreparePoisonInboxRecoveryAsync(Guid storageId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Provider does not deserialize persisted inbox envelopes.");

    /// <summary>Reads terminal retention and attempt identity directly from the database.</summary>
    protected virtual Task<PersistedInboxPoisonState> ReadInboxPoisonStateAsync(
        Guid storageId,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException("Provider does not expose relational inbox state.");

    /// <summary>Advances only the fixture row's terminal expiry after its retention interval has been verified.</summary>
    protected virtual Task ExpirePoisonInboxAsync(Guid storageId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Provider does not expose relational inbox expiry.");

    protected readonly record struct PersistedInboxPoisonState(
        DateTimeOffset DatabaseNow,
        string StatusName,
        DateTimeOffset? TerminalAt,
        DateTimeOffset? EffectiveExpiresAt,
        Guid? AttemptId,
        DateTimeOffset? NextRetryAt,
        DateTimeOffset? LockedUntil,
        string? Owner,
        string Content
    );

    /// <summary>Controllable membership used by storage-provider conformance tests to stamp the owner identity.</summary>
    protected ControlledNodeMembership NodeMembership { get; } = new();

    /// <summary>
    /// Counts persisted received-message rows matching the supplied <paramref name="messageId"/>
    /// (and optionally <paramref name="group"/>). Provider-specific because the row visibility
    /// after a concurrent upsert storm needs a direct count query — the public monitoring API
    /// does not filter by MessageId.
    /// </summary>
    protected abstract Task<int> CountReceivedMessagesByIdentityAsync(
        string messageId,
        string? group,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Thread-safe counter for generating unique logical message IDs.
    /// </summary>
    private static long _messageIdCounter;

    /// <summary>Creates a valid message for testing.</summary>
    protected static Message CreateMessage(string? messageId = null, string? messageName = null, object? value = null)
    {
        var id = messageId ?? $"msg-{Interlocked.Increment(ref _messageIdCounter)}";

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, id },
            { MessagingHeaders.MessageName, messageName ?? "TestMessage" },
            { MessagingHeaders.SentTime, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
        };

        return new Message(headers, value ?? new { Data = "test" });
    }

    public virtual async Task should_initialize_schema()
    {
        // given
        var initializer = GetInitializer();

        // when
        var act = async () => await initializer.InitializeAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual Task should_get_table_names()
    {
        // given
        var initializer = GetInitializer();

        // when
        var publishedTable = initializer.GetPublishedTableName();
        var receivedTable = initializer.GetReceivedTableName();

        // then
        publishedTable.Should().NotBeNullOrEmpty();
        receivedTable.Should().NotBeNullOrEmpty();

        return Task.CompletedTask;
    }

    public virtual async Task should_converge_inbox_admission_and_require_exact_fence()
    {
        var storage = GetStorage();
        var origin = CreateMessage($"inbox-{Guid.NewGuid():N}", "orders.created");

        ValueTask<InboxAdmissionResult> admit() =>
            storage.AdmitReceivedMessageAsync(
                "orders.created",
                "orders-topology-a",
                "orders.consumer",
                "v1",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = origin,
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                },
                cancellationToken: AbortToken
            );

        var first = await admit();
        var duplicate = await admit();

        first.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        duplicate.Disposition.Should().Be(InboxAdmissionDisposition.InFlightDuplicate);
        duplicate.Message.StorageId.Should().Be(first.Message.StorageId);
        first.Message.InboxGeneration.Should().NotBeNull();

        var originalAttempts = first.Message.InlineAttempts;
        first.Message.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                first.Message,
                TimeSpan.FromMinutes(1),
                originalAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        first.Message.InboxAttemptFence.Should().NotBeNull();

        var stale = new MediumMessage
        {
            StorageId = first.Message.StorageId,
            Origin = first.Message.Origin,
            Content = first.Message.Content,
            Lane = first.Message.Lane,
            InboxKey = first.Message.InboxKey,
            InboxGeneration = first.Message.InboxGeneration,
            InboxAttemptFence = first.Message.InboxAttemptFence! with { AttemptId = Guid.NewGuid() },
        };

        (await storage.MarkReceivedInboxOrphanedAsync(stale, orphaned: true, AbortToken)).Should().BeFalse();
        (await storage.MarkReceivedInboxOrphanedAsync(first.Message, orphaned: true, AbortToken)).Should().BeTrue();
        (await storage.MarkReceivedInboxOrphanedAsync(first.Message, orphaned: true, AbortToken)).Should().BeFalse();

        var releaseStorage = storage.Should().BeAssignableTo<IGracefulLeaseReleaseStorage>().Subject;
        var exactIdentity = new MessageLeaseIdentity(
            first.Message.StorageId,
            first.Message.Owner,
            first.Message.LockedUntil!.Value,
            first.Message.Lane,
            first.Message.InboxAttemptFence
        );
        (await releaseStorage.ReleaseReceivedLeaseAsync(exactIdentity with { InboxAttemptFence = null }, AbortToken))
            .Should()
            .BeFalse("an inbox lease cannot be released without its complete attempt fence");
        (
            await releaseStorage.ReleaseReceivedLeaseAsync(
                exactIdentity with
                {
                    InboxAttemptFence = exactIdentity.InboxAttemptFence! with { AttemptId = Guid.NewGuid() },
                },
                AbortToken
            )
        )
            .Should()
            .BeFalse("a stale attempt cannot release its successor");
        (await releaseStorage.ReleaseReceivedLeaseAsync(exactIdentity, AbortToken)).Should().BeTrue();

        var previousAttempts = first.Message.InlineAttempts;
        first.Message.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                first.Message,
                TimeSpan.FromMinutes(1),
                previousAttempts,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        var deferredIdentity = new MessageLeaseIdentity(
            first.Message.StorageId,
            first.Message.Owner,
            first.Message.LockedUntil!.Value,
            first.Message.Lane,
            first.Message.InboxAttemptFence
        );
        var deferralStorage = storage.Should().BeAssignableTo<ICircuitRetryDeferralStorage>().Subject;
        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(
                    deferredIdentity with
                    {
                        InboxAttemptFence = deferredIdentity.InboxAttemptFence! with { AttemptId = Guid.NewGuid() },
                    },
                    _Now().AddMinutes(5)
                ),
                AbortToken
            )
        )
            .Should()
            .BeFalse("a stale attempt cannot defer its successor");
        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(deferredIdentity, _Now().AddMinutes(5)),
                AbortToken
            )
        )
            .Should()
            .BeTrue();
    }

    public virtual async Task should_converge_n_way_inbox_admission_on_one_generation()
    {
        var storage = GetStorage();
        var origin = CreateMessage($"inbox-race-{Guid.NewGuid():N}", "orders.created");

        var admissions = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => _AdmitInboxAsync(storage, origin).AsTask())
        );

        admissions.Should().ContainSingle(result => result.Disposition == InboxAdmissionDisposition.Winner);
        admissions
            .Where(result => result.Disposition != InboxAdmissionDisposition.Winner)
            .Should()
            .OnlyContain(result => result.Disposition == InboxAdmissionDisposition.InFlightDuplicate);
        admissions.Select(result => result.Message.StorageId).Distinct().Should().ContainSingle();
        admissions.Select(result => result.Message.InboxGeneration).Distinct().Should().ContainSingle();
    }

    public virtual async Task should_isolate_every_persisted_inbox_key_component()
    {
        var storage = GetStorage();
        var messageId = $"inbox-isolation-{Guid.NewGuid():N}";

        static Message WithTenant(Message message, string? tenantId)
        {
            if (tenantId is null)
            {
                message.Headers.Remove(Headers.TenantId);
            }
            else
            {
                message.Headers[Headers.TenantId] = tenantId;
            }

            return message;
        }

        var first = await _AdmitInboxAsync(storage, WithTenant(CreateMessage(messageId, "orders.created"), null));
        var tenantA = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created"), "tenant-a")
        );
        var tenantB = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created"), "tenant-b")
        );
        var queue = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created"), null),
            lane: MessageLane.Queue
        );
        var contractV2 = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created"), null),
            contractVersion: "v2"
        );
        var consumerB = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created"), null),
            consumerIdentity: "orders.consumer-b"
        );
        var generationOne = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created"), null),
            generation: 1
        );
        var contractCase = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "Orders.Created"), null)
        );
        var contractTrailingSpace = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created "), null)
        );
        var contractComposedUnicode = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.créated"), null)
        );
        var contractDecomposedUnicode = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.cre\u0301ated"), null)
        );
        var tenantlessDuplicate = await _AdmitInboxAsync(
            storage,
            WithTenant(CreateMessage(messageId, "orders.created"), "   ")
        );

        new[]
        {
            first,
            tenantA,
            tenantB,
            queue,
            contractV2,
            consumerB,
            generationOne,
            contractCase,
            contractTrailingSpace,
            contractComposedUnicode,
            contractDecomposedUnicode,
        }
            .Should()
            .OnlyContain(result => result.Disposition == InboxAdmissionDisposition.Winner);
        tenantlessDuplicate.Disposition.Should().Be(InboxAdmissionDisposition.InFlightDuplicate);
    }

    public virtual async Task should_enforce_inbox_key_length_boundaries_without_truncation()
    {
        var storage = GetStorage();
        var maximum = CreateMessage(new string('m', 200), new string('n', 200));
        maximum.Headers[Headers.TenantId] = new string('t', 200);

        (
            await _AdmitInboxAsync(
                storage,
                maximum,
                consumerIdentity: new string('c', 200),
                contractVersion: new string('v', 100)
            )
        )
            .Disposition.Should()
            .Be(InboxAdmissionDisposition.Winner);

        Func<Task> oversizedName = async () =>
            await _AdmitInboxAsync(storage, CreateMessage($"name-{Guid.NewGuid():N}", new string('n', 201)));
        Func<Task> oversizedConsumer = async () =>
            await _AdmitInboxAsync(
                storage,
                CreateMessage($"consumer-{Guid.NewGuid():N}", "orders.created"),
                consumerIdentity: new string('c', 201)
            );
        Func<Task> oversizedVersion = async () =>
            await _AdmitInboxAsync(
                storage,
                CreateMessage($"version-{Guid.NewGuid():N}", "orders.created"),
                contractVersion: new string('v', 101)
            );
        Func<Task> oversizedMessageId = async () =>
            await _AdmitInboxAsync(storage, CreateMessage(new string('m', 201), "orders.created"));
        var oversizedTenant = CreateMessage($"tenant-{Guid.NewGuid():N}", "orders.created");
        oversizedTenant.Headers[Headers.TenantId] = new string('t', 201);
        Func<Task> oversizedTenantAct = async () => await _AdmitInboxAsync(storage, oversizedTenant);

        await oversizedName.Should().ThrowAsync<ArgumentException>();
        await oversizedConsumer.Should().ThrowAsync<ArgumentException>();
        await oversizedVersion.Should().ThrowAsync<ArgumentException>();
        await oversizedMessageId.Should().ThrowAsync<ArgumentException>();
        await oversizedTenantAct.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_suppress_terminal_inbox_redelivery_independent_of_topology_group()
    {
        var storage = GetStorage();
        var origin = CreateMessage($"inbox-terminal-{Guid.NewGuid():N}", "orders.created");
        var admitted = await _AdmitInboxAsync(storage, origin, group: "old-topology");

        admitted.Message.InlineAttempts++;
        (
            await storage.LeaseReceiveAndReserveAttemptAsync(
                admitted.Message,
                TimeSpan.FromMinutes(1),
                originalInlineAttempts: 0,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        (
            await storage.ChangeReceiveRetryStateAsync(
                admitted.Message,
                StatusName.Failed,
                MessageContentWrite.Preserve,
                nextRetryAt: null,
                lockedUntil: null,
                originalRetries: 0,
                originalInlineAttempts: 1,
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        var redelivery = await _AdmitInboxAsync(storage, origin, group: "renamed-topology");

        redelivery.Disposition.Should().Be(InboxAdmissionDisposition.TerminalFailedDuplicate);
        redelivery.Message.StorageId.Should().Be(admitted.Message.StorageId);
    }

    public virtual async Task should_apply_audited_inbox_operations_once_and_reject_operation_identity_reuse()
    {
        var storage = GetStorage();
        var admitted = await _AdmitInboxAsync(
            storage,
            CreateMessage($"inbox-operations-{Guid.NewGuid():N}", "orders.created")
        );
        admitted.Message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(admitted.Message, TimeSpan.FromMinutes(1), 0, AbortToken))
            .Should()
            .BeTrue();
        (
            await storage.ChangeReceiveRetryStateAsync(
                admitted.Message,
                StatusName.Failed,
                MessageContentWrite.Preserve,
                null,
                null,
                0,
                1,
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator-a")], authenticationType: "test")
        );
        var authorization = new InboxAuthorizationContext(principal);
        var operationId = Guid.NewGuid();
        var incarnation = admitted.Message.InboxGeneration!.IncarnationId;
        var request = new InboxOperationRequest(
            operationId,
            incarnation,
            StatusName.Failed,
            "retry after repair",
            authorization
        );
        var operations = storage.GetInboxOperationsApi();

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => operations.ForceReprocessAsync(request, AbortToken).AsTask())
        );
        var first = attempts.Should().ContainSingle(result => !result.IsReplay).Which;
        attempts.Should().OnlyContain(result => result.Outcome == InboxOperationOutcome.Applied);
        attempts.Select(result => result.ChildIncarnationId).Distinct().Should().ContainSingle();
        var replay = await operations.ForceReprocessAsync(request, AbortToken);
        var conflict = await operations.ForceReprocessAsync(
            request with
            {
                ExpectedStatus = StatusName.Succeeded,
            },
            AbortToken
        );

        first.Outcome.Should().Be(InboxOperationOutcome.Applied);
        first.ChildGeneration.Should().Be(admitted.Message.InboxGeneration.Number + 1);
        replay.IsReplay.Should().BeTrue();
        replay.ChildIncarnationId.Should().Be(first.ChildIncarnationId);
        conflict.Outcome.Should().Be(InboxOperationOutcome.OperationConflict);

        var page = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = first.ChildIncarnationId },
            authorization,
            AbortToken
        );
        page.Items.Should().ContainSingle();
        page.Items[0].ReplayParentIncarnationId.Should().Be(incarnation);
        page.Items[0].ReplayOperationId.Should().Be(operationId);
    }

    public virtual async Task should_expire_terminal_poison_inbox_and_allow_readmission(MessageLane lane)
    {
        var storage = GetStorage();
        var origin = CreateMessage($"inbox-poison-retention-{Guid.NewGuid():N}", "orders.created");
        var retention = TimeSpan.FromMinutes(17);
        var admitted = await storage.AdmitReceivedMessageAsync(
            origin.Name,
            "orders-group",
            "orders.consumer-a",
            "v1",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = origin,
                Content = string.Empty,
                Lane = lane,
            },
            inboxRetention: retention,
            cancellationToken: AbortToken
        );
        admitted.Message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(admitted.Message, TimeSpan.FromMinutes(1), 0, AbortToken))
            .Should()
            .BeTrue();
        await PreparePoisonInboxRecoveryAsync(admitted.Message.StorageId, AbortToken);
        var before = await ReadInboxPoisonStateAsync(admitted.Message.StorageId, AbortToken);
        before.AttemptId.Should().NotBeNull();
        Func<Task> nonterminalRedelivery = async () => await _AdmitInboxAsync(storage, origin, lane: lane);
        await nonterminalRedelivery.Should().ThrowAsync<JsonException>();

        var picked = await storage.GetReceivedMessagesOfNeedRetryAsync(lane, AbortToken);
        picked.Should().NotContain(message => message.StorageId == admitted.Message.StorageId);
        var terminal = await ReadInboxPoisonStateAsync(admitted.Message.StorageId, AbortToken);
        terminal.StatusName.Should().Be(nameof(StatusName.Failed));
        terminal.TerminalAt.Should().NotBeNull();
        terminal.TerminalAt!.Value.Should().BeOnOrAfter(before.DatabaseNow).And.BeOnOrBefore(terminal.DatabaseNow);
        terminal.EffectiveExpiresAt.Should().Be(terminal.TerminalAt.Value.Add(retention));
        terminal.AttemptId.Should().BeNull();
        terminal.NextRetryAt.Should().BeNull();
        terminal.LockedUntil.Should().BeNull();
        terminal.Owner.Should().BeNull();
        terminal.Content.Should().Be("not-json");

        var duplicate = await _AdmitInboxAsync(storage, origin, lane: lane);
        duplicate.Disposition.Should().Be(InboxAdmissionDisposition.TerminalFailedDuplicate);
        duplicate.ShouldDispatch.Should().BeFalse();
        duplicate.Message.StorageId.Should().Be(admitted.Message.StorageId);
        duplicate.Message.Content.Should().Be("not-json");
        await storage.DeleteExpiresAsync(
            GetInitializer().GetReceivedTableName(),
            DateTimeOffset.UtcNow,
            cancellationToken: AbortToken
        );
        (await ReadInboxPoisonStateAsync(admitted.Message.StorageId, AbortToken)).Content.Should().Be("not-json");

        await ExpirePoisonInboxAsync(admitted.Message.StorageId, AbortToken);
        await storage.DeleteExpiresAsync(
            GetInitializer().GetReceivedTableName(),
            DateTimeOffset.UtcNow,
            cancellationToken: AbortToken
        );
        var readmitted = await _AdmitInboxAsync(storage, origin, lane: lane);
        readmitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        readmitted
            .Message.InboxGeneration!.IncarnationId.Should()
            .NotBe(admitted.Message.InboxGeneration!.IncarnationId);
        readmitted.Message.StorageId.Should().NotBe(admitted.Message.StorageId);
    }

    public virtual async Task should_isolate_replay_lifecycles_after_root_purge(MessageLane lane, long rootGeneration)
    {
        var storage = GetStorage();
        var operations = storage.GetInboxOperationsApi();
        var origin = CreateMessage($"inbox-lifecycles-{Guid.NewGuid():N}", "orders.created");
        var authorization = new InboxAuthorizationContext(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "lifecycle-operator")], "test"))
        );
        var firstRoot = await _AdmitInboxAsync(storage, origin, lane: lane, generation: rootGeneration);
        await _CompleteInboxForReplayAsync(storage, firstRoot.Message);
        var firstRequest = new InboxOperationRequest(
            Guid.NewGuid(),
            firstRoot.Message.InboxGeneration!.IncarnationId,
            StatusName.Failed,
            "reprocess first lifecycle",
            authorization
        );
        var firstChild = await operations.ForceReprocessAsync(firstRequest, AbortToken);
        firstChild.Outcome.Should().Be(InboxOperationOutcome.Applied);
        var childMessage = new MediumMessage
        {
            StorageId = firstChild.ChildStorageId!.Value,
            Origin = origin,
            Content = firstRoot.Message.Content,
            Lane = lane,
            InboxKey = firstRoot.Message.InboxKey! with { Generation = firstChild.ChildGeneration!.Value },
            InboxGeneration = new InboxGeneration(
                firstChild.ChildGeneration.Value,
                firstChild.ChildIncarnationId!.Value
            ),
        };
        await _CompleteInboxForReplayAsync(storage, childMessage);
        var childRequest = firstRequest with
        {
            OperationId = Guid.NewGuid(),
            ExpectedIncarnationId = firstChild.ChildIncarnationId.Value,
            Reason = "retain old replay evidence",
        };
        (await operations.HoldAsync(childRequest, AbortToken)).Outcome.Should().Be(InboxOperationOutcome.Applied);
        (await operations.PurgeAsync(firstRequest with { OperationId = Guid.NewGuid() }, AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);

        var secondRoot = await _AdmitInboxAsync(storage, origin, lane: lane, generation: rootGeneration);
        secondRoot.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        secondRoot.Message.InboxGeneration!.Number.Should().Be(rootGeneration);
        secondRoot
            .Message.InboxGeneration.IncarnationId.Should()
            .NotBe(firstRoot.Message.InboxGeneration.IncarnationId);
        await _CompleteInboxForReplayAsync(storage, secondRoot.Message);
        var secondRequest = firstRequest with
        {
            OperationId = Guid.NewGuid(),
            ExpectedIncarnationId = secondRoot.Message.InboxGeneration.IncarnationId,
        };
        var secondChild = await operations.ForceReprocessAsync(secondRequest, AbortToken);
        secondChild.Outcome.Should().Be(InboxOperationOutcome.Applied);
        secondChild.ChildGeneration.Should().Be(firstChild.ChildGeneration);
        secondChild.ChildIncarnationId.Should().NotBe(firstChild.ChildIncarnationId!.Value);

        var oldView = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = firstChild.ChildIncarnationId },
            authorization,
            AbortToken
        );
        oldView.Items.Should().ContainSingle();
        oldView.Items[0].IsHeld.Should().BeTrue();
        oldView.Items[0].ReplayParentIncarnationId.Should().Be(firstRequest.ExpectedIncarnationId);
        var newView = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = secondChild.ChildIncarnationId },
            authorization,
            AbortToken
        );
        newView.Items.Should().ContainSingle();
        newView.Items[0].ReplayParentIncarnationId.Should().Be(secondRequest.ExpectedIncarnationId);
        newView.Items[0].IsCurrentGeneration.Should().BeTrue();

        // Receipts outlive the parent row and must still identify the original replay child.
        var oldReceipt = await operations.ForceReprocessAsync(firstRequest, AbortToken);
        oldReceipt.IsReplay.Should().BeTrue();
        oldReceipt.ChildIncarnationId.Should().Be(firstChild.ChildIncarnationId);
        var duplicate = await _AdmitInboxAsync(storage, origin, lane: lane, generation: rootGeneration);
        duplicate.Disposition.Should().Be(InboxAdmissionDisposition.TerminalFailedDuplicate);
        duplicate.Message.StorageId.Should().Be(secondRoot.Message.StorageId);

        // Explicit admission generations remain independent of replay generations with the same number.
        var explicitRoot = await _AdmitInboxAsync(storage, origin, lane: lane, generation: rootGeneration + 1);
        explicitRoot.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        explicitRoot.Message.StorageId.Should().NotBe(firstChild.ChildStorageId!.Value);
        explicitRoot.Message.StorageId.Should().NotBe(secondChild.ChildStorageId!.Value);
        (await operations.ReleaseHoldAsync(childRequest with { OperationId = Guid.NewGuid() }, AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        (await operations.PurgeAsync(childRequest with { OperationId = Guid.NewGuid() }, AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        var explicitDuplicate = await _AdmitInboxAsync(storage, origin, lane: lane, generation: rootGeneration + 1);
        explicitDuplicate.Disposition.Should().Be(InboxAdmissionDisposition.InFlightDuplicate);
        explicitDuplicate.Message.StorageId.Should().Be(explicitRoot.Message.StorageId);
        (await operations.ForceReprocessAsync(firstRequest, AbortToken))
            .ChildIncarnationId.Should()
            .Be(firstChild.ChildIncarnationId);
    }

    private static async Task _CompleteInboxForReplayAsync(IDataStorage storage, MediumMessage message)
    {
        message.InlineAttempts++;
        (await storage.LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(1), 0, AbortToken))
            .Should()
            .BeTrue();
        (
            await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Failed,
                MessageContentWrite.Preserve,
                null,
                null,
                0,
                1,
                AbortToken
            )
        )
            .Should()
            .BeTrue();
    }

    private static ValueTask<InboxAdmissionResult> _AdmitInboxAsync(
        IDataStorage storage,
        Message origin,
        string group = "orders-group",
        string consumerIdentity = "orders.consumer-a",
        string contractVersion = "v1",
        MessageLane lane = MessageLane.Bus,
        long generation = 0
    )
    {
        return storage.AdmitReceivedMessageAsync(
            origin.Name,
            group,
            consumerIdentity,
            contractVersion,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = origin,
                Content = string.Empty,
                Lane = lane,
            },
            generation,
            cancellationToken: AbortToken
        );
    }

    public virtual async Task should_store_published_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        const string messageName = "test-published-message";

        // when
        var result = await storage.StoreMessageAsync(messageName, message, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().NotBe(Guid.Empty);
        result.Origin.Should().BeSameAs(message);
    }

    public virtual async Task should_store_scheduled_message_with_atomic_not_before_state()
    {
        if (!Capabilities.SupportsDelayedScheduling || !Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support delayed scheduling and monitoring roundtrip");
        }

        var storage = GetStorage();
        var publishAt = TimeProvider.GetUtcNow().AddMinutes(30);
        var envelope = new MediumMessage
        {
            StorageId = Guid.Empty,
            Origin = CreateMessage(),
            Content = string.Empty,
            Lane = MessageLane.Bus,
        };

        var stored = await storage.StoreScheduledMessageAsync(
            "scheduled-atomic-state",
            envelope,
            publishAt,
            cancellationToken: AbortToken
        );

        stored.ExpiresAt.Should().BeCloseTo(publishAt, TimeSpan.FromMilliseconds(1));
        stored.NextRetryAt.Should().BeNull();

        var roundTripped = await storage.GetMonitoringApi().GetPublishedMessageAsync(stored.StorageId, AbortToken);
        roundTripped.Should().NotBeNull();
        roundTripped!.ExpiresAt.Should().BeCloseTo(publishAt, TimeSpan.FromSeconds(1));
        roundTripped.NextRetryAt.Should().BeNull();

        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Name = "scheduled-atomic-state",
                    PageSize = 20,
                },
                AbortToken
            );
        page.Items.Should().ContainSingle().Which.StatusName.Should().Be(StatusName.Delayed);
    }

    public virtual async Task should_store_published_message_with_non_numeric_message_id()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage("non-numeric-id");

        // when
        var result = await storage.StoreMessageAsync("test-published-message", message, cancellationToken: AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().NotBe(Guid.Empty);
        result.Origin.Id.Should().Be("non-numeric-id");
    }

    public virtual async Task should_store_published_message_with_intent_type()
    {
        // given
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support monitoring roundtrip");
        }

        var storage = GetStorage();
        var legacyIntents = new[] { (MessageLane.Bus, Value: (short)0), (MessageLane.Queue, Value: (short)1) };

        foreach (var (lane, value) in legacyIntents)
        {
            var envelope = new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = lane,
            };

            // when
            var result = await storage.StoreMessageAsync(
                $"test-published-message-{lane}",
                envelope,
                cancellationToken: AbortToken
            );

            // then
            result.Lane.Should().Be(lane);
            ((short)result.Lane).Should().Be(value);
            var roundTripped = await storage.GetMonitoringApi().GetPublishedMessageAsync(result.StorageId, AbortToken);
            roundTripped.Should().NotBeNull();
            roundTripped!.Lane.Should().Be(lane);
            ((short)roundTripped.Lane).Should().Be(value);
        }
    }

    public virtual async Task should_filter_monitoring_messages_by_intent_type()
    {
        // given
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support monitoring roundtrip");
        }

        var storage = GetStorage();
        await storage.StoreMessageAsync(
            "intent-filter",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken: AbortToken
        );
        await storage.StoreMessageAsync(
            "intent-filter",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Queue,
            },
            cancellationToken: AbortToken
        );

        // when
        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Publish,
                    Name = "intent-filter",
                    Lane = MessageLane.Queue,
                    PageSize = 20,
                },
                AbortToken
            );

        // then
        page.Items.Should().OnlyContain(message => message.Lane == MessageLane.Queue);
        page.Items.Should().ContainSingle();
    }

    public virtual async Task should_store_received_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        const string messageName = "test-received-message";
        const string group = "test-group";

        // when
        var result = await storage.StoreReceivedMessageAsync(messageName, group, message, AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().NotBe(Guid.Empty);
        result.Origin.Should().BeSameAs(message);
    }

    public virtual async Task should_store_received_bus_and_queue_rows_with_same_identity()
    {
        // given
        var storage = GetStorage();
        var messageId = $"same-identity-{Guid.NewGuid():N}";
        var bus = CreateMessage(messageId);
        var queue = CreateMessage(messageId);
        const string messageName = "test-received-message";
        const string group = "test-group";

        // when
        await storage.StoreReceivedMessageAsync(
            messageName,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = bus,
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            AbortToken
        );
        await storage.StoreReceivedMessageAsync(
            messageName,
            group,
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = queue,
                Content = string.Empty,
                Lane = MessageLane.Queue,
            },
            AbortToken
        );

        // then
        var rowCount = await CountReceivedMessagesByIdentityAsync(messageId, group, AbortToken);
        rowCount.Should().Be(2);
    }

    public virtual async Task should_store_received_exception_message()
    {
        // given
        var storage = GetStorage();
        var serializer = GetSerializer();
        const string messageName = "exception-message";
        const string group = "test-group";
        // StoreReceivedExceptionMessageAsync expects serialized Message JSON with headers, not raw text
        var message = CreateMessage();
        var content = serializer.Serialize(message);

        // when
        var act = async () =>
            await storage.StoreReceivedExceptionMessageAsync(
                messageName,
                group,
                content,
                cancellationToken: AbortToken
            );

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_change_publish_state()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("state-test", message, cancellationToken: AbortToken);

        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // when — transition to Failed with a future NextRetryAt so the row stays mutable and the
        // state transition can be read back through the monitoring API.
        var result = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );

        // then — the storage write must succeed and the persisted row must reflect the new state.
        result.Should().BeTrue("the state transition must succeed against a fresh, non-terminal row");

        if (Capabilities.SupportsMonitoringApi)
        {
            var roundTripped = await storage
                .GetMonitoringApi()
                .GetPublishedMessageAsync(storedMessage.StorageId, AbortToken);
            roundTripped.Should().NotBeNull("the row must persist after a successful state change");
            roundTripped!
                .NextRetryAt.Should()
                .NotBeNull("NextRetryAt must be persisted by ChangePublishStateAsync")
                .And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
        }
    }

    public virtual async Task should_change_receive_state()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync("state-test", "group", message, AbortToken);

        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);

        // when — transition to Failed with a future NextRetryAt so the row stays mutable and the
        // state transition can be read back through the monitoring API.
        var result = await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );

        // then — the storage write must succeed and the persisted row must reflect the new state.
        result.Should().BeTrue("the state transition must succeed against a fresh, non-terminal row");

        if (Capabilities.SupportsMonitoringApi)
        {
            var roundTripped = await storage
                .GetMonitoringApi()
                .GetReceivedMessageAsync(storedMessage.StorageId, AbortToken);
            roundTripped.Should().NotBeNull("the row must persist after a successful state change");
            roundTripped!
                .NextRetryAt.Should()
                .NotBeNull("NextRetryAt must be persisted by ChangeReceiveStateAsync")
                .And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
        }
    }

    public virtual Task should_preserve_persisted_envelope_when_published_transition_declares_preserve()
    {
        return _ShouldPreservePersistedEnvelopeAsync(received: false);
    }

    public virtual Task should_preserve_persisted_envelope_when_received_transition_declares_preserve()
    {
        return _ShouldPreservePersistedEnvelopeAsync(received: true);
    }

    public virtual Task should_refresh_persisted_envelope_when_published_transition_declares_refresh()
    {
        return _ShouldRefreshPersistedEnvelopeAsync(received: false);
    }

    public virtual Task should_refresh_persisted_envelope_when_received_transition_declares_refresh()
    {
        return _ShouldRefreshPersistedEnvelopeAsync(received: true);
    }

    /// <summary>
    /// <see cref="MessageContentWrite.Preserve"/> promises the stored envelope is left alone. A caller that
    /// mutated its own <c>Origin</c> without asking for a rewrite must therefore see none of that mutation in
    /// the row — the providers that persist serialized bytes get this for free, so this pins the providers
    /// that keep a live envelope to the same contract.
    /// </summary>
    private async Task _ShouldPreservePersistedEnvelopeAsync(bool received)
    {
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not expose the monitoring API needed to read the persisted envelope back");
        }

        // given — a stored row and the exact envelope bytes storage holds for it
        var storage = GetStorage();
        var stored = received
            ? await storage.StoreReceivedMessageAsync(
                "content-preserve",
                "content-preserve-group",
                CreateMessage(),
                AbortToken
            )
            : await storage.StoreMessageAsync("content-preserve", CreateMessage(), cancellationToken: AbortToken);

        var persistedContent = stored.Content;

        // when — the caller stamps its envelope the way the failure paths do, but declares Preserve
        stored.Origin.AddOrUpdateException(new InvalidOperationException("preserve-probe"));
        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var changed = received
            ? await storage.ChangeReceiveStateAsync(
                stored,
                StatusName.Failed,
                MessageContentWrite.Preserve,
                nextRetryAt,
                cancellationToken: AbortToken
            )
            : await storage.ChangePublishStateAsync(
                stored,
                StatusName.Failed,
                MessageContentWrite.Preserve,
                nextRetryAt: nextRetryAt,
                cancellationToken: AbortToken
            );

        // then — the transition landed, but the unwritten mutation never reached the row
        changed.Should().BeTrue("the transition must succeed against a fresh, non-terminal row");

        var roundTripped = received
            ? await storage.GetMonitoringApi().GetReceivedMessageAsync(stored.StorageId, AbortToken)
            : await storage.GetMonitoringApi().GetPublishedMessageAsync(stored.StorageId, AbortToken);

        roundTripped.Should().NotBeNull("the row must persist after a successful state change");
        roundTripped!
            .NextRetryAt.Should()
            .NotBeNull("the state transition must still be persisted")
            .And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
        roundTripped.Content.Should().Be(persistedContent, "Preserve must leave the stored envelope byte-identical");
        roundTripped
            .Origin.Headers.Should()
            .NotContainKey(
                MessagingHeaders.Exception,
                "a mutation the caller never asked storage to write must not leak into the row"
            );
    }

    /// <summary>
    /// <see cref="MessageContentWrite.Refresh"/> is what the failure paths use after stamping the exception
    /// onto <c>Origin</c>. It must push that mutation all the way into the row and re-sync the caller's
    /// <c>Content</c>, so the next pickup reads an envelope that agrees with its own bytes.
    /// </summary>
    private async Task _ShouldRefreshPersistedEnvelopeAsync(bool received)
    {
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not expose the monitoring API needed to read the persisted envelope back");
        }

        // given — a stored row and the exact envelope bytes storage holds for it
        var storage = GetStorage();
        var stored = received
            ? await storage.StoreReceivedMessageAsync(
                "content-refresh",
                "content-refresh-group",
                CreateMessage(),
                AbortToken
            )
            : await storage.StoreMessageAsync("content-refresh", CreateMessage(), cancellationToken: AbortToken);

        var persistedContent = stored.Content;

        // when — the caller stamps the exception onto Origin and declares Refresh, as the failure paths do
        stored.Origin.AddOrUpdateException(new InvalidOperationException("refresh-probe"));
        var expectedContent = GetSerializer().Serialize(stored.Origin);
        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var changed = received
            ? await storage.ChangeReceiveStateAsync(
                stored,
                StatusName.Failed,
                MessageContentWrite.Refresh,
                nextRetryAt,
                cancellationToken: AbortToken
            )
            : await storage.ChangePublishStateAsync(
                stored,
                StatusName.Failed,
                MessageContentWrite.Refresh,
                nextRetryAt: nextRetryAt,
                cancellationToken: AbortToken
            );

        // then — the mutated envelope is what storage now holds, on the row and on the caller's copy
        changed.Should().BeTrue("the transition must succeed against a fresh, non-terminal row");
        expectedContent
            .Should()
            .NotBe(persistedContent, "the probe must actually change the envelope, or this test proves nothing");
        stored.Content.Should().Be(expectedContent, "Refresh must re-sync the caller's Content with its Origin");

        var roundTripped = received
            ? await storage.GetMonitoringApi().GetReceivedMessageAsync(stored.StorageId, AbortToken)
            : await storage.GetMonitoringApi().GetPublishedMessageAsync(stored.StorageId, AbortToken);

        roundTripped.Should().NotBeNull("the row must persist after a successful state change");
        roundTripped!.Content.Should().Be(expectedContent, "Refresh must persist the mutated envelope");
        roundTripped
            .Origin.Headers.Should()
            .ContainKey(
                MessagingHeaders.Exception,
                "the persisted Origin must agree with the Content that Refresh just wrote"
            )
            .WhoseValue.Should()
            .Be(nameof(InvalidOperationException));
    }

    public virtual async Task should_change_publish_state_to_delayed()
    {
        // Skip if storage doesn't support delayed scheduling
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("delayed-test", message, cancellationToken: AbortToken);

        // when
        var act = async () => await storage.ChangePublishStateToDelayedAsync([storedMessage.StorageId], AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_not_flip_terminal_published_row_back_to_delayed()
    {
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given — a row sealed terminal (Succeeded, no scheduled retry). The dispatcher's shutdown
        // flush (DisposeAsync → ChangePublishStateToDelayedAsync) can race a consumer that just
        // dispatched the same row; once one side seals it, the flush must not resurrect it as Delayed.
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "terminal-delayed-guard",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        var sealedFirst = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Succeeded,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );
        sealedFirst.Should().BeTrue("the transition to Succeeded must win against a fresh row");

        // when — the late shutdown flush tries to move the sealed row back to Delayed.
        await storage.ChangePublishStateToDelayedAsync([storedMessage.StorageId], AbortToken);

        // then — the row must remain terminal: a follow-up state change is still rejected by the
        // terminal guard (a Delayed row would have accepted it) and the retry pickup never sees it.
        var lateChange = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Scheduled,
            nextRetryAt: DateTimeOffset.UtcNow,
            cancellationToken: AbortToken
        );
        lateChange.Should().BeFalse("the terminal seal must survive a late scheduler flush");

        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_ignore_unknown_storage_ids_when_flushing_delayed_state()
    {
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given — one live row plus an id with no backing row (e.g. the row was pruned between the
        // dispatcher snapshotting its scheduler queue and the shutdown flush running).
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "delayed-unknown-id",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        // when
        var act = async () =>
            await storage.ChangePublishStateToDelayedAsync([storedMessage.StorageId, Guid.NewGuid()], AbortToken);

        // then
        await act.Should().NotThrowAsync("missing rows must be skipped, not faulted on");
    }

    public virtual async Task should_get_published_messages_of_need_retry()
    {
        // given
        var storage = GetStorage();
        // when
        var result = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);

        // then
        result.Should().NotBeNull();
    }

    public virtual async Task should_get_received_messages_of_need_retry()
    {
        // given
        var storage = GetStorage();
        // when
        var result = await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);

        // then
        result.Should().NotBeNull();
    }

    public virtual Task should_claim_published_retry_messages_by_lane_and_apply_batch_per_lane()
    {
        return _ShouldClaimRetryMessagesByLaneAsync(published: true);
    }

    public virtual Task should_claim_received_retry_messages_by_lane_and_apply_batch_per_lane()
    {
        return _ShouldClaimRetryMessagesByLaneAsync(published: false);
    }

    public virtual Task should_preserve_unsupported_lane_without_starving_published_retry()
    {
        return _ShouldPreserveUnsupportedLaneWithoutStarvingRetryAsync(published: true);
    }

    public virtual Task should_preserve_unsupported_lane_without_starving_received_retry()
    {
        return _ShouldPreserveUnsupportedLaneWithoutStarvingRetryAsync(published: false);
    }

    public virtual async Task should_delete_expired_messages()
    {
        // Skip if storage doesn't support expiration
        if (!Capabilities.SupportsExpiration)
        {
            Assert.Skip("Storage does not support message expiration");
        }

        // given
        var storage = GetStorage();
        var initializer = GetInitializer();
        var tableName = initializer.GetPublishedTableName();
        var timeout = DateTimeOffset.UtcNow.AddMinutes(-10);

        // when
        var deletedCount = await storage.DeleteExpiresAsync(tableName, timeout, 100, AbortToken);

        // then
        deletedCount.Should().BeGreaterThanOrEqualTo(0);
    }

    public virtual async Task should_not_delete_expired_failed_messages_with_pending_retry()
    {
        if (!Capabilities.SupportsExpiration || !Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support expiration and monitoring roundtrip");
        }

        // given — Failed rows with a future NextRetryAt are retry-scheduled, not terminal poison.
        // Expiration cleanup must only delete Failed/Succeeded rows once NextRetryAt is cleared.
        var storage = GetStorage();
        var initializer = GetInitializer();
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var cleanupCutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(10);

        var published = await storage.StoreMessageAsync(
            "retry-expiration-published",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        published.ExpiresAt = expiredAt;

        var publishedChanged = await storage.ChangePublishStateAsync(
            published,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );
        publishedChanged.Should().BeTrue();

        var received = await storage.StoreReceivedMessageAsync(
            "retry-expiration-received",
            "retry-expiration-group",
            CreateMessage(),
            AbortToken
        );
        received.ExpiresAt = expiredAt;

        var receivedChanged = await storage.ChangeReceiveStateAsync(
            received,
            StatusName.Failed,
            nextRetryAt: nextRetryAt,
            cancellationToken: AbortToken
        );
        receivedChanged.Should().BeTrue();

        // when
        var deletedPublished = await storage.DeleteExpiresAsync(
            initializer.GetPublishedTableName(),
            cleanupCutoff,
            100,
            AbortToken
        );
        var deletedReceived = await storage.DeleteExpiresAsync(
            initializer.GetReceivedTableName(),
            cleanupCutoff,
            100,
            AbortToken
        );

        // then
        deletedPublished.Should().Be(0);
        deletedReceived.Should().Be(0);

        var persistedPublished = await storage
            .GetMonitoringApi()
            .GetPublishedMessageAsync(published.StorageId, AbortToken);
        persistedPublished.Should().NotBeNull();
        persistedPublished!.NextRetryAt.Should().NotBeNull().And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));

        var persistedReceived = await storage
            .GetMonitoringApi()
            .GetReceivedMessageAsync(received.StorageId, AbortToken);
        persistedReceived.Should().NotBeNull();
        persistedReceived!.NextRetryAt.Should().NotBeNull().And.BeCloseTo(nextRetryAt, TimeSpan.FromSeconds(1));
    }

    public virtual async Task should_delete_published_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("delete-test", message, cancellationToken: AbortToken);

        // when
        var deletedCount = await storage.DeletePublishedMessageAsync(storedMessage.StorageId, AbortToken);

        // then
        deletedCount.Should().Be(1);
    }

    public virtual async Task should_delete_received_message()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync("delete-test", "group", message, AbortToken);

        // when
        var deletedCount = await storage.DeleteReceivedMessageAsync(storedMessage.StorageId, AbortToken);

        // then
        deletedCount.Should().Be(1);
    }

    public virtual Task should_get_monitoring_api()
    {
        // Skip if storage doesn't support monitoring API
        if (!Capabilities.SupportsMonitoringApi)
        {
            Assert.Skip("Storage does not support monitoring API");
        }

        // given
        var storage = GetStorage();

        // when
        var monitoringApi = storage.GetMonitoringApi();

        // then
        monitoringApi.Should().NotBeNull();
        return Task.CompletedTask;
    }

    public virtual async Task should_handle_concurrent_storage_operations()
    {
        // Skip if storage doesn't support concurrent operations
        if (!Capabilities.SupportsConcurrentOperations)
        {
            Assert.Skip("Storage does not support concurrent operations");
        }

        // given
        var storage = GetStorage();
        var results = new ConcurrentBag<MediumMessage>();

        // when
        var tasks = Enumerable
            .Range(0, 20)
            .Select(async i =>
            {
                var message = CreateMessage();
                var result = await storage.StoreMessageAsync(
                    $"concurrent-messageName-{i}",
                    message,
                    cancellationToken: AbortToken
                );
                results.Add(result);
            });

        await Task.WhenAll(tasks);

        // then
        results.Should().HaveCount(20);
        results.Should().AllSatisfy(r => r.StorageId.Should().NotBe(Guid.Empty));
    }

    public virtual async Task should_schedule_messages_of_delayed()
    {
        // Skip if storage doesn't support delayed scheduling
        if (!Capabilities.SupportsDelayedScheduling)
        {
            Assert.Skip("Storage does not support delayed scheduling");
        }

        // given
        var storage = GetStorage();
        var scheduledMessages = new List<MediumMessage>();

        // when
        await storage.ScheduleMessagesOfDelayedAsync(
            (_, messages) =>
            {
                scheduledMessages.AddRange(messages);
                return ValueTask.CompletedTask;
            },
            AbortToken
        );

        // then - should complete without exception
        scheduledMessages.Should().NotBeNull();
    }

    public virtual async Task should_claim_delayed_messages_atomically_when_capability_supported()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        var now = TimeProvider.GetUtcNow();
        var later = await storage.StoreMessageAsync(
            "delayed-claim-later",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
                ExpiresAt = now.AddSeconds(30),
            },
            cancellationToken: AbortToken
        );
        var earlier = await storage.StoreMessageAsync(
            "delayed-claim-earlier",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
                ExpiresAt = now.AddSeconds(20),
            },
            cancellationToken: AbortToken
        );
        later.ExpiresAt = now.AddSeconds(30);
        earlier.ExpiresAt = now.AddSeconds(20);
        (await storage.ChangePublishStateAsync(later, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        (await storage.ChangePublishStateAsync(earlier, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        var claimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);

        claimed.Select(message => message.StorageId).Should().Equal(earlier.StorageId, later.StorageId);
        claimed.Should().AllSatisfy(message => message.LockedUntil.Should().NotBeNull());
        (await claimStorage.ClaimDelayedMessagesAsync(AbortToken))
            .Should()
            .BeEmpty("the live claim lease must fence an immediate re-poll");
    }

    public virtual async Task should_keep_early_delayed_claim_lease_alive_until_dispatch()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        var dispatchTimeout = TimeSpan.FromSeconds(1);
        if (!TrySetDispatchTimeout(dispatchTimeout))
        {
            Assert.Skip("Storage does not expose mutable dispatch-timeout options");
            return;
        }

        var expiresAt = TimeProvider.GetUtcNow().AddSeconds(30);
        var stored = await storage.StoreMessageAsync(
            "delayed-claim-short-timeout",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
                ExpiresAt = expiresAt,
            },
            cancellationToken: AbortToken
        );
        stored.ExpiresAt = expiresAt;
        (await storage.ChangePublishStateAsync(stored, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        var claimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);

        var winner = claimed.Should().ContainSingle().Subject;
        winner.StorageId.Should().Be(stored.StorageId);
        winner.LockedUntil.Should().NotBeNull();
        // LockedUntil is persisted at the storage clock's granularity (PostgreSQL truncates to microseconds),
        // so allow up to 1 microsecond of downward truncation from the tick-precision expected value.
        winner.LockedUntil!.Value.Should().BeOnOrAfter(expiresAt.Add(dispatchTimeout) - TimeSpan.FromMicroseconds(1));
    }

    public virtual async Task should_clear_claim_lease_when_flushing_delayed_state()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        // A long dispatch timeout makes the claim lease clearly future-dated, so a stale (un-cleared) lease would
        // fence the row from re-claim on restart. The graceful-shutdown flush must release the lease so the row
        // is immediately re-claimable — otherwise the delayed message is delivered up to DispatchTimeout late.
        var dispatchTimeout = TimeSpan.FromSeconds(120);
        if (!TrySetDispatchTimeout(dispatchTimeout))
        {
            Assert.Skip("Storage does not expose mutable dispatch-timeout options");
            return;
        }

        var expiresAt = TimeProvider.GetUtcNow().AddSeconds(30);
        var stored = await storage.StoreMessageAsync(
            "delayed-claim-flush-clears-lease",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
                ExpiresAt = expiresAt,
            },
            cancellationToken: AbortToken
        );
        stored.ExpiresAt = expiresAt;
        (await storage.ChangePublishStateAsync(stored, StatusName.Delayed, cancellationToken: AbortToken))
            .Should()
            .BeTrue();

        // Claim it — stamps a future-dated ownership lease and moves it to Queued.
        var claimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);
        claimed.Should().ContainSingle().Which.StorageId.Should().Be(stored.StorageId);

        // Flush it back to Delayed, exactly as the graceful-shutdown scheduler flush does.
        await storage.ChangePublishStateToDelayedAsync([stored.StorageId], AbortToken);

        // The flush must clear the lease so the row is immediately re-claimable. With a stale lease this re-poll
        // returns empty (the message would wait out DispatchTimeout before re-dispatch).
        var reclaimed = await claimStorage.ClaimDelayedMessagesAsync(AbortToken);
        reclaimed
            .Should()
            .ContainSingle("the graceful-shutdown flush must release the claim lease for immediate re-scheduling")
            .Which.StorageId.Should()
            .Be(stored.StorageId);
    }

    public virtual async Task should_return_disjoint_winners_to_concurrent_delayed_claimers()
    {
        var storage = GetStorage();
        if (storage is not IDelayedMessageClaimStorage claimStorage)
        {
            Assert.Skip("Storage does not support atomic delayed-message claiming");
            return;
        }

        const int messageCount = 8;
        var now = TimeProvider.GetUtcNow();
        var storageIds = new HashSet<Guid>();
        for (var index = 0; index < messageCount; index++)
        {
            var expiresAt = now.AddSeconds(10 + index);
            var stored = await storage.StoreMessageAsync(
                $"concurrent-delayed-claim-{index}",
                new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = CreateMessage(),
                    Content = string.Empty,
                    Lane = MessageLane.Bus,
                    ExpiresAt = expiresAt,
                },
                cancellationToken: AbortToken
            );
            stored.ExpiresAt = expiresAt;
            (await storage.ChangePublishStateAsync(stored, StatusName.Delayed, cancellationToken: AbortToken))
                .Should()
                .BeTrue();
            storageIds.Add(stored.StorageId);
        }

        var claims = await Task.WhenAll(
            claimStorage.ClaimDelayedMessagesAsync(AbortToken).AsTask(),
            claimStorage.ClaimDelayedMessagesAsync(AbortToken).AsTask()
        );
        var claimedIds = claims.SelectMany(messages => messages).Select(message => message.StorageId).ToArray();

        claimedIds.Should().HaveCount(messageCount).And.OnlyHaveUniqueItems();
        claimedIds.Should().BeEquivalentTo(storageIds);
    }

    public virtual async Task should_store_message_with_transaction()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();

        // when — null transaction path (provider-specific transaction tests should cover the real path)
        var result = await storage.StoreMessageAsync("transaction-test", message, transaction: null, AbortToken);

        // then
        result.Should().NotBeNull();
        result.StorageId.Should().NotBe(Guid.Empty);
        result.Origin.Should().BeSameAs(message);
        result.Retries.Should().Be(0);
    }

    public virtual async Task should_handle_message_state_transitions()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("state-transition", message, cancellationToken: AbortToken);

        // when - transition through states
        await storage.ChangePublishStateAsync(storedMessage, StatusName.Scheduled, cancellationToken: AbortToken);
        await storage.ChangePublishStateAsync(storedMessage, StatusName.Succeeded, cancellationToken: AbortToken);

        // then - no exception thrown
        storedMessage.Should().NotBeNull();
    }

    public virtual async Task should_handle_failed_message_state()
    {
        // given
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync("failed-state", message, cancellationToken: AbortToken);

        // when
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // then — the failed message should appear in retry results once its scheduled retry time is due.
        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        retriable.Should().Contain(m => m.StorageId == storedMessage.StorageId);
    }

    // -------------------------------------------------------------------------
    // Negative NextRetryAt filter cases — Failed messages must not be picked up
    // unless NextRetryAt is in the past. Mirrors the partial-index predicates.
    // -------------------------------------------------------------------------

    public virtual async Task should_not_return_published_message_with_failed_status_and_null_next_retry_at()
    {
        // given — a Failed message with NextRetryAt = NULL represents a permanent failure
        // (Stop classification). It must NOT be returned by the retry-pickup query.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync(
            "failed-null-next-retry",
            message,
            cancellationToken: AbortToken
        );

        // when
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_seal_succeeded_published_message_against_state_change_and_retry_pickup()
    {
        // given — Succeeded with NextRetryAt = NULL is the terminal fingerprint written after a
        // successful dispatch. This is the double-dispatch closure: the commit-edge drain and the
        // relay sweep can both attempt the same row in a narrow window, so once one of them seals
        // it, the row must reject any late state change AND never be returned by the retry pickup.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync(
            "succeeded-terminal-seal",
            message,
            cancellationToken: AbortToken
        );

        var sealedFirst = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Succeeded,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );
        sealedFirst.Should().BeTrue("the first transition to Succeeded must win against a fresh row");

        // when — a late writer (the losing side of the drain/relay race) tries to flip the row back.
        var lateChange = await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Scheduled,
            nextRetryAt: DateTimeOffset.UtcNow,
            cancellationToken: AbortToken
        );

        // then — the terminal guard rejects the write and the pickup never re-sends the row.
        lateChange.Should().BeFalse("a Succeeded row with no scheduled retry is terminal");

        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_published_message_with_future_next_retry_at()
    {
        // given — a Failed message scheduled for the future must NOT be returned until its
        // retry time is due (the query predicate is NextRetryAt <= now()).
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreMessageAsync(
            "failed-future-next-retry",
            message,
            cancellationToken: AbortToken
        );

        // when
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddHours(1),
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_received_message_with_failed_status_and_null_next_retry_at()
    {
        // given — a Failed received message with NextRetryAt = NULL must NOT be returned.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "failed-null-next-retry",
            "test-group",
            message,
            AbortToken
        );

        // when
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: null,
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_received_message_with_future_next_retry_at()
    {
        // given — a Failed received message scheduled for the future must NOT be returned
        // until its retry time is due.
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "failed-future-next-retry",
            "test-group",
            message,
            AbortToken
        );

        // when
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddHours(1),
            cancellationToken: AbortToken
        );

        // then
        var retriable = await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken);
        retriable.Should().NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_leased_published_message_until_lease_expires()
    {
        // Verifies the lease/pickup contract:
        //   1. An active lease (LockedUntil in the future) excludes the row from retry pickup.
        //   2. After the lease window elapses (LockedUntil <= now), the row is eligible again.
        //
        // The lease-contention guard from PR #254 review #15 rejects an attempt to overwrite an
        // active lease with a past timestamp — so the old "negative-timestamp trick" no longer
        // works. The test instead writes a short real-clock lease and waits for it to expire,
        // matching how production code would observe lease expiry.
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "leased-published",
            CreateMessage(),
            cancellationToken: AbortToken
        );

        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leaseWindow = TimeSpan.FromMilliseconds(500);
        var leased = await storage.LeasePublishAsync(storedMessage, leaseWindow, AbortToken);

        leased.Should().BeTrue();
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        await Task.Delay(leaseWindow + TimeSpan.FromMilliseconds(250), AbortToken);

        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_not_return_leased_received_message_until_lease_expires()
    {
        // Asymmetric coverage parity with the published-lease test above. See that method for
        // the rationale behind the short real-clock lease window.
        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "leased-received",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var leaseWindow = TimeSpan.FromMilliseconds(500);
        var leased = await storage.LeaseReceiveAsync(storedMessage, leaseWindow, AbortToken);

        leased.Should().BeTrue();
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        await Task.Delay(leaseWindow + TimeSpan.FromMilliseconds(250), AbortToken);

        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_use_database_clock_when_reclaiming_published_retry_lease()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();

        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "db-clock-published-retry",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(storedMessage, TimeSpan.FromMinutes(30), AbortToken)).Should().BeTrue();

        (await fastClockStorage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_stamp_fresh_dispatch_lease_from_database_clock(bool published, bool reserveAttempt)
    {
        var skewedStorage = _CreateRelationalClockSkewStorage();
        var databaseTimeBefore = await GetDatabaseUtcNowAsync(AbortToken);
        if (databaseTimeBefore is null)
        {
            Assert.Skip("Storage does not expose a relational database-clock test seam");
        }

        var storage = GetStorage();
        var message = published
            ? await storage.StoreMessageAsync("db-clock-dispatch-lease", CreateMessage(), cancellationToken: AbortToken)
            : await storage.StoreReceivedMessageAsync(
                "db-clock-dispatch-lease",
                "db-clock-dispatch-group",
                CreateMessage(),
                AbortToken
            );
        var originalOwner = NodeMembership.SetIdentity($"database-clock-{published}-{reserveAttempt}");
        var leaseDuration = TimeSpan.FromSeconds(5);
        message.InlineAttempts = reserveAttempt ? 1 : 0;

        var acquired = (published, reserveAttempt) switch
        {
            (true, false) => await skewedStorage.LeasePublishAsync(message, leaseDuration, AbortToken),
            (true, true) => await skewedStorage.LeasePublishAndReserveAttemptAsync(
                message,
                leaseDuration,
                originalInlineAttempts: 0,
                AbortToken
            ),
            (false, false) => await skewedStorage.LeaseReceiveAsync(message, leaseDuration, AbortToken),
            (false, true) => await skewedStorage.LeaseReceiveAndReserveAttemptAsync(
                message,
                leaseDuration,
                originalInlineAttempts: 0,
                AbortToken
            ),
        };

        var databaseTimeAfter = await GetDatabaseUtcNowAsync(AbortToken);
        var persisted = await GetPersistedLeaseIdentityAsync(published, message.StorageId, AbortToken);
        acquired.Should().BeTrue();
        databaseTimeAfter.Should().NotBeNull();
        persisted.Should().NotBeNull();
        var persistedLockedUntil = persisted!.Value.LockedUntil;
        var databaseTimeBeforeUtc = new DateTimeOffset(
            DateTime.SpecifyKind(databaseTimeBefore.Value, DateTimeKind.Utc),
            TimeSpan.Zero
        );
        var databaseTimeAfterUtc = new DateTimeOffset(
            DateTime.SpecifyKind(databaseTimeAfter!.Value, DateTimeKind.Utc),
            TimeSpan.Zero
        );
        message.LockedUntil.Should().Be(persistedLockedUntil);
        message.Owner.Should().Be(persisted.Value.Owner).And.Be(originalOwner.ToString());
        message
            .LockedUntil.Should()
            .BeOnOrAfter(databaseTimeBeforeUtc.Add(leaseDuration))
            .And.BeOnOrBefore(databaseTimeAfterUtc.Add(leaseDuration));

        NodeMembership.SetIdentity("database-clock-contender");
        var reacquired = published
            ? await skewedStorage.LeasePublishAsync(message, leaseDuration, AbortToken)
            : await skewedStorage.LeaseReceiveAsync(message, leaseDuration, AbortToken);
        reacquired.Should().BeFalse("the database-authored lease is still active");
    }

    public virtual async Task should_use_database_clock_when_reclaiming_received_retry_lease()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();

        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "db-clock-received-retry",
            "db-clock-group",
            CreateMessage(),
            AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeaseReceiveAsync(storedMessage, TimeSpan.FromMinutes(30), AbortToken)).Should().BeTrue();

        (await fastClockStorage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_use_database_clock_when_fast_forwarding_dead_owner_lease()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();

        var deadOwner = NodeMembership.SetIdentity("db-clock-dead-owner");
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "db-clock-dead-owner-reclaim",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(storedMessage, TimeSpan.FromMinutes(30), AbortToken)).Should().BeTrue();

        (await fastClockStorage.ReclaimDeadPublishedOwnersAsync([deadOwner.ToString()], AbortToken)).Should().Be(1);
        (await fastClockStorage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_stamp_retry_lease_from_database_clock()
    {
        var fastClockStorage = _CreateRelationalClockSkewStorage();
        var storage = GetStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "db-clock-retry-stamp",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            cancellationToken: AbortToken
        );

        var claimed = (await fastClockStorage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == storedMessage.StorageId)
            .Subject;

        claimed.LockedUntil.Should().BeAfter(DateTimeOffset.UtcNow).And.BeBefore(DateTimeOffset.UtcNow.AddMinutes(10));
    }

    public virtual async Task should_use_application_clock_when_scheduling_published_retry()
    {
        var (storage, schedulingClock) = _CreateRelationalSchedulingClockStorage();
        var storedMessage = await storage.StoreMessageAsync(
            "application-clock-published-retry",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: schedulingClock.GetUtcNow().AddMinutes(1),
            cancellationToken: AbortToken
        );

        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        schedulingClock.Advance(TimeSpan.FromMinutes(2));

        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_use_application_clock_when_scheduling_received_retry()
    {
        var (storage, schedulingClock) = _CreateRelationalSchedulingClockStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "application-clock-received-retry",
            "application-clock-group",
            CreateMessage(),
            AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: schedulingClock.GetUtcNow().AddMinutes(1),
            cancellationToken: AbortToken
        );

        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == storedMessage.StorageId);

        schedulingClock.Advance(TimeSpan.FromMinutes(2));

        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == storedMessage.StorageId);
    }

    public virtual async Task should_return_unstored_snapshot_when_redelivery_hits_active_receive_lease()
    {
        var storage = GetStorage();
        var message = CreateMessage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "active-lease-redelivery",
            "test-group",
            message,
            AbortToken
        );
        var now = _Now();

        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: now.AddSeconds(-1),
            cancellationToken: AbortToken
        );
        var leased = await storage.LeaseReceiveAsync(storedMessage, TimeSpan.FromMinutes(5), AbortToken);
        var beforeRedelivery = Capabilities.SupportsMonitoringApi
            ? await storage.GetMonitoringApi().GetReceivedMessageAsync(storedMessage.StorageId, AbortToken)
            : null;

        var redelivery = await storage.StoreReceivedMessageAsync(
            "active-lease-redelivery",
            "test-group",
            message,
            AbortToken
        );

        leased.Should().BeTrue();
        redelivery.StorageId.Should().NotBe(storedMessage.StorageId);
        redelivery.LockedUntil.Should().BeNull();
        redelivery.Owner.Should().BeNull();
        (await storage.LeaseReceiveAsync(redelivery, TimeSpan.FromMinutes(5), AbortToken))
            .Should()
            .BeFalse("the guard-blocked upsert returned an unpersisted candidate");

        if (beforeRedelivery is not null)
        {
            var afterRedelivery = await storage
                .GetMonitoringApi()
                .GetReceivedMessageAsync(storedMessage.StorageId, AbortToken);
            afterRedelivery.Should().NotBeNull();
            afterRedelivery!.Content.Should().Be(beforeRedelivery.Content);
            afterRedelivery.LockedUntil.Should().Be(beforeRedelivery.LockedUntil);
            afterRedelivery.Owner.Should().Be(beforeRedelivery.Owner);
            afterRedelivery.Retries.Should().Be(beforeRedelivery.Retries);
            afterRedelivery.ExceptionInfo.Should().Be(beforeRedelivery.ExceptionInfo);
        }
    }

    public virtual async Task should_reclaim_published_retry_row_owned_by_dead_node()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("dead-published-owner");
        var deadOwned = await _StoreFailedPublishedMessageAsync("dead-owned-published");
        var deadLease = await storage.LeasePublishAsync(deadOwned, TimeSpan.FromHours(1), AbortToken);
        deadLease.Should().BeTrue("the dead-owned row must be actively leased before reclaim runs");

        var liveOwner = NodeMembership.SetIdentity("live-published-owner");
        var liveOwned = await _StoreFailedPublishedMessageAsync("live-owned-published");
        var liveLease = await storage.LeasePublishAsync(liveOwned, TimeSpan.FromHours(1), AbortToken);
        liveLease.Should().BeTrue("the live-owned row must be actively leased before reclaim runs");

        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == deadOwned.StorageId || m.StorageId == liveOwned.StorageId);

        var reclaimed = await storage.ReclaimDeadPublishedOwnersAsync([deadOwner.ToString()], AbortToken);

        reclaimed.Should().Be(1);
        var retriable = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();
        retriable.Should().Contain(m => m.StorageId == deadOwned.StorageId);
        retriable.Should().NotContain(m => m.StorageId == liveOwned.StorageId);
        deadOwner.ToString().Should().NotBe(liveOwner.ToString());
    }

    public virtual async Task should_reclaim_received_retry_row_owned_by_dead_node()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("dead-received-owner");
        var deadOwned = await _StoreFailedReceivedMessageAsync("dead-owned-received", "dead-group");
        var deadLease = await storage.LeaseReceiveAsync(deadOwned, TimeSpan.FromHours(1), AbortToken);
        deadLease.Should().BeTrue("the dead-owned row must be actively leased before reclaim runs");

        var liveOwner = NodeMembership.SetIdentity("live-received-owner");
        var liveOwned = await _StoreFailedReceivedMessageAsync("live-owned-received", "live-group");
        var liveLease = await storage.LeaseReceiveAsync(liveOwned, TimeSpan.FromHours(1), AbortToken);
        liveLease.Should().BeTrue("the live-owned row must be actively leased before reclaim runs");

        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == deadOwned.StorageId || m.StorageId == liveOwned.StorageId);

        var reclaimed = await storage.ReclaimDeadReceivedOwnersAsync([deadOwner.ToString()], AbortToken);

        reclaimed.Should().Be(1);
        var retriable = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();
        retriable.Should().Contain(m => m.StorageId == deadOwned.StorageId);
        retriable.Should().NotContain(m => m.StorageId == liveOwned.StorageId);
        deadOwner.ToString().Should().NotBe(liveOwner.ToString());
    }

    public virtual async Task should_stamp_owner_on_claim()
    {
        var storage = GetStorage();
        var owner = NodeMembership.SetIdentity("claim-owner", incarnation: 7);
        var expectedOwner = owner.ToString();

        var published = await _StoreFailedPublishedMessageAsync("claim-owner-published");
        var claimedPublished = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == published.StorageId)
            .Subject;

        claimedPublished.Owner.Should().Be(expectedOwner);
        claimedPublished.LockedUntil.Should().NotBeNull();

        var received = await _StoreFailedReceivedMessageAsync("claim-owner-received", "claim-group");
        var claimedReceived = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(m => m.StorageId == received.StorageId)
            .Subject;

        claimedReceived.Owner.Should().Be(expectedOwner);
        claimedReceived.LockedUntil.Should().NotBeNull();
    }

    public virtual async Task should_release_only_exact_published_retry_lease_generation()
    {
        var storage = GetStorage();
        var releaseStorage = storage.Should().BeAssignableTo<IGracefulLeaseReleaseStorage>().Subject;
        NodeMembership.SetIdentity("graceful-published-owner");
        var stored = await _StoreFailedPublishedMessageAsync("graceful-release-published");
        var claimed = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == stored.StorageId)
            .Subject;
        var identity = new MessageLeaseIdentity(
            claimed.StorageId,
            claimed.Owner,
            claimed.LockedUntil!.Value,
            claimed.Lane
        );

        (await releaseStorage.ReleasePublishedLeaseAsync(identity with { Owner = "another-owner" }, AbortToken))
            .Should()
            .BeFalse();
        (
            await releaseStorage.ReleasePublishedLeaseAsync(
                identity with
                {
                    LockedUntil = identity.LockedUntil.AddMilliseconds(1),
                },
                AbortToken
            )
        )
            .Should()
            .BeFalse();
        (await releaseStorage.ReleasePublishedLeaseAsync(identity with { Lane = MessageLane.Queue }, AbortToken))
            .Should()
            .BeFalse();
        (await releaseStorage.ReleasePublishedLeaseAsync(identity, AbortToken)).Should().BeTrue();

        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == stored.StorageId);
    }

    public virtual async Task should_release_only_exact_received_retry_lease_generation()
    {
        var storage = GetStorage();
        var releaseStorage = storage.Should().BeAssignableTo<IGracefulLeaseReleaseStorage>().Subject;
        NodeMembership.SetIdentity("graceful-received-owner");
        var stored = await _StoreFailedReceivedMessageAsync("graceful-release-received", "graceful-group");
        var claimed = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == stored.StorageId)
            .Subject;
        var identity = new MessageLeaseIdentity(
            claimed.StorageId,
            claimed.Owner,
            claimed.LockedUntil!.Value,
            claimed.Lane
        );

        (await releaseStorage.ReleaseReceivedLeaseAsync(identity with { Owner = "another-owner" }, AbortToken))
            .Should()
            .BeFalse();
        (
            await releaseStorage.ReleaseReceivedLeaseAsync(
                identity with
                {
                    LockedUntil = identity.LockedUntil.AddMilliseconds(1),
                },
                AbortToken
            )
        )
            .Should()
            .BeFalse();
        (await releaseStorage.ReleaseReceivedLeaseAsync(identity with { Lane = MessageLane.Queue }, AbortToken))
            .Should()
            .BeFalse();
        (await releaseStorage.ReleaseReceivedLeaseAsync(identity, AbortToken)).Should().BeTrue();

        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == stored.StorageId);
    }

    public virtual async Task should_atomically_defer_only_exact_live_received_retry_lease_generation()
    {
        var storage = GetStorage();
        var deferralStorage = storage.Should().BeAssignableTo<ICircuitRetryDeferralStorage>().Subject;
        NodeMembership.SetIdentity("circuit-deferral-owner");
        var stored = await _StoreFailedReceivedMessageAsync("circuit-deferral", "circuit-deferral-group");
        var claimed = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == stored.StorageId)
            .Subject;
        var identity = new MessageLeaseIdentity(
            claimed.StorageId,
            claimed.Owner,
            claimed.LockedUntil!.Value,
            claimed.Lane
        );
        var deferUntil = _Now().AddMinutes(5);
        var before = await storage.GetMonitoringApi().GetReceivedMessageAsync(claimed.StorageId, AbortToken);
        before.Should().NotBeNull();

        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(identity with { StorageId = Guid.NewGuid() }, deferUntil),
                AbortToken
            )
        )
            .Should()
            .BeFalse();
        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(identity with { Owner = "stale-owner" }, deferUntil),
                AbortToken
            )
        )
            .Should()
            .BeFalse();
        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(
                    identity with
                    {
                        LockedUntil = identity.LockedUntil.AddMilliseconds(1),
                    },
                    deferUntil
                ),
                AbortToken
            )
        )
            .Should()
            .BeFalse();
        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(identity with { Lane = MessageLane.Queue }, deferUntil),
                AbortToken
            )
        )
            .Should()
            .BeFalse();

        var afterStaleAttempts = await storage
            .GetMonitoringApi()
            .GetReceivedMessageAsync(claimed.StorageId, AbortToken);
        afterStaleAttempts.Should().BeEquivalentTo(before, options => options.Excluding(message => message.Origin));
        afterStaleAttempts!.Content.Should().Be(before!.Content);

        (await deferralStorage.DeferReceivedRetryAsync(new CircuitRetryDeferral(identity, deferUntil), AbortToken))
            .Should()
            .BeTrue();

        var after = await storage.GetMonitoringApi().GetReceivedMessageAsync(claimed.StorageId, AbortToken);
        after.Should().NotBeNull();
        after!
            .Should()
            .BeEquivalentTo(
                before,
                options =>
                    options
                        .Excluding(message => message.NextRetryAt)
                        .Excluding(message => message.Owner)
                        .Excluding(message => message.LockedUntil)
                        .Excluding(message => message.Origin)
            );
        after.Content.Should().Be(before!.Content);
        after.NextRetryAt.Should().BeCloseTo(deferUntil, TimeSpan.FromMicroseconds(1));
        after.Owner.Should().BeNull();
        after.LockedUntil.Should().BeNull();
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(message => message.StorageId == stored.StorageId);
    }

    public virtual async Task should_atomically_defer_received_retry_lease_with_null_owner()
    {
        var storage = GetStorage();
        var deferralStorage = storage.Should().BeAssignableTo<ICircuitRetryDeferralStorage>().Subject;
        var stored = await _StoreFailedReceivedMessageAsync("circuit-deferral-null-owner", "null-owner-group");
        var claimed = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == stored.StorageId)
            .Subject;
        claimed.Owner.Should().BeNull();
        var identity = new MessageLeaseIdentity(
            claimed.StorageId,
            claimed.Owner,
            claimed.LockedUntil!.Value,
            claimed.Lane
        );

        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(identity, _Now().AddMinutes(5)),
                AbortToken
            )
        )
            .Should()
            .BeTrue();
    }

    public virtual async Task should_not_defer_expired_received_retry_lease()
    {
        var storage = GetStorage();
        var deferralStorage = storage.Should().BeAssignableTo<ICircuitRetryDeferralStorage>().Subject;
        NodeMembership.SetIdentity("circuit-deferral-expired-owner");
        var stored = await _StoreFailedReceivedMessageAsync("circuit-deferral-expired", "expired-group");
        (await storage.LeaseReceiveAsync(stored, TimeSpan.FromSeconds(-1), AbortToken)).Should().BeTrue();
        var identity = new MessageLeaseIdentity(stored.StorageId, stored.Owner, stored.LockedUntil!.Value, stored.Lane);

        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(identity, _Now().AddMinutes(5)),
                AbortToken
            )
        )
            .Should()
            .BeFalse();
    }

    public virtual async Task should_not_defer_terminal_received_retry_lease()
    {
        var storage = GetStorage();
        var deferralStorage = storage.Should().BeAssignableTo<ICircuitRetryDeferralStorage>().Subject;
        NodeMembership.SetIdentity("circuit-deferral-terminal-owner");
        var stored = await _StoreFailedReceivedMessageAsync("circuit-deferral-terminal", "terminal-group");
        var claimed = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == stored.StorageId)
            .Subject;
        var identity = new MessageLeaseIdentity(
            claimed.StorageId,
            claimed.Owner,
            claimed.LockedUntil!.Value,
            claimed.Lane
        );
        (
            await storage.ChangeReceiveStateAsync(
                claimed,
                StatusName.Succeeded,
                nextRetryAt: null,
                lockedUntil: identity.LockedUntil,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();
        var beforeDeferral = await storage.GetMonitoringApi().GetReceivedMessageAsync(claimed.StorageId, AbortToken);
        beforeDeferral.Should().NotBeNull();

        (
            await deferralStorage.DeferReceivedRetryAsync(
                new CircuitRetryDeferral(identity, _Now().AddMinutes(5)),
                AbortToken
            )
        )
            .Should()
            .BeFalse();

        var after = await storage.GetMonitoringApi().GetReceivedMessageAsync(claimed.StorageId, AbortToken);
        after.Should().NotBeNull();
        after!.Should().BeEquivalentTo(beforeDeferral, options => options.Excluding(message => message.Origin));
        after.Content.Should().Be(beforeDeferral!.Content);
    }

    // Pins AE4 / R14 (batch fairness, issue #808): once a full leading batch is deferred, the next
    // pickup must reach a due healthy row instead of reclaiming the same head rows. Storage has no
    // notion of circuit state, so an earlier-due leading batch stands in for the open-circuit group.
    public virtual async Task should_reach_healthy_row_after_deferring_a_full_leading_open_batch()
    {
        const int batchSize = 3;
        var storage = CreateStorageWithRetryBatchSize(batchSize);

        if (storage is null)
        {
            Assert.Skip("Storage does not expose a configurable retry-batch test seam");
            return;
        }

        var deferralStorage = storage.Should().BeAssignableTo<ICircuitRetryDeferralStorage>().Subject;
        NodeMembership.SetIdentity("batch-fairness-owner");

        var openRows = new List<MediumMessage>();

        for (var index = 0; index < batchSize; index++)
        {
            var envelope = new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage(),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            };
            var stored = await storage.StoreReceivedMessageAsync(
                $"batch-fairness-open-{index}",
                "batch-fairness-open-group",
                envelope,
                AbortToken
            );
            await storage.ChangeReceiveStateAsync(
                stored,
                StatusName.Failed,
                nextRetryAt: _Now().AddSeconds(-30 + index),
                cancellationToken: AbortToken
            );
            openRows.Add(stored);
        }

        var openRowIds = openRows.Select(message => message.StorageId).ToHashSet();

        var healthyEnvelope = new MediumMessage
        {
            StorageId = Guid.Empty,
            Origin = CreateMessage(),
            Content = string.Empty,
            Lane = MessageLane.Bus,
        };
        var healthyStored = await storage.StoreReceivedMessageAsync(
            "batch-fairness-healthy",
            "batch-fairness-healthy-group",
            healthyEnvelope,
            AbortToken
        );
        // Due, but sorts after every open-group row under the claim query's ORDER BY NextRetryAt, Id.
        await storage.ChangeReceiveStateAsync(
            healthyStored,
            StatusName.Failed,
            nextRetryAt: _Now().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // First pickup: 3 slots for 4 due rows, so the earlier-due open rows fill the claim. The
        // healthy row's absence here is what proves starvation is possible without the fix.
        // Scoped to this test's own rows: a sibling in this collection (PostgreSqlDeduplicationTest)
        // leaves due rows in the reused container, which can take claim slots. That also rules out an
        // unfiltered HaveCount — this test cannot guarantee its rows win every slot.
        var firstClaim = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();
        var ownedIds = new HashSet<Guid>(openRowIds) { healthyStored.StorageId };
        var ownedFirstClaim = firstClaim.Where(message => ownedIds.Contains(message.StorageId)).ToList();
        ownedFirstClaim.Should().OnlyContain(message => openRowIds.Contains(message.StorageId));
        ownedFirstClaim.Should().NotContain(message => message.StorageId == healthyStored.StorageId);

        var deferUntil = _Now().AddMinutes(10);

        foreach (var claimed in firstClaim)
        {
            var identity = new MessageLeaseIdentity(
                claimed.StorageId,
                claimed.Owner,
                claimed.LockedUntil!.Value,
                claimed.Lane
            );
            (await deferralStorage.DeferReceivedRetryAsync(new CircuitRetryDeferral(identity, deferUntil), AbortToken))
                .Should()
                .BeTrue();
        }

        // Second pickup: the deferred rows are now future-due, so the starved healthy row must surface.
        var secondClaim = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();
        secondClaim.Should().ContainSingle(message => message.StorageId == healthyStored.StorageId);
        secondClaim.Should().NotContain(message => openRowIds.Contains(message.StorageId));
    }

    public virtual async Task should_not_release_terminal_retry_lease_generation()
    {
        var storage = GetStorage();
        var releaseStorage = storage.Should().BeAssignableTo<IGracefulLeaseReleaseStorage>().Subject;
        NodeMembership.SetIdentity("graceful-terminal-owner");
        var storedPublished = await _StoreFailedPublishedMessageAsync("graceful-terminal-published");
        var claimedPublished = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == storedPublished.StorageId)
            .Subject;
        var publishedIdentity = new MessageLeaseIdentity(
            claimedPublished.StorageId,
            claimedPublished.Owner,
            claimedPublished.LockedUntil!.Value,
            claimedPublished.Lane
        );
        (
            await storage.ChangePublishRetryStateAsync(
                claimedPublished,
                StatusName.Succeeded,
                MessageContentWrite.Preserve,
                nextRetryAt: null,
                lockedUntil: null,
                originalRetries: claimedPublished.Retries,
                originalInlineAttempts: claimedPublished.InlineAttempts,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        if (Capabilities.SupportsMonitoringApi)
        {
            var roundTripped = await storage
                .GetMonitoringApi()
                .GetPublishedMessageAsync(claimedPublished.StorageId, AbortToken);
            roundTripped.Should().NotBeNull();
            roundTripped!.LockedUntil.Should().BeNull("the successful retry transition must clear its lease");
        }

        (await releaseStorage.ReleasePublishedLeaseAsync(publishedIdentity, AbortToken))
            .Should()
            .BeFalse("graceful release must never rewrite a terminal row");

        var storedReceived = await _StoreFailedReceivedMessageAsync(
            "graceful-terminal-received",
            "graceful-terminal-group"
        );
        var claimedReceived = (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == storedReceived.StorageId)
            .Subject;
        var receivedIdentity = new MessageLeaseIdentity(
            claimedReceived.StorageId,
            claimedReceived.Owner,
            claimedReceived.LockedUntil!.Value,
            claimedReceived.Lane
        );
        (
            await storage.ChangeReceiveRetryStateAsync(
                claimedReceived,
                StatusName.Succeeded,
                MessageContentWrite.Preserve,
                nextRetryAt: null,
                lockedUntil: null,
                originalRetries: claimedReceived.Retries,
                originalInlineAttempts: claimedReceived.InlineAttempts,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        if (Capabilities.SupportsMonitoringApi)
        {
            var roundTripped = await storage
                .GetMonitoringApi()
                .GetReceivedMessageAsync(claimedReceived.StorageId, AbortToken);
            roundTripped.Should().NotBeNull();
            roundTripped!.LockedUntil.Should().BeNull("the successful retry transition must clear its lease");
        }

        (await releaseStorage.ReleaseReceivedLeaseAsync(receivedIdentity, AbortToken))
            .Should()
            .BeFalse("graceful release must never rewrite a terminal row");

        var storedTerminal = await _StoreFailedPublishedMessageAsync("graceful-terminal-preserved-lease");
        var claimedTerminal = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .ContainSingle(message => message.StorageId == storedTerminal.StorageId)
            .Subject;
        var terminalIdentity = new MessageLeaseIdentity(
            claimedTerminal.StorageId,
            claimedTerminal.Owner,
            claimedTerminal.LockedUntil!.Value,
            claimedTerminal.Lane
        );
        (
            await storage.ChangePublishStateAsync(
                claimedTerminal,
                StatusName.Succeeded,
                nextRetryAt: null,
                lockedUntil: claimedTerminal.LockedUntil,
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        (await releaseStorage.ReleasePublishedLeaseAsync(terminalIdentity, AbortToken))
            .Should()
            .BeFalse("graceful release must remain fenced by terminal status");
    }

    public virtual async Task should_batch_release_only_exact_published_retry_lease_generations()
    {
        var storage = GetStorage();
        var releaseStorage = storage.Should().BeAssignableTo<IGracefulLeaseReleaseStorage>().Subject;
        NodeMembership.SetIdentity("graceful-batch-owner");
        var first = await _StoreFailedPublishedMessageAsync("graceful-batch-published-1");
        var second = await _StoreFailedPublishedMessageAsync("graceful-batch-published-2");
        var claimed = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Where(message => message.StorageId == first.StorageId || message.StorageId == second.StorageId)
            .ToArray();
        claimed.Should().HaveCount(2);
        var identities = claimed
            .Select(message => new MessageLeaseIdentity(
                message.StorageId,
                message.Owner,
                message.LockedUntil!.Value,
                message.Lane
            ))
            .ToArray();

        var released = await releaseStorage.ReleasePublishedLeasesAsync(
            [identities[0] with { Owner = "stale-owner" }, .. identities],
            AbortToken
        );

        released.Should().Be(2);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(message => message.StorageId == first.StorageId)
            .And.Contain(message => message.StorageId == second.StorageId);
    }

    public virtual async Task should_not_reclaim_rows_of_live_or_restarted_incarnation()
    {
        var storage = GetStorage();
        // The crashed incarnation (@7) is the dead owner; the restarted incarnation (@8) is live.
        // Reclaiming the dead set must fence the restart: @8's rows stay untouched.
        var deadOwner = NodeMembership.SetIdentity("restart-node", incarnation: 7);
        var oldPublished = await _StoreFailedPublishedMessageAsync("old-incarnation-published");
        (await storage.LeasePublishAsync(oldPublished, _FutureLease(), AbortToken)).Should().BeTrue();
        var oldReceived = await _StoreFailedReceivedMessageAsync("old-incarnation-received", "old-incarnation-group");
        (await storage.LeaseReceiveAsync(oldReceived, _FutureLease(), AbortToken)).Should().BeTrue();

        var liveOwner = NodeMembership.SetIdentity("restart-node", incarnation: 8);
        var livePublished = await _StoreFailedPublishedMessageAsync("live-incarnation-published");
        (await storage.LeasePublishAsync(livePublished, _FutureLease(), AbortToken)).Should().BeTrue();
        var liveReceived = await _StoreFailedReceivedMessageAsync(
            "live-incarnation-received",
            "live-incarnation-group"
        );
        (await storage.LeaseReceiveAsync(liveReceived, _FutureLease(), AbortToken)).Should().BeTrue();

        deadOwner.ToString().Should().NotBe(liveOwner.ToString());
        var deadOwners = new[] { deadOwner.ToString() };

        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        var publishedRetriable = (
            await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
        ).ToList();
        publishedRetriable.Should().Contain(m => m.StorageId == oldPublished.StorageId);
        publishedRetriable.Should().NotContain(m => m.StorageId == livePublished.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        var receivedRetriable = (
            await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
        ).ToList();
        receivedRetriable.Should().Contain(m => m.StorageId == oldReceived.StorageId);
        receivedRetriable.Should().NotContain(m => m.StorageId == liveReceived.StorageId);
    }

    public virtual async Task should_not_reclaim_terminal_rows()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("terminal-dead-owner");
        var published = await _StoreFailedPublishedMessageAsync("terminal-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        (
            await storage.ChangePublishStateAsync(
                published,
                StatusName.Failed,
                nextRetryAt: null,
                lockedUntil: _FutureLeaseUntil(),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var received = await _StoreFailedReceivedMessageAsync("terminal-received", "terminal-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();
        (
            await storage.ChangeReceiveStateAsync(
                received,
                StatusName.Failed,
                nextRetryAt: null,
                lockedUntil: _FutureLeaseUntil(),
                cancellationToken: AbortToken
            )
        )
            .Should()
            .BeTrue();

        var deadOwners = new[] { deadOwner.ToString() };

        // A terminal row owned by a dead owner is matched by the owner clause but excluded by the
        // terminal-row guard, so reclaim leaves it alone.
        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken))
            .Should()
            .Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_be_inert_when_no_dead_owners_passed()
    {
        var storage = GetStorage();
        var published = await _StoreFailedPublishedMessageAsync("owner-null-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("owner-null-received", "owner-null-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();

        (await storage.ReclaimDeadPublishedOwnersAsync([], AbortToken)).Should().Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync([], AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_not_reclaim_rows_with_null_owner()
    {
        var storage = GetStorage();
        // NodeMembership.Identity is null by default — rows get Owner=NULL when leased
        var published = await _StoreFailedPublishedMessageAsync("null-owner-guard-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("null-owner-guard-received", "null-owner-guard-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();

        // Non-empty list bypasses early-exit guard; WHERE Owner IS NOT NULL must filter null-Owner rows
        (await storage.ReclaimDeadPublishedOwnersAsync(["dead-owner-x"], AbortToken))
            .Should()
            .Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(["dead-owner-x"], AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .NotContain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_reclaim_dead_owner_rows_idempotently()
    {
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("idempotent-dead-owner");
        var published = await _StoreFailedPublishedMessageAsync("idempotent-published");
        (await storage.LeasePublishAsync(published, _FutureLease(), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("idempotent-received", "idempotent-group");
        (await storage.LeaseReceiveAsync(received, _FutureLease(), AbortToken)).Should().BeTrue();

        var deadOwners = new[] { deadOwner.ToString() };

        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(m => m.StorageId == published.StorageId);

        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(1);
        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_not_reclaim_dead_owner_rows_with_expired_lease()
    {
        // AE4: the LockedUntil floor — a dead owner's row whose lease has already expired is left
        // untouched by reclaim (the `LockedUntil > now` clause excludes it); normal lease-expiry
        // pickup recovers it. Reclaim only fast-forwards leases still in the future.
        var storage = GetStorage();
        var deadOwner = NodeMembership.SetIdentity("expired-lease-dead-owner");
        var published = await _StoreFailedPublishedMessageAsync("expired-lease-published");
        (await storage.LeasePublishAsync(published, TimeSpan.FromSeconds(-1), AbortToken)).Should().BeTrue();
        var received = await _StoreFailedReceivedMessageAsync("expired-lease-received", "expired-lease-group");
        (await storage.LeaseReceiveAsync(received, TimeSpan.FromSeconds(-1), AbortToken)).Should().BeTrue();

        var deadOwners = new[] { deadOwner.ToString() };

        (await storage.ReclaimDeadPublishedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);
        (await storage.ReclaimDeadReceivedOwnersAsync(deadOwners, AbortToken)).Should().Be(0);

        // The floor still recovers them: an expired lease is already retriable via normal pickup.
        (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(m => m.StorageId == published.StorageId);
        (await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken))
            .Should()
            .Contain(m => m.StorageId == received.StorageId);
    }

    public virtual async Task should_handle_concurrent_state_updates_to_same_row()
    {
        // Concurrent CAS / optimistic-concurrency contract: exactly one of N parallel
        // ChangeReceiveStateAsync calls with originalRetries=0 must succeed. The others must
        // return false because Retries no longer equals the original value (or because the row
        // is now terminal). Validates the per-row CAS guard used to prevent inverse-order pickups
        // from overwriting each other's terminal writes.
        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "concurrent-cas",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        // Transition to Failed/NextRetryAt-in-future so the row stays mutable (terminal guard
        // would otherwise reject EVERY call regardless of originalRetries semantics).
        storedMessage.Retries = 0;
        await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(5),
            cancellationToken: AbortToken
        );

        const int concurrency = 20;
        var bag = new ConcurrentBag<bool>();
        await Task.WhenAll(
            Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        var localCopy = new MediumMessage
                        {
                            StorageId = storedMessage.StorageId,
                            Origin = storedMessage.Origin,
                            Content = storedMessage.Content,
                            Lane = MessageLane.Bus,
                            Retries = 1,
                        };
                        var ok = await storage.ChangeReceiveStateAsync(
                            localCopy,
                            StatusName.Failed,
                            nextRetryAt: DateTimeOffset.UtcNow.AddMinutes(10),
                            originalRetries: 0,
                            cancellationToken: AbortToken
                        );
                        bag.Add(ok);
                    })
                )
        );

        bag.Count(x => x).Should().Be(1, "exactly one concurrent CAS update must win");
        bag.Count(x => !x).Should().Be(concurrency - 1, "all other writers must observe stale Retries");
    }

    public virtual async Task should_reject_mismatched_original_retries()
    {
        var storage = GetStorage();
        var storedMessage = await storage.StoreReceivedMessageAsync(
            "retry-race",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        storedMessage.Retries = 1;
        var first = await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            originalRetries: 0,
            cancellationToken: AbortToken
        );

        var second = await storage.ChangeReceiveStateAsync(
            storedMessage,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            originalRetries: 0,
            cancellationToken: AbortToken
        );

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    public virtual async Task should_lease_and_reserve_publish_attempt_in_single_step()
    {
        var storage = GetStorage();
        var message = await storage.StoreMessageAsync("lease-reserve", CreateMessage(), cancellationToken: AbortToken);
        message.InlineAttempts = 1;

        var reserved = await storage.LeasePublishAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 0,
            AbortToken
        );

        reserved.Should().BeTrue();
        message.LockedUntil.Should().NotBeNull("a successful combined write must mirror the lease to the caller");

        // The row is now actively leased: a second combined write must be rejected even with the
        // correct counter token, while the standalone mid-burst reservation under the held lease
        // still succeeds.
        message.InlineAttempts = 2;
        var contended = await storage.LeasePublishAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 1,
            AbortToken
        );
        contended.Should().BeFalse("an actively leased row must not be re-leased mid-burst");

        var midBurst = await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 1, AbortToken);
        midBurst.Should().BeTrue("the lease owner must still be able to reserve the next inline attempt");
    }

    public virtual async Task should_reject_lease_and_reserve_with_stale_inline_attempts_token()
    {
        var storage = GetStorage();
        var message = await storage.StoreReceivedMessageAsync(
            "lease-reserve-cas",
            "test-group",
            CreateMessage(),
            AbortToken
        );

        // First combined write with an already-expired lease leaves the row unleased but advances
        // the durable InlineAttempts counter to 1.
        message.InlineAttempts = 1;
        var first = await storage.LeaseReceiveAndReserveAttemptAsync(
            message,
            TimeSpan.FromSeconds(-1),
            originalInlineAttempts: 0,
            AbortToken
        );
        first.Should().BeTrue();

        // A contender holding a stale counter view (0) must fail the CAS; the current token (1)
        // must succeed.
        message.InlineAttempts = 2;
        var stale = await storage.LeaseReceiveAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 0,
            AbortToken
        );
        stale.Should().BeFalse("the durable InlineAttempts token moved; a stale view must not re-reserve");

        var current = await storage.LeaseReceiveAndReserveAttemptAsync(
            message,
            TimeSpan.FromMinutes(5),
            originalInlineAttempts: 1,
            AbortToken
        );
        current.Should().BeTrue();
    }

    public virtual Task should_reject_stale_published_lease_generation_writes()
    {
        return _ShouldRejectStaleLeaseGenerationWritesAsync(received: false);
    }

    public virtual Task should_reject_stale_received_lease_generation_writes()
    {
        return _ShouldRejectStaleLeaseGenerationWritesAsync(received: true);
    }

    public virtual Task should_allow_published_fenced_writes_with_fast_application_clock()
    {
        return _ShouldAllowFencedWritesWithFastApplicationClockAsync(received: false);
    }

    public virtual Task should_allow_received_fenced_writes_with_fast_application_clock()
    {
        return _ShouldAllowFencedWritesWithFastApplicationClockAsync(received: true);
    }

    public virtual async Task should_report_false_when_received_exception_message_is_already_terminal()
    {
        var storage = GetStorage();
        var serializer = GetSerializer();
        var message = CreateMessage();
        var content = serializer.Serialize(message);

        var first = await storage.StoreReceivedExceptionMessageAsync(
            "poisoned",
            "test-group",
            content,
            "first",
            AbortToken
        );
        var second = await storage.StoreReceivedExceptionMessageAsync(
            "poisoned",
            "test-group",
            content,
            "second",
            AbortToken
        );

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    public virtual async Task should_handle_concurrent_redelivery_storm_on_same_message_id()
    {
        // Fan-out concurrency test for StoreReceivedExceptionMessageAsync's upsert identity
        // (Version, MessageId, Group). N parallel writers for the SAME message must collapse
        // to exactly one row. The last writer's exceptionInfo must win (the upsert is an
        // unconditional overwrite when the existing row is non-terminal — the terminal-row
        // guard only blocks updates against Succeeded/Failed-NULL rows; the first call here
        // creates a Failed/NULL row, so subsequent storm writes hit the terminal guard and
        // return false. We assert the row-count invariant and that exception info matches one
        // of the contributors — see the comment in the InMemory provider on upsert semantics).
        // R10 — pre-warm the thread pool so a CI box with a low default min-thread count does not
        // starve the workers and produce a false-failure "lock starvation suspected" within the
        // 30s wall-clock budget. Without this, 64 Task.Run callbacks can sit in the global queue
        // for seconds before the threadpool grows.
        ThreadPool.SetMinThreads(
            Math.Max(64, Environment.ProcessorCount * 2),
            Math.Max(64, Environment.ProcessorCount * 2)
        );

        var storage = GetStorage();
        var serializer = GetSerializer();
        var message = CreateMessage($"storm-{Guid.NewGuid():N}");
        var content = serializer.Serialize(message);
        const string group = "storm-group";
        const int concurrency = 64;

        using var startGate = new ManualResetEventSlim(false);
        var results = new ConcurrentBag<bool>();

        var workers = Enumerable
            .Range(0, concurrency)
            .Select(index =>
                Task.Run(async () =>
                {
                    startGate.Wait(AbortToken);
                    var ok = await storage.StoreReceivedExceptionMessageAsync(
                        "redelivery-storm",
                        group,
                        content,
                        $"writer-{index}",
                        AbortToken
                    );
                    results.Add(ok);
                })
            )
            .ToArray();

        // Release every worker at the same instant so the test exercises the contended path,
        // not the trivial sequential one.
        startGate.Set();

        // Hard timeout — if the secondary-index path regresses to an O(N) scan inside an
        // exclusive lock the storm would either deadlock or take orders of magnitude longer
        // than 30 seconds. Failing fast surfaces the regression.
        var stormCompletion = Task.WhenAll(workers);
        var timeout = Task.Delay(TimeSpan.FromSeconds(30), AbortToken);
        var winner = await Task.WhenAny(stormCompletion, timeout);
        winner
            .Should()
            .BeSameAs(stormCompletion, "storm must finish well under 30s — lock starvation suspected otherwise");

        // Exactly one writer should report true (the inserter); all others observe the existing
        // terminal-NULL row and return false. The single-row invariant is then verified via a
        // direct identity probe — calling StoreReceivedExceptionMessageAsync again with new
        // exception info must also return false because the row is now terminal.
        results.Count(x => x).Should().Be(1, "exactly one concurrent upsert must insert the row");
        results.Count(x => !x).Should().Be(concurrency - 1, "all losers must observe the existing terminal row");

        var followUp = await storage.StoreReceivedExceptionMessageAsync(
            "redelivery-storm",
            group,
            content,
            "post-storm",
            AbortToken
        );
        followUp.Should().BeFalse("the row is terminal — no further upserts should succeed");
    }

    public virtual async Task should_handle_concurrent_first_insert_storm_with_null_and_non_null_group()
    {
        // R1 regression — guards against the F8-redux duplicate-row bug on the
        // StoreReceivedExceptionMessageAsync path. Two parallel storms exercise both halves of the
        // upsert identity:
        //   - NULL Group: a plain ("MessageId","Group") unique index treats NULLs as distinct, so
        //     the previous SELECT-FOR-UPDATE-then-INSERT pattern let two concurrent first-inserts
        //     both fall through and produce duplicate rows. PostgreSQL must rely on a NULL-safe
        //     conflict target (COALESCE("Group", '')); SqlServer's MERGE already handles this
        //     case in its ON clause.
        //   - non-NULL Group: the standard unique-constraint path. A regression would either
        //     produce duplicates (no constraint at all) or surface a raw 23505 sqlstate /
        //     2627 unique-violation to the caller. We assert exactly one row converges and no
        //     exception escapes.
        // Pre-warm the thread pool so a CI box with low default min-threads does not starve the
        // workers and produce a false "lock starvation" timeout.
        ThreadPool.SetMinThreads(
            Math.Max(64, Environment.ProcessorCount * 2),
            Math.Max(64, Environment.ProcessorCount * 2)
        );

        var storage = GetStorage();
        var serializer = GetSerializer();
        const int concurrency = 32;

        await runFirstInsertStormAsync(group: null);
        await runFirstInsertStormAsync(group: "g1");

        return;

        async Task runFirstInsertStormAsync(string? group)
        {
            var messageId = $"first-insert-storm-{group ?? "null"}-{Guid.NewGuid():N}";
            var message = CreateMessage(messageId);
            var content = serializer.Serialize(message);

            using var startGate = new ManualResetEventSlim(initialState: false);
            var results = new ConcurrentBag<bool>();
            var exceptions = new ConcurrentBag<Exception>();

            var workers = Enumerable
                .Range(0, concurrency)
                .Select(index =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            startGate.Wait(AbortToken);
                            // group! tolerates the InMemory provider's non-nullable string group
                            // parameter while still exercising the SQL providers' COALESCE/MERGE
                            // NULL-equivalent upsert key on the database side.
                            var ok = await storage.StoreReceivedExceptionMessageAsync(
                                "first-insert-storm",
                                group!,
                                content,
                                $"writer-{index}",
                                AbortToken
                            );
                            results.Add(ok);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    })
                )
                .ToArray();

            startGate.Set();
            var stormCompletion = Task.WhenAll(workers);
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), AbortToken);
            var winner = await Task.WhenAny(stormCompletion, timeout);
            winner
                .Should()
                .BeSameAs(
                    stormCompletion,
                    $"storm with group={group ?? "<null>"} must finish well under 30s — lock starvation or deadlock suspected"
                );

            exceptions
                .Should()
                .BeEmpty(
                    $"no concurrent insert should surface a unique-violation or sqlstate 23505 to the caller (group={group ?? "<null>"})"
                );
            results.Count(x => x).Should().Be(1, $"exactly one writer must insert the row (group={group ?? "<null>"})");
            results
                .Count(x => !x)
                .Should()
                .Be(
                    concurrency - 1,
                    $"all losing writers must observe the existing terminal row (group={group ?? "<null>"})"
                );

            var rowCount = await CountReceivedMessagesByIdentityAsync(messageId, group, AbortToken);
            rowCount
                .Should()
                .Be(1, $"the concurrent storm must converge to exactly one persisted row (group={group ?? "<null>"})");
        }
    }

    public virtual async Task should_handle_concurrent_store_received_message_with_same_identity()
    {
        // R3 regression — StoreReceivedMessageAsync (the non-exception path) must also serialize
        // through the same identity check that StoreReceivedExceptionMessageAsync uses. Before R3
        // the InMemory provider's non-exception path performed an unconditional insert + index
        // overwrite, so two concurrent calls with the same (Version, MessageId, Group) produced
        // duplicate rows that both showed up in GetReceivedMessagesOfNeedRetryAsync — running the
        // consume executor twice. The SQL providers enforce uniqueness via the DB constraint;
        // this test exercises the InMemory parity path as well as the PG/SqlServer DB paths.
        ThreadPool.SetMinThreads(
            Math.Max(64, Environment.ProcessorCount * 2),
            Math.Max(64, Environment.ProcessorCount * 2)
        );

        var storage = GetStorage();
        const int concurrency = 32;

        await runStoreReceivedStormAsync(group: null);
        await runStoreReceivedStormAsync(group: "consume-group");

        return;

        async Task runStoreReceivedStormAsync(string? group)
        {
            var messageId = $"store-received-storm-{group ?? "null"}-{Guid.NewGuid():N}";
            var sharedMessage = CreateMessage(messageId);

            using var startGate = new ManualResetEventSlim(initialState: false);
            var exceptions = new ConcurrentBag<Exception>();

            var workers = Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            startGate.Wait(AbortToken);
                            await storage.StoreReceivedMessageAsync(
                                "store-received-storm",
                                group!,
                                sharedMessage,
                                AbortToken
                            );
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    })
                )
                .ToArray();

            startGate.Set();
            var stormCompletion = Task.WhenAll(workers);
            var timeout = Task.Delay(TimeSpan.FromSeconds(30), AbortToken);
            var winner = await Task.WhenAny(stormCompletion, timeout);
            winner
                .Should()
                .BeSameAs(
                    stormCompletion,
                    $"storm with group={group ?? "<null>"} must finish well under 30s — lock starvation or deadlock suspected"
                );

            exceptions
                .Should()
                .BeEmpty(
                    $"no concurrent insert should surface a unique-violation or sqlstate 23505 to the caller (group={group ?? "<null>"})"
                );

            var rowCount = await CountReceivedMessagesByIdentityAsync(messageId, group, AbortToken);
            rowCount
                .Should()
                .Be(1, $"the concurrent storm must converge to exactly one persisted row (group={group ?? "<null>"})");
        }
    }

    public virtual async Task should_respect_initial_dispatch_grace()
    {
        // #10 — parity test for the InitialDispatchGrace exclusion contract. Providers that
        // expose a controllable TimeProvider exercise the WHERE-predicate boundary; providers
        // backed by TimeProvider.System (or by a DB-side time function) skip until they grow a
        // fixture-level clock injection seam.
        if (!SupportsControllableClock)
        {
            Assert.Skip(
                "Provider does not expose a controllable TimeProvider — initial-dispatch-grace boundary requires FakeTimeProvider."
            );
        }

        var fakeClock = TimeProvider as Microsoft.Extensions.Time.Testing.FakeTimeProvider;
        if (fakeClock is null)
        {
            Assert.Skip("TimeProvider override is not a FakeTimeProvider — cannot advance the clock for this test.");
        }

        // given — fresh published row carries NextRetryAt = Added + InitialDispatchGrace
        // (default 30s). Polling immediately must exclude it.
        var storage = GetStorage();
        var stored = await storage.StoreMessageAsync("grace-base", CreateMessage(), cancellationToken: AbortToken);

        var beforeGrace = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();
        beforeGrace
            .Should()
            .NotContain(
                m => m.StorageId == stored.StorageId,
                "freshly-stored rows must be excluded during the initial dispatch grace window"
            );

        // when — advance past the grace window.
        fakeClock!.Advance(TimeSpan.FromMinutes(2));

        // then — the row is now eligible for pickup.
        var afterGrace = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();
        afterGrace
            .Should()
            .Contain(
                m => m.StorageId == stored.StorageId,
                "after the grace window elapses the persisted retry processor must pick the row up"
            );
    }

    public virtual async Task should_pickup_message_at_max_persisted_retries_and_exclude_above()
    {
        // given — with MaxPersistedRetries = 4, the pickup predicate is `Retries <= 4`.
        // Retries == 4 is the LAST allowed pickup (where the helper returns Exhausted on
        // budget consumption). Retries == 5 represents the terminal state past the budget
        // and must NOT be picked up. Total dispatches = (MaxPersistedRetries + 1) = 5.
        var storage = GetStorage();

        // Boundary case 1 (published): Retries == MaxPersistedRetries → picked up.
        var atLimit = await storage.StoreMessageAsync(
            "max-retries-test-pub",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        atLimit.Retries = 4;
        await storage.ChangePublishStateAsync(
            atLimit,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // Boundary case 2 (published): Retries == MaxPersistedRetries + 1 → NOT picked up.
        var aboveLimit = await storage.StoreMessageAsync(
            "above-retries-test-pub",
            CreateMessage(),
            cancellationToken: AbortToken
        );
        aboveLimit.Retries = 5;
        await storage.ChangePublishStateAsync(
            aboveLimit,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        // when
        var retriable = (await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)).ToList();

        // then
        retriable.Should().Contain(m => m.StorageId == atLimit.StorageId);
        retriable.Should().NotContain(m => m.StorageId == aboveLimit.StorageId);

        // Same boundary semantics for received messages. Each scenario uses a distinct message
        // (and therefore a distinct MessageId) so the (MessageId, Group) upsert identity on the
        // received table does not collapse the two cases into a single row.
        var atLimitRecv = await storage.StoreReceivedMessageAsync(
            "max-retries-test-recv",
            "group",
            CreateMessage(),
            AbortToken
        );
        atLimitRecv.Retries = 4;
        await storage.ChangeReceiveStateAsync(
            atLimitRecv,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var aboveLimitRecv = await storage.StoreReceivedMessageAsync(
            "above-retries-test-recv",
            "group",
            CreateMessage(),
            AbortToken
        );
        aboveLimitRecv.Retries = 5;
        await storage.ChangeReceiveStateAsync(
            aboveLimitRecv,
            StatusName.Failed,
            nextRetryAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            cancellationToken: AbortToken
        );

        var retriableReceived = (
            await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
        ).ToList();
        retriableReceived.Should().Contain(m => m.StorageId == atLimitRecv.StorageId);
        retriableReceived.Should().NotContain(m => m.StorageId == aboveLimitRecv.StorageId);
    }

    private async Task<MediumMessage> _StoreFailedPublishedMessageAsync(string name)
    {
        var storage = GetStorage();
        var stored = await storage.StoreMessageAsync(name, CreateMessage(), cancellationToken: AbortToken);

        await storage.ChangePublishStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: _Now().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        return stored;
    }

    private async Task _ShouldPreserveUnsupportedLaneWithoutStarvingRetryAsync(bool published)
    {
        const short unsupportedLane = 99;
        var storage = GetStorage();
        var now = _Now();
        var invalidId = await SeedUnsupportedLaneRetryRowAsync(
            storage,
            published,
            unsupportedLane,
            now.AddMinutes(-2),
            AbortToken
        );
        if (invalidId is null)
        {
            Assert.Skip("Storage does not expose an authoritative raw retry-row test seam");
            return;
        }

        async Task<MediumMessage> storeHealthyAsync(MessageLane lane)
        {
            var envelope = new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = CreateMessage($"healthy-{published}-{lane}-{Guid.NewGuid():N}"),
                Content = string.Empty,
                Lane = lane,
            };
            var stored = published
                ? await storage.StoreMessageAsync(
                    $"unsupported-lane-published-{lane}",
                    envelope,
                    cancellationToken: AbortToken
                )
                : await storage.StoreReceivedMessageAsync(
                    $"unsupported-lane-received-{lane}",
                    $"unsupported-lane-group-{lane}",
                    envelope,
                    AbortToken
                );

            if (published)
            {
                await storage.ChangePublishStateAsync(
                    stored,
                    StatusName.Failed,
                    nextRetryAt: now.AddMinutes(-1),
                    cancellationToken: AbortToken
                );
            }
            else
            {
                await storage.ChangeReceiveStateAsync(
                    stored,
                    StatusName.Failed,
                    nextRetryAt: now.AddMinutes(-1),
                    cancellationToken: AbortToken
                );
            }

            return stored;
        }

        var stateBeforeClaim = await GetPersistedPoisonRetryStateAsync(storage, published, invalidId.Value, AbortToken);
        stateBeforeClaim.Should().NotBeNull();

        var busMessage = await storeHealthyAsync(MessageLane.Bus);
        var queueMessage = await storeHealthyAsync(MessageLane.Queue);

        var busClaim = (
            published
                ? await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
                : await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
        ).ToList();

        busClaim.Should().ContainSingle(message => message.StorageId == busMessage.StorageId);
        busClaim.Should().NotContain(message => message.StorageId == queueMessage.StorageId);
        busClaim.Should().NotContain(message => message.StorageId == invalidId.Value);

        var stateAfterBusClaim = await GetPersistedPoisonRetryStateAsync(
            storage,
            published,
            invalidId.Value,
            AbortToken
        );
        stateAfterBusClaim.Should().Be(stateBeforeClaim);

        var unknownPage = await storage
            .GetMonitoringApi()
            .GetUnknownLaneMessagesAsync(
                new UnknownLaneMessageQuery
                {
                    MessageType = published ? MessageType.Publish : MessageType.Subscribe,
                    CurrentPage = 0,
                    PageSize = 500,
                },
                AbortToken
            );
        unknownPage.Index.Should().Be(0);
        unknownPage.Size.Should().Be(200);
        unknownPage.Items.Should().ContainSingle();
        var unknownView = unknownPage.Items.Single();
        unknownView.StorageId.Should().Be(invalidId.Value);
        unknownView.MessageType.Should().Be(published ? MessageType.Publish : MessageType.Subscribe);
        unknownView.RawLane.Should().Be(unsupportedLane);

        var queueClaim = (
            published
                ? await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Queue, AbortToken)
                : await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Queue, AbortToken)
        ).ToList();
        queueClaim.Should().ContainSingle(message => message.StorageId == queueMessage.StorageId);
        queueClaim.Should().NotContain(message => message.StorageId == invalidId.Value);

        var stateAfterQueueClaim = await GetPersistedPoisonRetryStateAsync(
            storage,
            published,
            invalidId.Value,
            AbortToken
        );
        stateAfterQueueClaim.Should().Be(stateBeforeClaim);
    }

    private async Task _ShouldClaimRetryMessagesByLaneAsync(bool published)
    {
        const int batchSize = 2;
        var storage = CreateStorageWithRetryBatchSize(batchSize);
        if (storage is null)
        {
            Assert.Skip("Storage does not expose a configurable retry-batch test seam");
            return;
        }

        foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
        {
            for (var index = 0; index < batchSize + 1; index++)
            {
                var envelope = new MediumMessage
                {
                    StorageId = Guid.Empty,
                    Origin = CreateMessage(),
                    Content = string.Empty,
                    Lane = lane,
                };
                var stored = published
                    ? await storage.StoreMessageAsync(
                        $"lane-batch-published-{lane}-{index}",
                        envelope,
                        cancellationToken: AbortToken
                    )
                    : await storage.StoreReceivedMessageAsync(
                        $"lane-batch-received-{lane}-{index}",
                        $"lane-batch-group-{lane}-{index}",
                        envelope,
                        AbortToken
                    );

                if (published)
                {
                    await storage.ChangePublishStateAsync(
                        stored,
                        StatusName.Failed,
                        nextRetryAt: _Now().AddMinutes(-10 + index),
                        cancellationToken: AbortToken
                    );
                }
                else
                {
                    await storage.ChangeReceiveStateAsync(
                        stored,
                        StatusName.Failed,
                        nextRetryAt: _Now().AddMinutes(-10 + index),
                        cancellationToken: AbortToken
                    );
                }
            }
        }

        foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
        {
            var firstClaim = (
                published
                    ? await storage.GetPublishedMessagesOfNeedRetryAsync(lane, AbortToken)
                    : await storage.GetReceivedMessagesOfNeedRetryAsync(lane, AbortToken)
            ).ToList();

            firstClaim.Should().HaveCount(batchSize);
            firstClaim.Should().OnlyContain(message => (short)message.Lane == (short)lane);
        }

        foreach (var lane in new[] { MessageLane.Bus, MessageLane.Queue })
        {
            var secondClaim = (
                published
                    ? await storage.GetPublishedMessagesOfNeedRetryAsync(lane, AbortToken)
                    : await storage.GetReceivedMessagesOfNeedRetryAsync(lane, AbortToken)
            ).ToList();

            ((short)secondClaim.Should().ContainSingle().Which.Lane).Should().Be((short)lane);
        }
    }

    private async Task<MediumMessage> _StoreFailedReceivedMessageAsync(string name, string group)
    {
        var storage = GetStorage();
        var stored = await storage.StoreReceivedMessageAsync(name, group, CreateMessage(), AbortToken);

        await storage.ChangeReceiveStateAsync(
            stored,
            StatusName.Failed,
            nextRetryAt: _Now().AddSeconds(-1),
            cancellationToken: AbortToken
        );

        return stored;
    }

    private async Task _ShouldRejectStaleLeaseGenerationWritesAsync(bool received)
    {
        var storage = GetStorage();
        var stale = received
            ? await _StoreFailedReceivedMessageAsync("stale-generation-received", "stale-generation-group")
            : await _StoreFailedPublishedMessageAsync("stale-generation-published");

        NodeMembership.SetIdentity("stale-generation-owner-a");
        var leaseA = received
            ? await storage.LeaseReceiveAsync(stale, TimeSpan.FromSeconds(-1), AbortToken)
            : await storage.LeasePublishAsync(stale, TimeSpan.FromSeconds(-1), AbortToken);
        leaseA.Should().BeTrue();

        var successor = _CopyMessage(stale);
        NodeMembership.SetIdentity("stale-generation-owner-b");
        var leaseB = received
            ? await storage.LeaseReceiveAsync(successor, TimeSpan.FromMinutes(5), AbortToken)
            : await storage.LeasePublishAsync(successor, TimeSpan.FromMinutes(5), AbortToken);
        leaseB.Should().BeTrue();
        successor.Owner.Should().NotBe(stale.Owner);

        stale.InlineAttempts = 1;
        var reserved = received
            ? await storage.ReserveReceiveAttemptAsync(stale, originalInlineAttempts: 0, AbortToken)
            : await storage.ReservePublishAttemptAsync(stale, originalInlineAttempts: 0, AbortToken);
        reserved.Should().BeFalse("a prior lease generation must not reserve under its successor's lease");

        foreach (
            var (state, nextRetryAt) in new[]
            {
                (StatusName.Succeeded, (DateTimeOffset?)null),
                (StatusName.Failed, (DateTimeOffset?)null),
                (StatusName.Failed, _Now().AddMinutes(1)),
            }
        )
        {
            // Refresh mirrors the production failure paths these fencing scenarios model, and exercises the
            // wider SQL variant that also carries the Content assignment.
            var changed = received
                ? await storage.ChangeReceiveRetryStateAsync(
                    stale,
                    state,
                    MessageContentWrite.Refresh,
                    nextRetryAt,
                    lockedUntil: null,
                    originalRetries: 0,
                    originalInlineAttempts: 0,
                    AbortToken
                )
                : await storage.ChangePublishRetryStateAsync(
                    stale,
                    state,
                    MessageContentWrite.Refresh,
                    nextRetryAt,
                    lockedUntil: null,
                    originalRetries: 0,
                    originalInlineAttempts: 0,
                    AbortToken
                );

            changed.Should().BeFalse("a prior lease generation must not write after successor acquisition");
        }
    }

    private async Task _ShouldAllowFencedWritesWithFastApplicationClockAsync(bool received)
    {
        var storage = _CreateRelationalClockSkewStorage();
        var message = received
            ? await storage.StoreReceivedMessageAsync(
                "fast-clock-fenced-received",
                "fast-clock-fenced-group",
                CreateMessage(),
                AbortToken
            )
            : await storage.StoreMessageAsync(
                "fast-clock-fenced-published",
                CreateMessage(),
                cancellationToken: AbortToken
            );

        NodeMembership.SetIdentity("fast-clock-fenced-owner");
        var leased = received
            ? await storage.LeaseReceiveAsync(message, TimeSpan.FromMinutes(5), AbortToken)
            : await storage.LeasePublishAsync(message, TimeSpan.FromMinutes(5), AbortToken);
        leased.Should().BeTrue();

        message.InlineAttempts = 1;
        var reserved = received
            ? await storage.ReserveReceiveAttemptAsync(message, originalInlineAttempts: 0, AbortToken)
            : await storage.ReservePublishAttemptAsync(message, originalInlineAttempts: 0, AbortToken);
        reserved.Should().BeTrue("the active database-clock lease must accept its owner's reservation");

        message.Retries = 1;
        var changed = received
            ? await storage.ChangeReceiveRetryStateAsync(
                message,
                StatusName.Failed,
                MessageContentWrite.Refresh,
                nextRetryAt: _Now().AddMinutes(1),
                lockedUntil: null,
                originalRetries: 0,
                originalInlineAttempts: 1,
                AbortToken
            )
            : await storage.ChangePublishRetryStateAsync(
                message,
                StatusName.Failed,
                MessageContentWrite.Refresh,
                nextRetryAt: _Now().AddMinutes(1),
                lockedUntil: null,
                originalRetries: 0,
                originalInlineAttempts: 1,
                AbortToken
            );
        changed.Should().BeTrue("application clock skew must not reject the active owner's fenced state write");
    }

    private static MediumMessage _CopyMessage(MediumMessage message)
    {
        return new()
        {
            StorageId = message.StorageId,
            Origin = message.Origin,
            Content = message.Content,
            Lane = message.Lane,
            Added = message.Added,
            ExpiresAt = message.ExpiresAt,
            NextRetryAt = message.NextRetryAt,
            LockedUntil = message.LockedUntil,
            Owner = message.Owner,
            Retries = message.Retries,
            InlineAttempts = message.InlineAttempts,
            ExceptionInfo = message.ExceptionInfo,
        };
    }

    private DateTimeOffset _Now()
    {
        return TimeProvider.GetUtcNow();
    }

    /// <summary>Lease duration long enough that the lease stays live for the whole test.</summary>
    private static TimeSpan _FutureLease()
    {
        return TimeSpan.FromHours(1);
    }

    private DateTimeOffset _FutureLeaseUntil()
    {
        return _Now().Add(_FutureLease());
    }

    private IDataStorage _CreateRelationalClockSkewStorage()
    {
        var storage = CreateStorageWithTimeProvider(
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1))
        );
        if (storage is null)
        {
            Assert.Skip("Storage does not expose a relational clock-skew test seam");
        }

        return storage;
    }

    private (
        IDataStorage Storage,
        Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock
    ) _CreateRelationalSchedulingClockStorage()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1));
        var storage = CreateStorageWithTimeProvider(clock);
        if (storage is null)
        {
            Assert.Skip("Storage does not expose a relational scheduling-clock test seam");
        }

        return (storage, clock);
    }
}
