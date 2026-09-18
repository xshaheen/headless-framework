// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class InMemoryReceiveMiddlewareOutcomeConformanceTests : TestBase
{
    [Fact]
    public Task should_execute_accept_outcome_conformance() =>
        ReceiveMiddlewareOutcomeConformance.AssertAcceptOutcomeAsync(
            setup => setup.UseInMemory(),
            setup => setup.UseInMemoryStorage(),
            LoggerProvider,
            AbortToken
        );

    [Fact]
    public Task should_execute_skip_outcome_conformance() =>
        ReceiveMiddlewareOutcomeConformance.AssertSkipOutcomeAsync(
            setup => setup.UseInMemory(),
            setup => setup.UseInMemoryStorage(),
            LoggerProvider,
            AbortToken
        );

    [Fact]
    public Task should_execute_reject_outcome_conformance() =>
        ReceiveMiddlewareOutcomeConformance.AssertRejectOutcomeAsync(
            setup => setup.UseInMemory(),
            setup => setup.UseInMemoryStorage(),
            LoggerProvider,
            AbortToken
        );

    [Fact]
    public Task should_execute_cancelled_outcome_conformance() =>
        ReceiveMiddlewareOutcomeConformance.AssertCancelledOutcomeAsync(
            setup => setup.UseInMemory(),
            setup => setup.UseInMemoryStorage(),
            LoggerProvider,
            AbortToken
        );
}
