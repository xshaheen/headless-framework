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
    /// The call may come before or after the host's <c>AddHeadlessMessaging</c>, and it stays inert in a host without
    /// messaging. It follows the same rules as <c>Message&lt;T&gt;</c>: identical declarations of one type merge, from
    /// this method or from <c>ConfigureMessaging</c>, and a declaration that differs fails naming both. The name and
    /// version are validated against the messaging rules once messaging is registered.
    /// </remarks>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <param name="name">The stable logical message name, for example <c>orders.placed</c>.</param>
    /// <param name="version">The contract schema version, compared ordinally.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="version"/> is not valid.</exception>
    /// <exception cref="InvalidOperationException">
    /// Messaging is registered and the declaration conflicts with an earlier one for the same type.
    /// </exception>
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

        var declaration = new MessageContractDeclaration(typeof(TMessage), name, version);
        services.AddSingleton(declaration);

        // Messaging installs the observer the first time it touches the collection and applies every declaration
        // recorded before then itself, so a declaration made after that point reaches it here. Applying it now, rather
        // than when the provider is built, keeps a publish that runs before startup on the declared name.
        if (
            services
                .FirstOrDefault(static descriptor =>
                    descriptor.ServiceType == typeof(MessageContractDeclarationObserver)
                )
                ?.ImplementationInstance
            is MessageContractDeclarationObserver observer
        )
        {
            observer.OnDeclared(declaration);
        }

        return services;
    }
}

/// <summary>One <see cref="MessageContractRegistrationExtensions.AddMessageContract{TMessage}"/> call.</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Name">The logical message name.</param>
/// <param name="Version">The contract schema version.</param>
internal sealed record MessageContractDeclaration(Type MessageType, string Name, string Version);

/// <summary>Receives the declarations made after messaging first touched the service collection.</summary>
/// <param name="onDeclared">Applies one declaration as a message contract.</param>
internal sealed class MessageContractDeclarationObserver(Action<MessageContractDeclaration> onDeclared)
{
    public void OnDeclared(MessageContractDeclaration declaration) => onDeclared(declaration);
}
