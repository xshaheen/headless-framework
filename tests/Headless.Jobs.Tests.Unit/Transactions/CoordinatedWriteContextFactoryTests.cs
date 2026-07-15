// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Tests.Transactions;

/// <summary>
/// Coverage for the coordinated-write constructor contract that the persistence-provider registration validates
/// up front: a context missing the single-argument <c>DbContextOptions&lt;TContext&gt;</c> ctor must fail with the
/// direct authored message (at DI-build), not a <see cref="TypeInitializationException" /> wrapper from the
/// provider's static factory at first coordinated write.
/// </summary>
public sealed class CoordinatedWriteContextFactoryTests
{
    [Fact]
    public void require_options_constructor_returns_ctor_when_options_ctor_is_present()
    {
        var act = CoordinatedWriteContextFactory.RequireOptionsConstructor<WellFormedContext>;

        act.Should().NotThrow();
        CoordinatedWriteContextFactory.RequireOptionsConstructor<WellFormedContext>().Should().NotBeNull();
    }

    [Fact]
    public void require_options_constructor_throws_direct_invalid_operation_when_options_ctor_is_missing()
    {
        var act = CoordinatedWriteContextFactory.RequireOptionsConstructor<MissingOptionsCtorContext>;

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*MissingOptionsCtorContext*single DbContextOptions*");
    }

    private sealed class WellFormedContext(DbContextOptions<WellFormedContext> options) : DbContext(options) { }

    // No single-arg DbContextOptions<TContext> ctor (only the implicit parameterless one), so
    // RequireOptionsConstructor must reject it. Never instantiated — the check is pure reflection.
    private sealed class MissingOptionsCtorContext : DbContext;
}
