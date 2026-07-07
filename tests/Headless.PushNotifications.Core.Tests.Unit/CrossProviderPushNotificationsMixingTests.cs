// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Cross-cutting test that a single <c>AddHeadlessPushNotifications</c> call can compose a default service with
/// named instances from different providers — each owning its own keyed backend and options — without DI
/// collisions. Firebase creates its app lazily on first send, so a named Firebase instance resolves offline
/// (no credentials or network) as long as its options validate.
/// </summary>
public sealed class CrossProviderPushNotificationsMixingTests
{
    [Fact]
    public void should_register_default_and_heterogeneous_named_providers_without_collision()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when - a default Noop plus named Noop and Firebase instances, all in a single call.
        services.AddHeadlessPushNotifications(static setup =>
        {
            setup.UseNoop();
            setup.AddNamed("audit", static instance => instance.UseNoop());
            setup.AddNamed("marketing", static instance => instance.UseFirebase(static o => o.Json = "{}"));
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

        // keyed resolution stays in sync with the factory for every name.
        foreach (var name in (string[])["audit", "marketing"])
        {
            provider
                .GetRequiredKeyedService<IPushNotificationService>(name)
                .Should()
                .BeSameAs(serviceProvider.GetService(name));
        }

        serviceProvider.RegisteredNames.Should().BeEquivalentTo(["audit", "marketing"]);
    }
}
