// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Tests.Authentication;
using Tests.Modules.Fixtures;

namespace Tests.Streaming;

/// <summary>The live stream under host authentication refuses a caller that holds no session.</summary>
public sealed class StreamAuthenticationTests : TestBase
{
    [Fact]
    public async Task should_answer_401_when_the_stream_is_opened_without_a_session_cookie()
    {
        await using var app = await ShellTestHost.StartAsync(
            dashboard =>
                dashboard
                    .WithHostAuthentication(host => host.ViewPolicy = ShellTestHost.OpsPolicy)
                    .AddModule<DiagEndpointModule, DiagModuleOptions>("diag"),
            new ShellTestHostOptions
            {
                Time = ShellTestHost.NewTime(),
                ConfigureServices = StreamingWiring.AddServices,
                MapHost = StreamingWiring.MapEndpoints,
            },
            AbortToken
        );
        using var client = ShellTestHost.Client(app);

        await using var stream = await SseStream.SendAsync(client, "/headless/api/stream", AbortToken);

        stream.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
