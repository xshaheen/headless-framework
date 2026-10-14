// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Testing;

/// <summary>Extension methods for <see cref="MessagingTestHarness"/>.</summary>
[PublicAPI]
public static class MessagingTestHarnessExtensions
{
    /// <summary>
    /// Gets the singleton <see cref="TestConsumer{TMessage}"/> registered for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The message type the consumer handles.</typeparam>
    /// <param name="harness">The messaging test harness.</param>
    /// <returns>The registered <see cref="TestConsumer{TMessage}"/> singleton instance.</returns>
    /// <remarks>
    /// To use this method, register the consumer as a singleton before calling
    /// <see cref="MessagingTestHarness.CreateAsync"/> — for example:
    /// <code>
    /// services.AddSingleton&lt;TestConsumer&lt;MyMessage&gt;&gt;();
    /// setup.Subscribe&lt;TestConsumer&lt;MyMessage&gt;&gt;("my-message-name");
    /// </code>
    /// </remarks>
    public static TestConsumer<TMessage> GetTestConsumer<TMessage>(this MessagingTestHarness harness)
        where TMessage : class
    {
        return harness.GetRequiredService<TestConsumer<TMessage>>();
    }

    /// <summary>
    /// Registers the messaging test harness recording infrastructure into an existing
    /// <see cref="IServiceCollection"/>. Use this when the application host owns the
    /// <see cref="IServiceProvider"/> lifecycle (e.g. <c>WebApplicationFactory</c>, <c>IHost</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this <strong>after</strong> <c>AddHeadlessMessaging</c> so that the transport and
    /// pipeline registrations exist to be decorated. The host is responsible for bootstrapping
    /// and disposal — the harness resolved from DI will <strong>not</strong> dispose the container.
    /// </para>
    /// <para>
    /// <strong>Example with WebApplicationFactory:</strong>
    /// <code>
    /// var factory = new WebApplicationFactory&lt;Program&gt;()
    ///     .WithWebHostBuilder(b =&gt; b.ConfigureTestServices(services =&gt;
    ///     {
    ///         services.AddMessagingTestHarness();
    ///     }));
    ///
    /// var client = factory.CreateClient();
    /// var harness = factory.Services.GetRequiredService&lt;MessagingTestHarness&gt;();
    ///
    /// await client.PostAsJsonAsync("/orders", new { Id = "ORD-1" });
    /// await harness.WaitForConsumedAsync&lt;OrderCreated&gt;(TimeSpan.FromSeconds(5));
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to decorate.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddMessagingTestHarness(this IServiceCollection services)
    {
        MessagingTestHarness.ConfigureServices(services);
        return services;
    }

    /// <summary>
    /// Resets the <see cref="MessagingTestHarness"/> registered in <paramref name="services"/>, after its in-flight
    /// publish and consume work has settled (see <see cref="MessagingTestHarness.ResetAsync"/>). Does nothing when
    /// no harness is registered, so a shared test-state reset can call it for hosts with and without messaging.
    /// </summary>
    /// <remarks>
    /// Call it from a fixture's per-test reset against the host's root provider, for example
    /// <c>await server.Services.ResetMessagingHarnessAsync(cancellationToken)</c>.
    /// </remarks>
    /// <param name="services">The host's service provider.</param>
    /// <param name="cancellationToken">Cancels the wait for in-flight work.</param>
    /// <returns>A task that completes when the harness is reset, or immediately when none is registered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="TimeoutException">In-flight work did not settle within <see cref="MessagingTestHarness.DefaultTimeout"/>.</exception>
    public static Task ResetMessagingHarnessAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);

        var harness = services.GetService<MessagingTestHarness>();

        return harness?.ResetAsync(cancellationToken: cancellationToken) ?? Task.CompletedTask;
    }
}
