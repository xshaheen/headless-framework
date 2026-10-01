// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

/// <summary>
/// Lets a library declare the contract of a message it publishes or consumes while depending only on the messaging
/// abstractions.
/// </summary>
[PublicAPI]
public static class MessageContractRegistrationExtensions
{
    /// <summary>
    /// Declares the contract of one message type for both lanes: the logical name publishing and consuming resolve the
    /// type to, and its schema version. It makes the same declaration as <c>Message&lt;T&gt;(name, version)</c> in a
    /// <c>ConfigureMessaging</c> contribution, with no correlation or lane settings, available to a library that does
    /// not reference <c>Headless.Messaging.Core</c>.
    /// </summary>
    /// <remarks>
    /// The call only records the declaration, so it may come before or after the host's <c>AddHeadlessMessaging</c>, and
    /// it stays inert in a host without messaging. Messaging folds every declaration once, when the host first needs its
    /// message names: at startup, or at an earlier publish. It follows the same rules as <c>Message&lt;T&gt;</c> there:
    /// identical declarations of one type merge, from this method or from <c>ConfigureMessaging</c>, and a declaration
    /// that differs fails naming both. The name and version are validated against the messaging rules at that point.
    /// </remarks>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <param name="name">The stable logical message name, for example <c>orders.placed</c>.</param>
    /// <param name="version">The contract schema version, compared ordinally.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="version"/> is blank.</exception>
    public static IServiceCollection AddMessageContract<TMessage>(
        this IServiceCollection services,
        string name,
        string version = MessageOptions.InitialContractVersion
    )
        where TMessage : class
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNullOrWhiteSpace(version);

        services.AddSingleton<MessageDeclaration>(new MessageContractDeclaration(typeof(TMessage), name, version));

        return services;
    }
}

/// <summary>
/// One message declaration recorded in a service collection. Messaging folds every declaration, in registration order,
/// when the host's consumer registry freezes, so a declaration counts wherever and whenever it was made.
/// </summary>
/// <param name="MessageType">The declared message type.</param>
internal abstract record MessageDeclaration(Type MessageType);

/// <summary>One <see cref="MessageContractRegistrationExtensions.AddMessageContract{TMessage}"/> call.</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Name">The logical message name.</param>
/// <param name="Version">The contract schema version.</param>
internal sealed record MessageContractDeclaration(Type MessageType, string Name, string Version)
    : MessageDeclaration(MessageType);
