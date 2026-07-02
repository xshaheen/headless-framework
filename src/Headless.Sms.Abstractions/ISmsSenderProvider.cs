// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>
/// Resolves <see cref="ISmsSender"/> instances registered under a name through the setup builder — for
/// example <c>setup.AddNamed("otp", i =&gt; i.UseTwilio(…))</c>. The default (unkeyed) sender is resolved
/// directly as <see cref="ISmsSender"/> and is not exposed through this provider.
/// </summary>
[PublicAPI]
public interface ISmsSenderProvider
{
    /// <summary>Gets the SMS sender registered under <paramref name="name"/>.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved SMS sender.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no sender is registered under <paramref name="name"/>; the message points to
    /// <c>AddNamed</c> and the provider <c>Use*</c> methods.
    /// </exception>
    ISmsSender GetSender(string name);

    /// <summary>Gets the SMS sender registered under <paramref name="name"/>, or <see langword="null"/> when none is registered.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved SMS sender, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    ISmsSender? GetSenderOrNull(string name);
}
