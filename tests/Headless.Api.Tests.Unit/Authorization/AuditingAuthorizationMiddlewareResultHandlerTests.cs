// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Authorization;
using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Authorization;

public sealed class AuditingAuthorizationMiddlewareResultHandlerTests : TestBase
{
    [Fact]
    public async Task should_write_denial_audit_with_uncancelled_token_when_client_has_already_disconnected()
    {
        // given: a request whose connection already reset (RequestAborted is already cancelled), mirroring a
        // client that sends headers then resets the connection before the audit write can run.
        var writer = Substitute.For<IAuditLogWriter<object>>();
        writer.WriteAsync(Arg.Any<AuditLogWriteRequest>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var inner = Substitute.For<IAuthorizationMiddlewareResultHandler>();
        var handler = new AuditingAuthorizationMiddlewareResultHandler<object>(
            inner,
            NullLogger<AuditingAuthorizationMiddlewareResultHandler<object>>.Instance
        );

        var services = new ServiceCollection();
        services.AddSingleton(writer);
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider, RequestAborted = new(canceled: true) };
        var policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
        var authorizeResult = PolicyAuthorizationResult.Forbid();

        // when
        await handler.HandleAsync(_ => Task.CompletedTask, context, policy, authorizeResult);

        // then: the write is not tied to the client connection, so it still runs with an uncancelled token, and
        // the wrapped handler still produces the response.
        await writer
            .Received(1)
            .WriteAsync(
                Arg.Any<AuditLogWriteRequest>(),
                Arg.Is<CancellationToken>(token => token == CancellationToken.None)
            );
        await inner.Received(1).HandleAsync(Arg.Any<RequestDelegate>(), context, policy, authorizeResult);
    }
}
