// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;

namespace Headless.Dashboard;

/// <summary>Builds the shell document's per-response nonce and its Content Security Policy.</summary>
/// <remarks>
/// Scripts load only through the nonce with <c>'strict-dynamic'</c>: the browser then ignores <c>'self'</c> and any
/// host-added script source for parser-inserted scripts, and trusts code a nonced script loads through
/// <c>import()</c>. <c>base-uri 'none'</c> makes a <c>&lt;base&gt;</c> element inert, so the document emits none and
/// every URL it carries is an absolute path.
/// </remarks>
internal static class ContentSecurityPolicy
{
    /// <summary>Returns a fresh nonce: 128 random bits, base64-encoded as the CSP grammar requires.</summary>
    public static string CreateNonce()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToBase64String(bytes);
    }

    /// <summary>Builds the policy for one document response.</summary>
    /// <param name="nonce">
    /// The response's nonce, carried by every script element the document emits and by the style elements its UI
    /// library inserts.
    /// </param>
    /// <param name="host">The sources the host adds.</param>
    /// <exception cref="InvalidOperationException">
    /// A host source is empty or contains white space, a control character, a semicolon, or a comma, which would end
    /// or split a directive.
    /// </exception>
    public static string Build(string nonce, DashboardContentSecurityPolicy host)
    {
        var policy = new StringBuilder(256);

        policy.Append("default-src 'self'; script-src 'nonce-").Append(nonce).Append("' 'strict-dynamic'");
        _AppendSources(policy, host.ScriptSources, "script-src");
        policy.Append("; style-src 'self' 'nonce-").Append(nonce).Append('\'');
        _AppendSources(policy, host.StyleSources, "style-src");
        policy.Append("; img-src 'self' data:");
        _AppendSources(policy, host.ImageSources, "img-src");
        policy.Append("; font-src 'self'");
        _AppendSources(policy, host.FontSources, "font-src");
        policy.Append("; connect-src 'self'");
        _AppendSources(policy, host.ConnectSources, "connect-src");
        policy.Append("; frame-ancestors 'none'; base-uri 'none'; object-src 'none'");

        return policy.ToString();
    }

    private static void _AppendSources(StringBuilder policy, IList<string> sources, string directive)
    {
        foreach (var source in sources)
        {
            // A host source is configuration, but one with a separator would inject a directive of its own (or, with
            // a line break, make the header invalid), so it is refused instead of written.
            if (
                string.IsNullOrEmpty(source)
                || source.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ';' or ',')
            )
            {
                throw new InvalidOperationException(
                    $"The dashboard's Content Security Policy source '{source}' for {directive} is not a single "
                        + "source expression. Pass each source, such as \"https://cdn.example.com\", on its own."
                );
            }

            policy.Append(' ').Append(source);
        }
    }
}
