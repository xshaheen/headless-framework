// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Exceptions;
using Headless.Testing.Tests;

namespace Tests.CircuitBreaker;

public sealed class CircuitBreakerDefaultsTests : TestBase
{
    // -------------------------------------------------------------------------
    // Transient exceptions → true
    // -------------------------------------------------------------------------

    [Fact]
    public void timeout_exception_is_transient()
    {
        CircuitBreakerDefaults.IsTransient(new TimeoutException()).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void http_request_exception_with_5xx_status_is_transient(HttpStatusCode statusCode)
    {
        var ex = new HttpRequestException("server error", null, statusCode);

        CircuitBreakerDefaults.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void http_request_exception_with_socket_inner_exception_is_transient()
    {
        var inner = new SocketException();
        var ex = new HttpRequestException("connection refused", inner);

        CircuitBreakerDefaults.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void socket_exception_is_transient()
    {
        CircuitBreakerDefaults.IsTransient(new SocketException()).Should().BeTrue();
    }

    [Fact]
    public void broker_connection_exception_is_transient()
    {
        var ex = new BrokerConnectionException(new Exception("broker down"));

        CircuitBreakerDefaults.IsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void task_canceled_exception_without_cancellation_requested_is_transient()
    {
        // TaskCanceledException with default token (IsCancellationRequested == false) → timeout
        var ex = new TaskCanceledException();

        CircuitBreakerDefaults.IsTransient(ex).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Non-transient exceptions → false
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    public void http_request_exception_with_4xx_status_is_not_transient(HttpStatusCode statusCode)
    {
        var ex = new HttpRequestException("bad request", null, statusCode);

        CircuitBreakerDefaults.IsTransient(ex).Should().BeFalse();
    }

    [Fact]
    public void http_request_exception_with_null_status_is_not_transient()
    {
        var ex = new HttpRequestException("unknown");

        CircuitBreakerDefaults.IsTransient(ex).Should().BeFalse();
    }

    [Fact]
    public void task_canceled_exception_with_cancellation_requested_is_not_transient()
    {
        // Simulates deliberate cancellation (e.g. app shutdown)
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = new TaskCanceledException("cancelled", null, cts.Token);

        CircuitBreakerDefaults.IsTransient(ex).Should().BeFalse();
    }

    [Fact]
    public void argument_exception_is_not_transient()
    {
        CircuitBreakerDefaults.IsTransient(new ArgumentException("bad arg")).Should().BeFalse();
    }

    [Fact]
    public void invalid_operation_exception_is_not_transient()
    {
        CircuitBreakerDefaults.IsTransient(new InvalidOperationException()).Should().BeFalse();
    }

    [Fact]
    public void null_reference_exception_is_not_transient()
    {
        CircuitBreakerDefaults.IsTransient(new NullReferenceException()).Should().BeFalse();
    }

    [Fact]
    public void operation_canceled_exception_is_not_transient()
    {
        // OperationCanceledException (not TaskCanceledException) → not matched
        CircuitBreakerDefaults.IsTransient(new OperationCanceledException()).Should().BeFalse();
    }
}
