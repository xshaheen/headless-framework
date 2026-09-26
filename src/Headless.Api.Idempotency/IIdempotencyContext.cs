// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;

namespace Headless.Api.Idempotency;

/// <summary>
/// The admission a request reached its handler under. The idempotency middleware sets it as an
/// <see cref="Microsoft.AspNetCore.Http.HttpContext" /> feature before it calls the handler, and only when the request
/// was admitted; read it with <c>HttpContext.GetIdempotencyContext()</c>.
/// </summary>
/// <remarks>
/// A handler whose writes must commit only while it still owns the key fences them in its own unit of work with
/// <c>unit.Idempotency.FenceAsync(context.Admission)</c>. The middleware completes the admission with the captured
/// response after the handler returns, so a crash between the handler's commit and that completion re-runs the
/// handler on the next retry with <see cref="IsTakeover" /> set.
/// </remarks>
[PublicAPI]
public interface IIdempotencyContext
{
    /// <summary>Gets the raw <c>Idempotency-Key</c> header value the client sent.</summary>
    string HeaderKey { get; }

    /// <summary>
    /// Gets the scope string the key was derived from: the <see cref="IdempotencyOptions.KeyDeriver" /> output, or the
    /// default composition of user, method, path, query, and header key.
    /// </summary>
    string Scope { get; }

    /// <summary>
    /// Gets the idempotency key the durable store admitted: the lowercase SHA-256 hex of <see cref="Scope" />. The
    /// store scopes it by the current tenant.
    /// </summary>
    string Key { get; }

    /// <summary>Gets the admission the request holds.</summary>
    IdempotentAdmission Admission { get; }

    /// <summary>
    /// Gets the admitted attempt's generation, the fencing token the admission holds on its record while the handler
    /// runs. A later attempt of the same key always holds a higher one.
    /// </summary>
    long Generation { get; }

    /// <summary>
    /// Gets whether an earlier attempt with this key was admitted and ended without completing (it crashed or stalled
    /// past its lease). Its partial side effects may exist, so a handler that is not naturally idempotent should
    /// check before redoing them.
    /// </summary>
    bool IsTakeover { get; }
}

internal sealed class IdempotencyContext(string headerKey, string scope, string key, IdempotentAdmission admission)
    : IIdempotencyContext
{
    public string HeaderKey { get; } = headerKey;

    public string Scope { get; } = scope;

    public string Key { get; } = key;

    public IdempotentAdmission Admission { get; } = admission;

    public long Generation { get; } = admission.Generation!.Value;

    public bool IsTakeover => Admission.IsTakeover;
}
