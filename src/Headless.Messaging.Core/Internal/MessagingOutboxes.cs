// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>A storage whose database identity can be compared with a unit of work's connection.</summary>
internal interface IRelationalOutboxStorage
{
    /// <summary>
    /// Builds, but never opens, a connection from this storage's own configuration. It is only read for its
    /// provider type, data source, and database name.
    /// </summary>
    DbConnection CreateIdentityConnection();
}

/// <summary>
/// One additional outbox registered through <c>AddOutbox().Use…()</c>. The provider package supplies the factory;
/// the storage it builds serves only the published rows of its own database.
/// </summary>
internal sealed class OutboxStorageRegistration(string name, Func<IServiceProvider, MessagingOutbox> factory)
{
    public string Name { get; } = Argument.IsNotNullOrWhiteSpace(name);

    public MessagingOutbox Create(IServiceProvider serviceProvider) => factory(serviceProvider);
}

/// <summary>An additional outbox storage: published rows and their relay for one database.</summary>
/// <remarks>
/// Its schema is initialized apart from the host's startup: an outbox whose database is unreachable at startup
/// leaves the host running, and the first of the background retry or a unit of work on that database initializes
/// it. Until then its relay skips it, and a unit of work on its database initializes it before writing.
/// </remarks>
internal sealed class MessagingOutbox
{
    private readonly Lock _initializationLock = new();
    private Task? _initialization;

    public MessagingOutbox(string name, IDataStorage storage, IStorageInitializer initializer)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(storage);
        Argument.IsNotNull(initializer);

        if (
            storage is not IDeliveryCoordinationResolver coordination
            || storage is not IRelationalOutboxStorage relational
        )
        {
            throw new MessagingConfigurationException(
                $"Outbox storage '{name}' must be a relational storage that can join a unit of work."
            );
        }

        Name = name;
        Storage = storage;
        Initializer = initializer;
        Coordination = coordination;
        Relational = relational;
    }

    public string Name { get; }

    public IDataStorage Storage { get; }

    public IStorageInitializer Initializer { get; }

    public IDeliveryCoordinationResolver Coordination { get; }

    public IRelationalOutboxStorage Relational { get; }

    /// <summary>Whether this outbox's schema initialization has completed in this process.</summary>
    public bool IsInitialized
    {
        get
        {
            lock (_initializationLock)
            {
                return _initialization is { IsCompletedSuccessfully: true };
            }
        }
    }

    /// <summary>
    /// Initializes this outbox's schema unless that already succeeded. Concurrent callers share one attempt; a failed
    /// attempt is reported to every caller that shared it, and the next call starts a fresh one.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task attempt;
            TaskCompletionSource? owned = null;

            lock (_initializationLock)
            {
                if (_initialization is null or { IsFaulted: true } or { IsCanceled: true })
                {
                    owned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _initialization = owned.Task;
                }

                attempt = _initialization;
            }

            if (owned is not null)
            {
                // Run outside the lock: initialization is database I/O, and IsInitialized readers must not wait on it.
                try
                {
                    await Initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    owned.SetResult();
                }
                catch (OperationCanceledException e)
                {
                    owned.SetCanceled(e.CancellationToken);
                }
                catch (Exception e)
                {
                    owned.SetException(e);
                }
            }

            try
            {
                await attempt.WaitAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The shared attempt was canceled by the caller that started it, not by this one: start another.
            }
        }
    }

    /// <summary>
    /// A short stable key for this outbox's database, used to give its relay its own lock resources. Replicas of
    /// one application derive the same key from the same configuration; two replicas that spell the host
    /// differently take separate locks, which costs duplicate polling but not correctness, because the per-row
    /// lease is what fences a published row.
    /// </summary>
    public string LockKey
    {
        get
        {
            if (field is not null)
            {
                return field;
            }

            using var connection = Relational.CreateIdentityConnection();
            var identity =
                $"{connection.GetType().FullName}|{connection.DataSource?.Trim().ToLowerInvariant()}|{connection.Database}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));

            return field = Convert.ToHexStringLower(hash.AsSpan(0, 8));
        }
    }
}

/// <summary>
/// Every outbox storage of the host: the primary storage, which also owns the inbox, retry state, and the
/// dashboard, plus the additional outboxes registered through <c>AddOutbox()</c>. A unit of work's publish goes to
/// the one outbox whose database matches the unit.
/// </summary>
/// <remarks>
/// A published row from an additional outbox carries its storage in <c>MediumMessage.OutboxStorage</c> from the
/// moment core reads or writes it, so every later state change of that row reaches the database that holds it.
/// </remarks>
internal sealed class MessagingOutboxes : IDeliveryCoordinationResolver
{
    private const string _PrimaryName = "the primary storage";

    private readonly IDeliveryCoordinationResolver? _primaryCoordination;

    public MessagingOutboxes(
        IDataStorage? primary,
        IDeliveryCoordinationResolver? primaryCoordination,
        IReadOnlyList<MessagingOutbox> secondaries
    )
    {
        Argument.IsNotNull(secondaries);

        Primary = primary;
        _primaryCoordination = primaryCoordination;
        Secondaries = secondaries;

        if (secondaries.Count > 0)
        {
            _Validate(primary, secondaries);
        }
    }

    public static MessagingOutboxes Create(IServiceProvider serviceProvider)
    {
        var secondaries = serviceProvider
            .GetServices<OutboxStorageRegistration>()
            .Select(registration => registration.Create(serviceProvider))
            .ToArray();

        return new MessagingOutboxes(
            serviceProvider.GetService<IDataStorage>(),
            serviceProvider.GetService<IDeliveryCoordinationResolver>(),
            secondaries
        );
    }

    /// <summary>The primary storage, or <see langword="null" /> on a host without storage.</summary>
    public IDataStorage? Primary { get; }

    /// <summary>The additional outboxes, in registration order.</summary>
    public IReadOnlyList<MessagingOutbox> Secondaries { get; }

    /// <summary>
    /// Picks the outbox whose database the unit's transaction belongs to. With only the primary storage this is
    /// exactly that storage's own answer. With several, the first compatible outbox wins (startup validation
    /// guarantees at most one); when none is, the most specific refusal is reported, so a unit on an unregistered
    /// database is refused as a database mismatch rather than as another provider's type mismatch.
    /// </summary>
    public DeliveryCoordination Resolve(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        if (Primary is null)
        {
            return _ResolveWith(resolver: null, unitOfWork);
        }

        var primary = _ResolveWith(_primaryCoordination, unitOfWork);
        if (primary.Status is DeliveryCoordinationStatus.Compatible || Secondaries.Count == 0)
        {
            return primary;
        }

        var best = primary;
        foreach (var outbox in Secondaries)
        {
            var candidate = outbox.Coordination.Resolve(unitOfWork);
            if (candidate.Status is DeliveryCoordinationStatus.Compatible)
            {
                return candidate.WithOutbox(outbox);
            }

            if (
                candidate.Status is DeliveryCoordinationStatus.Incompatible
                && (
                    best.Status is not DeliveryCoordinationStatus.Incompatible
                    || _Specificity(candidate.Mismatch) > _Specificity(best.Mismatch)
                )
            )
            {
                best = candidate;
            }
        }

        return best;
    }

    private static DeliveryCoordination _ResolveWith(IDeliveryCoordinationResolver? resolver, IUnitOfWork unitOfWork)
    {
        if (resolver is not null)
        {
            // The storage decides: the in-memory storage joins a resource-less unit through its buffer, the
            // relational storages join only a same-database relational resource.
            return resolver.Resolve(unitOfWork);
        }

        // A storage with no resolver can join nothing. A resource-less unit then behaves like no unit at all
        // rather than as an incompatible one, so Optional still writes a standalone durable row.
        return unitOfWork.Resource is null
            ? DeliveryCoordination.None
            : DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.MissingRelationalCapability);
    }

    // A finished transaction is the unit's own fault whichever outbox looked at it; a database mismatch means a
    // storage of the right provider looked and refused, which says more than a provider mismatch from another.
    private static int _Specificity(DeliveryCoordinationMismatch mismatch)
    {
        return mismatch switch
        {
            DeliveryCoordinationMismatch.TransactionCompleted => 4,
            DeliveryCoordinationMismatch.Database => 3,
            DeliveryCoordinationMismatch.StorageProvider => 2,
            _ => 1,
        };
    }

    private static void _Validate(IDataStorage? primary, IReadOnlyList<MessagingOutbox> secondaries)
    {
        if (primary is not IRelationalOutboxStorage primaryRelational)
        {
            throw new MessagingConfigurationException(
                primary is null
                    ? "AddOutbox() requires a primary messaging storage. Configure one with UseEntityFramework<TContext>(), UsePostgreSql(...), or UseSqlServer(...)."
                    : $"AddOutbox() requires a relational primary messaging storage, but the primary storage is '{primary.GetType().Name}'."
            );
        }

        var identities = new List<(string Name, DbConnection Connection)>(secondaries.Count + 1);
        try
        {
            identities.Add((_PrimaryName, primaryRelational.CreateIdentityConnection()));
            foreach (var outbox in secondaries)
            {
                identities.Add((outbox.Name, outbox.Relational.CreateIdentityConnection()));
            }

            for (var i = 0; i < identities.Count; i++)
            {
                for (var j = i + 1; j < identities.Count; j++)
                {
                    if (RelationalDatabaseIdentity.IsSameDatabase(identities[i].Connection, identities[j].Connection))
                    {
                        throw new MessagingConfigurationException(
                            $"Outbox storages '{identities[i].Name}' and '{identities[j].Name}' both resolve to database "
                                + $"'{identities[j].Connection.Database}' on '{identities[j].Connection.DataSource}'. "
                                + "Register one outbox per database."
                        );
                    }
                }
            }
        }
        finally
        {
            foreach (var (_, connection) in identities)
            {
                connection.Dispose();
            }
        }
    }
}
