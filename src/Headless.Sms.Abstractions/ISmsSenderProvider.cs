// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>
/// Resolves named <see cref="ISmsSender"/> instances registered through the SMS setup builder, for example
/// <c>setup.AddNamed("otp", i =&gt; i.UseTwilio(…))</c>. The default (unkeyed) sender is resolved directly
/// as <see cref="ISmsSender"/> and is not exposed through this provider.
/// </summary>
[PublicAPI]
public interface ISmsSenderProvider
{
    /// <summary>Gets the SMS sender registered under the specified name.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved SMS sender.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    /// <exception cref="InvalidOperationException">
    /// No sender is registered under <paramref name="name"/>; the thrown message points to
    /// <c>AddNamed</c> and the provider <c>Use*</c> methods.
    /// </exception>
    ISmsSender GetSender(string name);

    /// <summary>Gets the SMS sender registered under the specified name, or <see langword="null"/> when not found.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved SMS sender, or <see langword="null"/> when not registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    ISmsSender? GetSenderOrNull(string name);

    /// <summary>
    /// Gets the names of all registered named SMS sender instances. Use this set to validate an
    /// externally-supplied name before resolving it, rather than probing <see cref="GetSenderOrNull"/> and
    /// handling <see langword="null"/>. The default (unnamed) sender is not included.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }
}
