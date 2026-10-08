// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Context;
using Headless.Http;
using Headless.Idempotency;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Idempotency;

/// <summary>
/// How a request maps to its stored record, shared by the middleware that admits it and the lookup that reads it back,
/// so both always address the same record.
/// </summary>
internal static class IdempotencyRequestScope
{
    /// <summary>
    /// Builds the scope a request's record is stored under, or an empty scope when the request runs without
    /// idempotency (a <see cref="IdempotencyOptions.KeyDeriver" /> returned one, or
    /// <see cref="IdempotencyOptions.RequireUserIdentity" /> refuses an anonymous caller).
    /// </summary>
    public static string Build(
        HttpContext context,
        IdempotencyOptions options,
        ICurrentUser currentUser,
        string method,
        string path,
        string query,
        string key
    )
    {
        if (options.KeyDeriver != null)
        {
            return options.KeyDeriver(context, key);
        }

        var user = (string?)currentUser.UserId;

        // RequireUserIdentity (default) refuses requests without a user, so two anonymous callers cannot
        // cross-replay each other's responses on a shared Idempotency-Key. Operators with intentional anonymous
        // flows (webhook receivers, OAuth callbacks) set RequireUserIdentity=false and accept that every anonymous
        // caller shares one namespace, or configure KeyDeriver with a verified per-caller discriminator.
        if (options.RequireUserIdentity && string.IsNullOrEmpty(user))
        {
            return string.Empty;
        }

        // QueryString participates so endpoints that branch on query parameters (e.g., ?action=void vs
        // ?action=capture, ?dry_run=true vs ?dry_run=false) don't cross-replay when the client reuses the same
        // idempotency key. The tenant is not part of the scope: the store keys every record by the request's store
        // tenant. Anonymous-user fallback uses an empty segment rather than a literal "anon" so a real UserId equal to
        // the string "anon" cannot collide with the anonymous bucket.
        return $"idem:{user ?? string.Empty}:{CanonicalMethod(method)}:{path}{query}:{key}";
    }

    /// <summary>
    /// Returns the tenant the store keys a request's record under: the authenticated principal's tenant claim, or
    /// <see langword="null" /> (the host scope) for a principal without one and for anonymous requests.
    /// </summary>
    /// <remarks>
    /// The ambient tenant is deliberately ignored. Pre-authentication resolution (catalog identifiers from the host or a
    /// header) sets it from caller-controlled input, so keying by it would let an anonymous caller pick which tenant's
    /// namespace its key lands in, and pre-seed or replay that tenant's records.
    /// </remarks>
    public static string? StoreTenant(ICurrentUser currentUser, MultiTenancyOptions tenancyOptions)
    {
        return currentUser is { IsAuthenticated: true, Principal: { } principal }
            ? TenantClaimReader.GetTenantId(principal, tenancyOptions)
            : null;
    }

    /// <summary>
    /// Returns the store key for a scope string: the lowercase SHA-256 hex of its UTF-8 bytes. Always 64 characters,
    /// so a long path or a 255-character header key still fits the store's key limit, and the raw scope (which carries
    /// the user id) never reaches the store or the logs.
    /// </summary>
    public static string Hash(string scope)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
    }

    /// <summary>Checks a single key value the way the middleware checks the header: length and control characters.</summary>
    public static bool IsValidKey(string key, out string reason)
    {
        if (key.Length > 255)
        {
            reason = "length-exceeds-255";
            return false;
        }

        foreach (var c in key)
        {
            if (c is <= (char)31 or (char)127)
            {
                reason = "control-character";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    public static string CanonicalMethod(string method)
    {
        return method switch
        {
            _ when HttpMethods.IsPost(method) => HttpMethods.Post,
            _ when HttpMethods.IsPut(method) => HttpMethods.Put,
            _ when HttpMethods.IsPatch(method) => HttpMethods.Patch,
            _ when HttpMethods.IsDelete(method) => HttpMethods.Delete,
            _ when HttpMethods.IsGet(method) => HttpMethods.Get,
            _ when HttpMethods.IsHead(method) => HttpMethods.Head,
            _ when HttpMethods.IsOptions(method) => HttpMethods.Options,
            _ when HttpMethods.IsTrace(method) => HttpMethods.Trace,
            _ when HttpMethods.IsConnect(method) => HttpMethods.Connect,
            _ => method.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Writes a stored response to the current response, replaying status code, allowlisted headers, and body exactly
    /// as originally captured, and sets the <c>Idempotent-Replayed: true</c> response header.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The response has already started, which means <c>UseHeadlessHttpIdempotency()</c> runs after middleware that
    /// already wrote to the response body, or the stored response is empty.
    /// </exception>
    public static async Task ReplayAsync(
        HttpContext context,
        IdempotentResult result,
        ISet<string> replayHeaderAllowlist,
        CancellationToken cancellationToken
    )
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException(
                "Cannot replay idempotent response: HttpResponse has already started. "
                    + "Ensure UseHeadlessHttpIdempotency() is registered before any middleware that writes to the response body."
            );
        }

        var snapshot =
            result.Deserialize(IdempotencyJsonContext.Default.IdempotencyResponseSnapshot)
            ?? throw new InvalidOperationException("The stored idempotent response is empty.");

        context.Response.StatusCode = snapshot.StatusCode;

        // Strip pre-existing allowlisted headers set by upstream middleware so byte-equivalent replay isn't poisoned
        // by per-request mutations (CORS, security policies). Headers outside the allowlist (e.g., traceparent from
        // logging) remain untouched.
        foreach (var allowedHeader in replayHeaderAllowlist)
        {
            context.Response.Headers.Remove(allowedHeader);
        }

        foreach (var (name, values) in snapshot.Headers)
        {
            if (replayHeaderAllowlist.Contains(name))
            {
                context.Response.Headers[name] = values;
            }
        }

        context.Response.Headers[HttpHeaderNames.IdempotentReplayed] = "true";

        if (snapshot.Body.Length > 0)
        {
            context.Response.ContentLength = snapshot.Body.Length;
            await context.Response.Body.WriteAsync(snapshot.Body, cancellationToken).ConfigureAwait(false);
        }
    }
}
