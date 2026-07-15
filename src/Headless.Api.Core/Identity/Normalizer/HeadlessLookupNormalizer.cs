// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Text;
using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.Normalizer;

/// <summary>
/// <see cref="ILookupNormalizer"/> that delegates to <c>Headless.Text.LookupNormalizer</c>
/// for consistent username and email normalization across the framework.
/// Register via <c>builder.AddUserStore&lt;…&gt;().AddLookupNormalizer&lt;HeadlessLookupNormalizer&gt;()</c>.
/// </summary>
[PublicAPI]
public sealed class HeadlessLookupNormalizer : ILookupNormalizer
{
    /// <summary>Normalizes a username for lookup (e.g. upper-case, Unicode fold).</summary>
    /// <param name="name">The username to normalize, or <see langword="null"/>.</param>
    /// <returns>The normalized username, or <see langword="null"/> if <paramref name="name"/> is <see langword="null"/>.</returns>
    public string? NormalizeName(string? name)
    {
        return LookupNormalizer.NormalizeUserName(name);
    }

    /// <summary>Normalizes an email address for lookup (e.g. upper-case, Unicode fold).</summary>
    /// <param name="email">The email address to normalize, or <see langword="null"/>.</param>
    /// <returns>The normalized email, or <see langword="null"/> if <paramref name="email"/> is <see langword="null"/>.</returns>
    public string? NormalizeEmail(string? email)
    {
        return LookupNormalizer.NormalizeEmail(email);
    }
}
