// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Keys;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;
using Testcontainers.Xunit;

namespace Tests;

/// <summary>
/// One Lowkey Vault container (a community Azure Key Vault API emulator, MIT) per collection; each test creates its
/// own key.
/// </summary>
/// <remarks>
/// The image is pinned here rather than in the shared <c>TestImages</c>: only this suite uses it, and changing the
/// shared Testcontainers package would put every integration suite that references it in this change's scope.
/// </remarks>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class LowkeyVaultFixture()
    : ContainerFixture<ContainerBuilder, IContainer>(TestContextMessageSink.Instance),
        ICollectionFixture<LowkeyVaultFixture>
{
    // Pinned by digest: the image has a single human maintainer, so a moved tag must not reach the tests unreviewed.
    private const string _Image =
        "nagyesta/lowkey-vault:7.3.112@sha256:2636ad677e0e9b98f16b82f36dae16748bcb0d3eee7c9ac6d357f9e9a233be3a";

    private const int _HttpsPort = 8443;

    // One handler for every client: it accepts the emulator's self-signed certificate and lives as long as the tests.
    private static readonly HttpClientHandler _EmulatorHandler = new()
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    };

    /// <summary>Gets the URI of the emulator's default vault, on the port mapped to the host.</summary>
    public Uri VaultUri =>
        new(
            $"https://{(Container.Hostname is "127.0.0.1" ? "localhost" : Container.Hostname)}:{Container.GetMappedPublicPort(_HttpsPort)}"
        );

    public KeyClient CreateKeyClient()
    {
        // The emulator takes any bearer token and serves a self-signed certificate.
        var options = new KeyClientOptions(KeyClientOptions.ServiceVersion.V7_6)
        {
            DisableChallengeResourceVerification = true,
            Transport = new HttpClientTransport(_EmulatorHandler),
        };

        return new KeyClient(VaultUri, new NoopCredential(), options);
    }

    public async Task<KeyVaultKey> CreateKeyAsync(string name, CancellationToken cancellationToken)
    {
        var options = new CreateRsaKeyOptions(name)
        {
            KeySize = 2048,
            // Key Vault needs only wrapKey and unwrapKey; the emulator also checks decrypt before it unwraps.
            KeyOperations =
            {
                KeyOperation.WrapKey,
                KeyOperation.UnwrapKey,
                KeyOperation.Encrypt,
                KeyOperation.Decrypt,
            },
        };

        return await CreateKeyClient().CreateRsaKeyAsync(options, cancellationToken);
    }

    protected override ContainerBuilder Configure()
    {
        // Relaxed ports let the default vault answer on whatever host port Testcontainers maps.
        return base.Configure()
            .WithImage(_Image)
            .WithPortBinding(_HttpsPort, assignRandomHostPort: true)
            .WithEnvironment("LOWKEY_ARGS", "--LOWKEY_VAULT_RELAXED_PORTS=true")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Started LowkeyVaultApp"));
    }

    private sealed class NoopCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("emulator", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
