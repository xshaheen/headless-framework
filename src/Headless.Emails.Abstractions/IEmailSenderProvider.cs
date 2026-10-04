// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Resolves named <see cref="IEmailSender"/> instances registered through the email setup builder, for example
/// <c>setup.AddNamed("marketing", i =&gt; i.UseAwsSes(…))</c>. The default (unkeyed) sender is resolved directly
/// as <see cref="IEmailSender"/> and is not exposed through this provider.
/// </summary>
[PublicAPI]
public interface IEmailSenderProvider
{
    /// <summary>Gets the email sender registered under the specified name.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved email sender instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    /// <exception cref="InvalidOperationException">No sender is registered under <paramref name="name"/>.</exception>
    IEmailSender GetSender(string name);

    /// <summary>Gets the email sender registered under the specified name, or <see langword="null"/> when not found.</summary>
    /// <param name="name">The sender instance name.</param>
    /// <returns>The resolved email sender instance, or <see langword="null"/> when not registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains only white space.</exception>
    IEmailSender? GetSenderOrNull(string name);

    /// <summary>
    /// Gets the names of all registered named email sender instances. Use this set to validate an
    /// externally-supplied name before resolving it, rather than probing <see cref="GetSenderOrNull"/> and
    /// handling <see langword="null"/>. The default (unnamed) sender is not included.
    /// </summary>
    IReadOnlySet<string> RegisteredNames { get; }
}
