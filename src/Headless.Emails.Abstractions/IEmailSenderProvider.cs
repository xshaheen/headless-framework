// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Resolves <see cref="IEmailSender"/> instances registered under a name through the setup builder — for
/// example <c>setup.AddNamed("marketing", i =&gt; i.UseAwsSes(…))</c>. The default (unkeyed) sender is
/// resolved directly as <see cref="IEmailSender"/> and is not exposed through this provider.
/// </summary>
[PublicAPI]
public interface IEmailSenderProvider
{
    /// <summary>Gets the email sender registered under <paramref name="name"/>.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved email sender.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no sender is registered under <paramref name="name"/>.</exception>
    IEmailSender GetSender(string name);

    /// <summary>Gets the email sender registered under <paramref name="name"/>, or <see langword="null"/> when none is registered.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved email sender, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    IEmailSender? GetSenderOrNull(string name);

    /// <summary>
    /// Gets the names of all registered named email sender instances. Use this to validate an
    /// externally-supplied name before resolving it, rather than probing <see cref="GetSenderOrNull"/> and
    /// handling <see langword="null"/>. The default (unnamed) sender is not included.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }
}
