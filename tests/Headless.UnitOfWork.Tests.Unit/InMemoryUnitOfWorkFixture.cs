// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Tests;

/// <summary>
/// In-memory conformance fixture: every session is a real <see cref="UnitOfWorkFactory" /> with a captured
/// logger.
/// </summary>
public sealed class InMemoryUnitOfWorkFixture : IUnitOfWorkFixture
{
    public UnitOfWorkSession CreateSession()
    {
        var logger = new CapturingLogger<UnitOfWorkFactory>();

        return new UnitOfWorkSession(new UnitOfWorkFactory(logger), logger.Entries);
    }
}

/// <summary>Runs the portable conformance scenarios against the in-memory factory.</summary>
public sealed class InMemoryUnitOfWorkConformanceTests()
    : UnitOfWorkConformanceTests<InMemoryUnitOfWorkFixture>(new InMemoryUnitOfWorkFixture());
