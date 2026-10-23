// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Capabilities;

namespace Tests.Guarantees;

/// <summary>The behavior area a guarantee belongs to.</summary>
[PublicAPI]
public enum GuaranteeArea
{
    Publish,
    DispatchRetry,
    Lease,
    Consume,
    Settlement,
    Inbox,
    CircuitBreaker,
    Shutdown,
    Lanes,
    Envelope,
    Connectivity,
    RequestReply,
    Storage,
    Coordination,
    Dashboard,
    Registration,
    Observability,
}

/// <summary>Where a guarantee stands in its lifecycle: no test yet, bound to a passing test, or quarantined.</summary>
[PublicAPI]
public enum GuaranteeStatus
{
    /// <summary>At least one bound test or supported manifest scenario proves it.</summary>
    Bound,

    /// <summary>No test proves it yet; the linked issue tracks writing one.</summary>
    NoTestYet,

    /// <summary>Its test exposes a product defect and skips through <see cref="MessagingGuarantees.RequireSupport(string)"/>.</summary>
    Unsupported,
}

/// <summary>One catalogued messaging guarantee.</summary>
/// <param name="Id">Stable ID of the form <c>MSG-AREA-NN</c>; never reuse a retired ID.</param>
/// <param name="Area">The behavior area.</param>
/// <param name="Statement">The guarantee in one line, phrased as what a consumer can rely on.</param>
/// <param name="RequiresRealSystem">
/// <see langword="true"/> when only a real broker or database shows the behavior, so a simulator or fake cannot stand as
/// its evidence.
/// </param>
/// <param name="Status">The lifecycle state.</param>
[PublicAPI]
public sealed record MessagingGuarantee(
    string Id,
    GuaranteeArea Area,
    string Statement,
    bool RequiresRealSystem,
    GuaranteeStatus Status
)
{
    /// <summary>The issue that tracks the missing test or the defect; required unless the guarantee is bound.</summary>
    public string? IssueUrl { get; init; }

    /// <summary>Why the guarantee is quarantined; required when <see cref="Status"/> is Unsupported.</summary>
    public string? Rationale { get; init; }

    /// <summary>
    /// Transport manifest scenarios that prove the guarantee when its tests live in provider leaves the catalogue
    /// cannot see. The manifest's own binding policy ties each supported cell to a test that runs.
    /// </summary>
    public IReadOnlyList<TransportConformanceScenario> ManifestScenarios { get; init; } = [];
}

/// <summary>The catalogue of critical messaging guarantees and their evidence state.</summary>
[PublicAPI]
public static class MessagingGuarantees
{
    /// <summary>Tracks the guarantees that still need a test in the fast tier or against a real system.</summary>
    public const string FillIssueUrl = "https://github.com/xshaheen/headless-framework/issues/1277";

    public static IReadOnlyList<MessagingGuarantee> All { get; } =
    [
        // Publish and outbox
        _Bound(
            "MSG-PUB-01",
            GuaranteeArea.Publish,
            realSystem: true,
            "A publish enlisted in a unit of work writes its outbox row in the unit's transaction: commit keeps one row, rollback leaves none."
        ),
        _Bound(
            "MSG-PUB-02",
            GuaranteeArea.Publish,
            realSystem: true,
            "A scheduled row and its not-before state are stored in one transaction."
        ),
        _Bound(
            "MSG-PUB-03",
            GuaranteeArea.Publish,
            realSystem: true,
            "The relay sends an enlisted row only after the unit commits, never before and never after a rollback."
        ),
        _Bound(
            "MSG-PUB-04",
            GuaranteeArea.Publish,
            realSystem: false,
            "Publishing on a completed unit, or through a binding taken before it completed, throws instead of writing outside the transaction."
        ),
        _Bound(
            "MSG-PUB-05",
            GuaranteeArea.Publish,
            realSystem: true,
            "Durable delivery through a unit whose database has no outbox is refused before any row is written or message sent."
        ),
        _Bound(
            "MSG-PUB-06",
            GuaranteeArea.Publish,
            realSystem: true,
            "One unreachable outbox database never blocks relay of the others, and the host starts while it is down."
        ),
        _Bound(
            "MSG-PUB-07",
            GuaranteeArea.Publish,
            realSystem: false,
            "With strict tenancy, a publish takes the tenant from its options, else the ambient tenant, else throws before any effect."
        ),
        _NoTestYet(
            "MSG-PUB-08",
            GuaranteeArea.Publish,
            realSystem: false,
            "A committed row whose in-process dispatch signal is lost is still sent by relay pickup."
        ),
        _Bound(
            "MSG-PUB-09",
            GuaranteeArea.Publish,
            realSystem: true,
            "Outbox rows written by the EF Core bridge are relayed to the broker after commit."
        ),
        _Bound(
            "MSG-PUB-10",
            GuaranteeArea.Publish,
            realSystem: true,
            "An outbox row written while the broker is down is delivered once the broker returns."
        ),
        // Dispatch and retry
        _Bound(
            "MSG-RETRY-01",
            GuaranteeArea.DispatchRetry,
            realSystem: true,
            "Retry pickup returns only due, unleased, non-terminal rows."
        ),
        _Bound(
            "MSG-RETRY-02",
            GuaranteeArea.DispatchRetry,
            realSystem: true,
            "Concurrent retry pickers on one backlog claim each row exactly once."
        ),
        _Bound(
            "MSG-RETRY-03",
            GuaranteeArea.DispatchRetry,
            realSystem: true,
            "A published row past the persisted retry cap is still claimed, so it can be made terminal instead of stranded."
        ),
        _NoTestYet(
            "MSG-RETRY-04",
            GuaranteeArea.DispatchRetry,
            realSystem: false,
            "A failing publish gets (inline + 1) x (persisted + 1) attempts, then ends Failed with no next retry and fires OnExhausted once."
        ),
        _NoTestYet(
            "MSG-RETRY-05",
            GuaranteeArea.DispatchRetry,
            realSystem: false,
            "After a crash that already reserved the inline budget, recovery does not send the message again."
        ),
        _NoTestYet(
            "MSG-RETRY-06",
            GuaranteeArea.DispatchRetry,
            realSystem: false,
            "A transport publish timeout outside shutdown is a transient failed attempt with back-off; shutdown cancellation writes nothing."
        ),
        _Bound(
            "MSG-RETRY-07",
            GuaranteeArea.DispatchRetry,
            realSystem: true,
            "A fresh row stays invisible to retry pickup during the initial dispatch grace, so the dispatcher and the relay do not both send it."
        ),
        _Bound(
            "MSG-RETRY-08",
            GuaranteeArea.DispatchRetry,
            realSystem: true,
            "A delayed row is claimed once it is due on the database clock, and concurrent claimers get disjoint rows."
        ),
        _Bound(
            "MSG-RETRY-09",
            GuaranteeArea.DispatchRetry,
            realSystem: true,
            "Revocation wins only before the first attempt is reserved, and a revocation racing that reservation has exactly one winner."
        ),
        _NoTestYet(
            "MSG-RETRY-10",
            GuaranteeArea.DispatchRetry,
            realSystem: false,
            "A transient storage outage during dispatch, retry, or inbox admission loses no row and spends no retry budget."
        ),
        // Leases and the storage clock
        _Bound(
            "MSG-LEASE-01",
            GuaranteeArea.Lease,
            realSystem: true,
            "Leasing a row and reserving an attempt is one fenced step; a stale attempt token or lease generation is rejected."
        ),
        _Bound(
            "MSG-LEASE-02",
            GuaranteeArea.Lease,
            realSystem: true,
            "Lease renewal extends only the live generation the caller holds and fails once another node took the lease."
        ),
        _Bound(
            "MSG-LEASE-03",
            GuaranteeArea.Lease,
            realSystem: true,
            "Lease expiry and retry due times are decided on the database clock, so a skewed app clock cannot steal a lease or retry early."
        ),
        _Bound(
            "MSG-LEASE-04",
            GuaranteeArea.Lease,
            realSystem: false,
            "A send or handler whose lease is lost is cancelled and writes no state, and the next owner's claim succeeds."
        ),
        // Consume and failure policy
        _Bound(
            "MSG-CONSUME-01",
            GuaranteeArea.Consume,
            realSystem: false,
            "A failing handler runs 1 + immediate + delayed attempts, then the row is Failed with no next retry and OnExhausted fires once."
        ),
        _Bound(
            "MSG-CONSUME-02",
            GuaranteeArea.Consume,
            realSystem: false,
            "A crash after the final attempt was reserved ends the row as spent without running the handler again."
        ),
        _Bound(
            "MSG-CONSUME-03",
            GuaranteeArea.Consume,
            realSystem: false,
            "A fail rule, permanent exception, undeserializable payload, or missing consumer fails the row at once and fires OnExhausted once."
        ),
        _Bound(
            "MSG-CONSUME-04",
            GuaranteeArea.Consume,
            realSystem: false,
            "When storage refuses a state write because the row is already terminal, the caller stops: no second OnExhausted and no breaker failure."
        ),
        _Bound(
            "MSG-CONSUME-05",
            GuaranteeArea.Consume,
            realSystem: false,
            "A handler that succeeded is not run again inline when its success write fails."
        ),
        _Bound(
            "MSG-CONSUME-06",
            GuaranteeArea.Consume,
            realSystem: false,
            "Shutdown during an attempt writes no state and fires no OnExhausted, and the row is retried after restart."
        ),
        _Bound(
            "MSG-CONSUME-07",
            GuaranteeArea.Consume,
            realSystem: true,
            "On a real broker and database, a throwing handler is retried per policy, ends terminal once, and its delivery is settled once."
        ),
        _Bound(
            "MSG-CONSUME-08",
            GuaranteeArea.Consume,
            realSystem: true,
            "In a transactional inbox attempt, handler state, inbox completion, and outbox rows commit or roll back together."
        ),
        _Bound(
            "MSG-CONSUME-09",
            GuaranteeArea.Consume,
            realSystem: true,
            "A consumer client never runs more deliveries at once than the concurrency it was asked for."
        ),
        // Settlement
        _Bound(
            "MSG-SETTLE-01",
            GuaranteeArea.Settlement,
            realSystem: false,
            "A delivery is committed to the broker only after its inbox admission or poison row is stored."
        ),
        _Bound(
            "MSG-SETTLE-02",
            GuaranteeArea.Settlement,
            realSystem: false,
            "When inbox admission cannot be stored, the delivery is not committed and goes back to the broker for redelivery."
        ),
        _Bound(
            "MSG-SETTLE-03",
            GuaranteeArea.Settlement,
            realSystem: true,
            "Committing a delivery prevents its redelivery, and rejecting it redelivers it."
        ),
        _Bound(
            "MSG-SETTLE-04",
            GuaranteeArea.Settlement,
            realSystem: true,
            "A settlement value that was already used never settles another delivery."
        ),
        _Bound(
            "MSG-SETTLE-05",
            GuaranteeArea.Settlement,
            realSystem: true,
            "A callback that throws leaves the delivery available for redelivery and the listener running."
        ),
        _Scenarios(
            "MSG-SETTLE-06",
            GuaranteeArea.Settlement,
            "A malformed envelope is settled terminally without running a handler and is not redelivered across a consumer restart.",
            TransportConformanceScenario.MalformedEnvelopeTerminalSettlement
        ),
        _Bound(
            "MSG-SETTLE-07",
            GuaranteeArea.Settlement,
            realSystem: true,
            "A poisoned delivery produces exactly one dead-letter copy and settles the original once."
        ),
        _Bound(
            "MSG-SETTLE-08",
            GuaranteeArea.Settlement,
            realSystem: false,
            "A header builder that throws a transient error leads to redelivery, then dead-letter once the retry budget is spent."
        ),
        // Inbox
        _Bound(
            "MSG-INBOX-01",
            GuaranteeArea.Inbox,
            realSystem: true,
            "Exactly one of many concurrent admissions of one inbox key wins; the rest are duplicates."
        ),
        _Bound(
            "MSG-INBOX-02",
            GuaranteeArea.Inbox,
            realSystem: true,
            "A terminal inbox row suppresses redelivery of the same message to the same consumer."
        ),
        _Bound(
            "MSG-INBOX-03",
            GuaranteeArea.Inbox,
            realSystem: true,
            "Every inbox key component, tenant included, isolates rows, and key lengths are enforced without truncation."
        ),
        _Bound(
            "MSG-INBOX-04",
            GuaranteeArea.Inbox,
            realSystem: false,
            "A redelivery of an already-admitted message is committed as a duplicate: no poison row, dead-letter, OnExhausted, or breaker failure."
        ),
        _Bound(
            "MSG-INBOX-05",
            GuaranteeArea.Inbox,
            realSystem: true,
            "With a real broker and database, a duplicate broker delivery runs the handler once."
        ),
        _Bound(
            "MSG-INBOX-06",
            GuaranteeArea.Inbox,
            realSystem: true,
            "A transactional inbox commit whose reply is lost is resolved as committed: the handler is not run again and a redelivery is a duplicate."
        ),
        // Circuit breaker and consumer register
        _Bound(
            "MSG-CB-01",
            GuaranteeArea.CircuitBreaker,
            realSystem: false,
            "The circuit opens at the failure threshold, half-opens after its timer with one probe, and closes on probe success or reopens on a transient failure."
        ),
        _Bound(
            "MSG-CB-02",
            GuaranteeArea.CircuitBreaker,
            realSystem: false,
            "A success or failure report from an earlier circuit epoch never moves a half-open circuit."
        ),
        _Bound(
            "MSG-CB-03",
            GuaranteeArea.CircuitBreaker,
            realSystem: false,
            "An open circuit pauses every client of its consumer identity and lane, and a client added while it half-opens ends resumed."
        ),
        _Bound(
            "MSG-CB-04",
            GuaranteeArea.CircuitBreaker,
            realSystem: true,
            "Retry rows of an open consumer are deferred to the probe time only on their exact live lease generation."
        ),
        _Scenarios(
            "MSG-CB-05",
            GuaranteeArea.CircuitBreaker,
            "Pausing a consumer stops broker delivery, and resuming it delivers once with no duplicate.",
            TransportConformanceScenario.ConsumerPauseRecovery
        ),
        _Bound(
            "MSG-CB-06",
            GuaranteeArea.CircuitBreaker,
            realSystem: true,
            "A message store that becomes unreachable fails admission with a fault the default classifier counts as transient, so the consumer's circuit opens at its threshold on every relational provider."
        ),
        // Shutdown and drain
        _Bound(
            "MSG-SHUT-01",
            GuaranteeArea.Shutdown,
            realSystem: true,
            "Graceful shutdown releases only the exact lease generation it recorded and never another owner's lease."
        ),
        _Bound(
            "MSG-SHUT-02",
            GuaranteeArea.Shutdown,
            realSystem: false,
            "The in-flight drain waits for tracked handlers up to its budget, keeps draining after a fault, and surfaces the fault."
        ),
        _Bound(
            "MSG-SHUT-03",
            GuaranteeArea.Shutdown,
            realSystem: true,
            "Every consumer client stops within a bound, whether idle or with a handler running."
        ),
        _Bound(
            "MSG-SHUT-04",
            GuaranteeArea.Shutdown,
            realSystem: false,
            "A message cut off by graceful shutdown is processed exactly once after restart."
        ),
        _Bound(
            "MSG-SHUT-05",
            GuaranteeArea.Shutdown,
            realSystem: false,
            "Settlement of an in-progress delivery completes during host shutdown instead of being abandoned."
        ),
        // Bus and queue lanes
        _Scenarios(
            "MSG-LANE-01",
            GuaranteeArea.Lanes,
            "A bus message reaches one copy per consumer identity, and replicas of one identity compete for that copy.",
            TransportConformanceScenario.BusConsumerIdentityFanOut,
            TransportConformanceScenario.BusReplicaCompetition
        ),
        _Scenarios(
            "MSG-LANE-02",
            GuaranteeArea.Lanes,
            "A queue message reaches one owner across replicas, and a name used on both lanes is delivered only on its own lane.",
            TransportConformanceScenario.QueueOwnership,
            TransportConformanceScenario.SameNameLaneIsolation
        ),
        _Bound(
            "MSG-LANE-03",
            GuaranteeArea.Lanes,
            realSystem: true,
            "On a broker that orders by key, messages that share a routing affinity key arrive in publish order."
        ),
        // Envelope
        _Bound(
            "MSG-ENV-01",
            GuaranteeArea.Envelope,
            realSystem: true,
            "The type, correlation, sent-time, tenant, and trace-parent headers reach the consumer unchanged."
        ),
        _Bound(
            "MSG-ENV-02",
            GuaranteeArea.Envelope,
            realSystem: true,
            "A body at the broker's documented size limit arrives intact, and one byte more fails at publish instead of being lost."
        ),
        _Bound(
            "MSG-ENV-03",
            GuaranteeArea.Envelope,
            realSystem: true,
            "A message left unconsumed past its time-to-live is never delivered."
        ),
        // Connectivity
        _Bound(
            "MSG-CONN-01",
            GuaranteeArea.Connectivity,
            realSystem: true,
            "A consumer resumes delivery exactly once after a broker restart."
        ),
        _Bound(
            "MSG-CONN-02",
            GuaranteeArea.Connectivity,
            realSystem: true,
            "A consumer resumes delivery once after a dropped connection and consumes what was published during the outage."
        ),
        _Bound(
            "MSG-CONN-03",
            GuaranteeArea.Connectivity,
            realSystem: false,
            "A consumer client whose channel shut down is restarted at the next transport check, whatever another client of its subscription reports."
        ) with
        {
            Status = GuaranteeStatus.Unsupported,
            Rationale =
                "The consumer register keeps one process-wide health flag, so one client's ConsumerRegistered hides another client's shutdown and the transport check never restarts it.",
            IssueUrl = "https://github.com/xshaheen/headless-framework/issues/1245",
        },
        // Request/reply
        _Bound("MSG-RR-01", GuaranteeArea.RequestReply, realSystem: true, "A caller receives only its own replies."),
        _Bound(
            "MSG-RR-02",
            GuaranteeArea.RequestReply,
            realSystem: true,
            "A reply is never written to an address outside the reply namespace."
        ),
        _Bound(
            "MSG-RR-03",
            GuaranteeArea.RequestReply,
            realSystem: true,
            "A restarted caller never receives a reply addressed to its previous run."
        ),
        _Bound(
            "MSG-RR-04",
            GuaranteeArea.RequestReply,
            realSystem: true,
            "A responder failure faults the caller, and a reply that arrives after the call timed out is dropped."
        ),
        _Bound(
            "MSG-RR-05",
            GuaranteeArea.RequestReply,
            realSystem: false,
            "A request past its deadline is not handled on any attempt and ends with no reply, OnExhausted, or breaker failure."
        ),
        _NoTestYet(
            "MSG-RR-06",
            GuaranteeArea.RequestReply,
            realSystem: true,
            "A reply is sent only after the success write takes effect, and on the transactional tier only after the commit is durable."
        ),
        // Storage
        _Bound(
            "MSG-STORE-01",
            GuaranteeArea.Storage,
            realSystem: true,
            "A terminal row is sealed: later state writes and redelivery upserts leave it unchanged and report no change."
        ),
        _Bound(
            "MSG-STORE-02",
            GuaranteeArea.Storage,
            realSystem: true,
            "Of many concurrent state writes to one row, exactly one wins."
        ),
        _Bound(
            "MSG-STORE-03",
            GuaranteeArea.Storage,
            realSystem: true,
            "Expired-row cleanup deletes only expired terminal rows, never a Failed row with a pending retry."
        ),
        _Bound(
            "MSG-STORE-04",
            GuaranteeArea.Storage,
            realSystem: true,
            "Schedules and revocations survive a storage restart and clock steps, and a revoked schedule is not resurrected at shutdown."
        ),
        _Bound(
            "MSG-STORE-05",
            GuaranteeArea.Storage,
            realSystem: true,
            "The EF Core storage variants pass the same storage conformance suite as the ADO.NET providers."
        ),
        // Coordination and dead-owner reclaim
        _Bound(
            "MSG-COORD-01",
            GuaranteeArea.Coordination,
            realSystem: true,
            "Dead-owner reclaim touches only owners classified Dead, never Suspected ones."
        ),
        _Bound(
            "MSG-COORD-02",
            GuaranteeArea.Coordination,
            realSystem: true,
            "Reclaim is idempotent: a dead owner surfaced twice, or by two bridges at once, is reclaimed once."
        ),
        _Bound(
            "MSG-COORD-03",
            GuaranteeArea.Coordination,
            realSystem: true,
            "Rows of a live or restarted incarnation of a node are never reclaimed."
        ),
        _Bound(
            "MSG-COORD-04",
            GuaranteeArea.Coordination,
            realSystem: true,
            "With a real coordination provider, a killed node's in-flight rows are redelivered by another node with no loss and no duplicate."
        ),
        _Bound(
            "MSG-COORD-05",
            GuaranteeArea.Coordination,
            realSystem: true,
            "Several hosts draining one retry backlog on one database run each row exactly once."
        ),
        _Bound(
            "MSG-COORD-06",
            GuaranteeArea.Coordination,
            realSystem: true,
            "Delayed rows are sent exactly once while hosts restart one after another."
        ),
        // Dashboard operator actions
        _Bound(
            "MSG-DASH-01",
            GuaranteeArea.Dashboard,
            realSystem: true,
            "An audited inbox operation applies once, and a reused operation ID with a different request is rejected."
        ),
        _Bound(
            "MSG-DASH-02",
            GuaranteeArea.Dashboard,
            realSystem: true,
            "When revoke and dispatch-now race on one scheduled row, exactly one action wins."
        ),
        _Bound(
            "MSG-DASH-03",
            GuaranteeArea.Dashboard,
            realSystem: false,
            "Requeue of a Succeeded published row is refused, and requeue of a Failed row sends it once."
        ),
        // Registration and options
        _Bound(
            "MSG-REG-01",
            GuaranteeArea.Registration,
            realSystem: false,
            "Startup rejects a dispatch timeout that does not exceed the worst-case inline publish burst."
        ),
        // Observability
        _Bound(
            "MSG-OBS-01",
            GuaranteeArea.Observability,
            realSystem: false,
            "Log event IDs are unique across the messaging assemblies."
        ),
    ];

    /// <summary>Returns the catalogued guarantee with <paramref name="id"/>, or <see langword="null"/>.</summary>
    public static MessagingGuarantee? Find(string id) => Find(All, id);

    /// <summary>Returns the guarantee with <paramref name="id"/> in <paramref name="catalogue"/>, or <see langword="null"/>.</summary>
    public static MessagingGuarantee? Find(IEnumerable<MessagingGuarantee> catalogue, string id) =>
        catalogue.FirstOrDefault(guarantee => string.Equals(guarantee.Id, id, StringComparison.Ordinal));

    /// <summary>Skips the calling test when the guarantee is quarantined as Unsupported against an open issue.</summary>
    /// <exception cref="InvalidOperationException">The ID is not in the catalogue.</exception>
    public static void RequireSupport(string id) => RequireSupport(All, id);

    /// <inheritdoc cref="RequireSupport(string)"/>
    public static void RequireSupport(IEnumerable<MessagingGuarantee> catalogue, string id)
    {
        var guarantee =
            Find(catalogue, id) ?? throw new InvalidOperationException($"{id} is not in the guarantee catalogue.");

        if (guarantee.Status == GuaranteeStatus.Unsupported)
        {
            Assert.Skip($"{id}: Unsupported. {guarantee.Rationale} {guarantee.IssueUrl}");
        }
    }

    private static MessagingGuarantee _Bound(string id, GuaranteeArea area, bool realSystem, string statement) =>
        new(id, area, statement, realSystem, GuaranteeStatus.Bound);

    private static MessagingGuarantee _Scenarios(
        string id,
        GuaranteeArea area,
        string statement,
        params TransportConformanceScenario[] scenarios
    ) => _Bound(id, area, realSystem: true, statement) with { ManifestScenarios = scenarios };

    private static MessagingGuarantee _NoTestYet(
        string id,
        GuaranteeArea area,
        bool realSystem,
        string statement,
        string issueUrl = FillIssueUrl
    ) => new(id, area, statement, realSystem, GuaranteeStatus.NoTestYet) { IssueUrl = issueUrl };
}
