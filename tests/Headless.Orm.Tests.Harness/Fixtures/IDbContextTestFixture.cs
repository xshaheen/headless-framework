// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Fixtures;

/// <summary>
/// Interface for database context test fixtures that provide shared test infrastructure.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public interface IDbContextTestFixture<out TContext>
    where TContext : DbContext
{
    /// <summary>
    /// The service provider configured for tests.
    /// </summary>
    ServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Test clock for controlling time in tests.
    /// </summary>
    TestClock Clock { get; }

    /// <summary>
    /// Test current tenant for multi-tenancy testing.
    /// </summary>
    TestCurrentTenant CurrentTenant { get; }

    /// <summary>
    /// Test current user for audit testing.
    /// </summary>
    TestCurrentUser CurrentUser { get; }

    /// <summary>
    /// Fixed "now" timestamp for audit assertions.
    /// </summary>
    DateTimeOffset Now { get; }

    /// <summary>
    /// Fixed user ID string for audit assertions.
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// Creates a new context instance within a service scope.
    /// </summary>
    TContext CreateContext(IServiceScope scope);
}
