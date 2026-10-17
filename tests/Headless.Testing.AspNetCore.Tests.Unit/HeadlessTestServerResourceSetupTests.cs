// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class HeadlessTestServerResourceSetupTests : TestBase
{
    private HeadlessTestServer<Program>? _server;

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_keep_the_reset_state_action_the_application_registered()
    {
        // given — the application's own service registration asks for ResetState, as Program.cs would
        var resource = new CountingResource();
        _server = new HeadlessTestServer<Program>(configureWebHost: builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHeadlessStatefulResource(_ => resource);
                services.AddHeadlessResourceSetupOnStartup(StatefulResourceStartupAction.ResetState);
            })
        );

        // when
        await _server.InitializeAsync();

        // then
        resource.Setups.Should().Be(1);
        resource.Clears.Should().Be(1, "the test server's default must not replace the application's ResetState");
    }

    [Fact]
    public async Task should_only_set_up_resources_by_default()
    {
        // given
        var resource = new CountingResource();
        _server = new HeadlessTestServer<Program>(configureTestServices: services =>
            services.AddHeadlessStatefulResource(_ => resource)
        );

        // when
        await _server.InitializeAsync();

        // then
        resource.Setups.Should().Be(1);
        resource.Clears.Should().Be(0);
    }

    private sealed class CountingResource : IStatefulResource
    {
        public int Setups { get; private set; }

        public int Clears { get; private set; }

        public string Type => "Test";

        public string Name => "counting";

        public Task SetupAsync(CancellationToken cancellationToken)
        {
            Setups++;

            return Task.CompletedTask;
        }

        public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClearStateAsync(CancellationToken cancellationToken)
        {
            Clears++;

            return Task.CompletedTask;
        }

        public Task TeardownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
    }
}
