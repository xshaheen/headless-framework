// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Testing;

/// <summary>Extension methods for registering test consumers in a <see cref="MessagingTestHarness"/>.</summary>
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
    /// options.Subscribe&lt;TestConsumer&lt;MyMessage&gt;&gt;("my-topic");
    /// </code>
    /// </remarks>
    public static TestConsumer<TMessage> GetTestConsumer<TMessage>(this MessagingTestHarness harness)
        where TMessage : class => harness.GetRequiredService<TestConsumer<TMessage>>();
}
