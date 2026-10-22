// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class HeadlessTestServerDeployStepsTests : TestBase
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
    public async Task should_keep_the_provision_and_purge_action_the_application_registered()
    {
        // given — the application's own service registration asks for ProvisionAndPurge, as Program.cs would
        var resource = new CountingProvisioner();
        _server = new HeadlessTestServer<Program>(configureWebHost: builder =>
            builder.ConfigureServices(services =>
            {
                services.AddResourceProvisioner(_ => resource);
                services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.ProvisionAndPurge);
            })
        );

        // when
        await _server.InitializeAsync();

        // then
        resource.Provisions.Should().Be(1);
        resource
            .Purges.Should()
            .Be(1, "the test server's default must not replace the application's ProvisionAndPurge");
    }

    [Fact]
    public async Task should_only_provision_resources_by_default()
    {
        // given
        var resource = new CountingProvisioner();
        _server = new HeadlessTestServer<Program>(configureTestServices: services =>
            services.AddResourceProvisioner(_ => resource)
        );

        // when
        await _server.InitializeAsync();

        // then
        resource.Provisions.Should().Be(1);
        resource.Purges.Should().Be(0);
    }

    private sealed class CountingProvisioner : IResourceProvisioner
    {
        public int Provisions { get; private set; }

        public int Purges { get; private set; }

        public string Kind => "Test";

        public string Name => "counting";

        public Task ProvisionAsync(CancellationToken cancellationToken)
        {
            Provisions++;

            return Task.CompletedTask;
        }

        public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PurgeAsync(CancellationToken cancellationToken)
        {
            Purges++;

            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
    }
}
