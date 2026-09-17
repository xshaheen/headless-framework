// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

// CA2000: the session owns the manager it was built with and disposes it via UnitOfWorkSession.DisposeAsync.

namespace Tests;

/// <summary>
/// In-memory conformance fixture: every session is a real <see cref="UnitOfWorkManager" /> with a captured
/// logger. The returned <see cref="UnitOfWorkSession" /> owns the manager and disposes it.
/// </summary>
[SuppressMessage(
    "Usage",
    "CA2000:Dispose objects before losing scope",
    Justification = "The session owns the manager and disposes it."
)]
public sealed class InMemoryUnitOfWorkFixture : IUnitOfWorkFixture
{
    public UnitOfWorkSession CreateSession()
    {
        var logger = new CapturingLogger<UnitOfWorkManager>();
        var manager = new UnitOfWorkManager(logger);

        return new UnitOfWorkSession(manager, logger.Entries);
    }
}

/// <summary>Runs the portable conformance scenarios against the in-memory manager.</summary>
public sealed class InMemoryUnitOfWorkConformanceTests()
    : UnitOfWorkConformanceTests<InMemoryUnitOfWorkFixture>(new InMemoryUnitOfWorkFixture());
