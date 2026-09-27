// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.PushNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Cross-cutting test that a single <c>AddHeadlessPushNotifications</c> call can compose a default service with
/// named instances from different providers — each owning its own keyed backend and options — without DI
/// collisions. Firebase creates its app lazily on first send, so a named Firebase instance resolves offline
/// (no credentials or network) as long as its options validate. APNs likewise opens no connection until the first
/// send, so a named APNs instance with a locally generated key resolves offline too.
/// </summary>
public sealed class CrossProviderPushNotificationsMixingTests
{
    [Fact]
    public void should_register_default_and_heterogeneous_named_providers_without_collision()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportPkcs8PrivateKeyPem();

        // when - a default Noop plus named Noop, Firebase, and APNs instances, all in a single call.
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", static instance => instance.UseNoop());
            setup.AddNamed("marketing", static instance => instance.UseFirebase(static o => o.Json = "{}"));
            setup.AddNamed(
                "ios",
                instance =>
                    instance.UseApns(o =>
                    {
                        o.KeyId = "ABC123DEFG";
                        o.TeamId = "TEAM123456";
                        o.PrivateKey = privateKey;
                        o.BundleId = "com.example.app";
                    })
            );
        });
        using var provider = services.BuildServiceProvider();

        // then - the default and every named service resolve to their own provider type. Service types are
        // internal to their packages, so assert by runtime type name.
        var serviceProvider = provider.GetRequiredService<IPushNotificationServiceProvider>();
        provider
            .GetRequiredService<IPushNotificationService>()
            .GetType()
            .Name.Should()
            .Be("NoopPushNotificationService");
        serviceProvider.GetService("audit").GetType().Name.Should().Be("NoopPushNotificationService");
        serviceProvider.GetService("marketing").GetType().Name.Should().Be("FcmPushNotificationService");
        serviceProvider.GetService("ios").GetType().Name.Should().Be("ApnsPushNotificationService");

        // keyed resolution stays in sync with the factory for every name.
        foreach (var name in (string[])["audit", "marketing", "ios"])
        {
            provider
                .GetRequiredKeyedService<IPushNotificationService>(name)
                .Should()
                .BeSameAs(serviceProvider.GetService(name));
        }

        serviceProvider.RegisteredNames.Should().BeEquivalentTo(["audit", "marketing", "ios"]);
    }
}
