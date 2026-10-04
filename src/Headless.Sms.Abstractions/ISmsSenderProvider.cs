// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>
/// Resolves named <see cref="ISmsSender"/> instances registered by the application.
/// </summary>
[PublicAPI]
public interface ISmsSenderProvider
{
    /// <summary>Gets the SMS sender registered under the specified name.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved SMS sender.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// No sender is registered under <paramref name="name"/>.
    /// </exception>
    ISmsSender GetSender(string name);

    /// <summary>Gets the SMS sender registered under the specified name, or <see langword="null"/> when not found.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved SMS sender, or <see langword="null"/> when not found.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    ISmsSender? GetSenderOrNull(string name);

    /// <summary>
    /// Gets the names of all registered named SMS sender instances.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }
}
