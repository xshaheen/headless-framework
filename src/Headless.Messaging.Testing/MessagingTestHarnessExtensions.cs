// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Testing;

/// <summary>Extension methods for <see cref="MessagingTestHarness"/>.</summary>
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
    /// setup.Subscribe&lt;TestConsumer&lt;MyMessage&gt;&gt;("my-topic");
    /// </code>
    /// </remarks>
    public static TestConsumer<TMessage> GetTestConsumer<TMessage>(this MessagingTestHarness harness)
        where TMessage : class => harness.GetRequiredService<TestConsumer<TMessage>>();

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
    /// await harness.WaitForConsumed&lt;OrderCreated&gt;(TimeSpan.FromSeconds(5));
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
}
