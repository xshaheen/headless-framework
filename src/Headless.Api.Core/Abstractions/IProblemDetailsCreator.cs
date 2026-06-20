// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace Headless.Api.Abstractions;

/// <summary>
/// Builds normalized <see cref="ProblemDetails"/> for the framework's standard error responses
/// (RFC 7807). Each factory stamps a stable <c>Title</c>/<c>Type</c> from
/// <see cref="HeadlessProblemDetailsConstants"/> and runs the result through <see cref="Normalize"/>,
/// so callers get a consistent wire shape regardless of where the error originated (exception
/// handlers, middleware, endpoint code).
/// </summary>
/// <remarks>
/// Most factories accept an optional <see cref="ErrorDescriptor"/> that is written to
/// <c>Extensions["error"]</c> as a machine-readable discriminator. Clients should branch on that
/// code rather than parse the human-readable <c>Detail</c>.
/// </remarks>
public interface IProblemDetailsCreator
{
    /// <summary>
    /// Builds a normalized 404 <see cref="ProblemDetails"/> for unmatched routes (typically emitted
    /// by <c>StatusCodesRewriterMiddleware</c> when ASP.NET Core's routing produces a bare 404).
    /// The current request path is embedded in <c>Detail</c>.
    /// </summary>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>.
    /// </param>
    ProblemDetails EndpointNotFound(ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 404 <see cref="ProblemDetails"/> for entity-not-found responses
    /// (typically mapped from <see cref="Headless.Exceptions.EntityNotFoundException"/>).
    /// </summary>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>. Omit to
    /// emit a 404 carrying no machine-readable discriminator.
    /// </param>
    /// <returns>
    /// A <see cref="ProblemDetails"/> already passed through <see cref="Normalize"/>. Deliberately
    /// surfaces no entity name or key — those belong in server logs, not the HTTP response.
    /// </returns>
    ProblemDetails EntityNotFound(ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 400 <see cref="ProblemDetails"/>. Callers attach a stable
    /// <see cref="ErrorDescriptor"/> to discriminate specific malformed-request cases.
    /// </summary>
    /// <param name="detail">
    /// Optional detail message. Defaults to the framework's generic malformed-syntax message
    /// when <see langword="null"/>.
    /// </param>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> written to <c>Extensions["error"]</c>. Clients branch
    /// on this to handle specific 400 cases without relying on detail text.
    /// </param>
    ProblemDetails BadRequest(string? detail = null, ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 429 <see cref="ProblemDetails"/> for rate-limited responses.
    /// </summary>
    /// <param name="retryAfterSeconds">
    /// Seconds the client should wait before retrying. Written to <c>Extensions["retryAfter"]</c>.
    /// Callers are responsible for setting the matching <c>Retry-After</c> response header.
    /// </param>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>.
    /// </param>
    ProblemDetails TooManyRequests(int retryAfterSeconds, ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 422 <see cref="ProblemDetails"/> for validation failures (typically
    /// mapped from <see cref="FluentValidation.ValidationException"/>).
    /// </summary>
    /// <param name="errors">
    /// Field-keyed map of validation errors written to <c>Extensions["errors"]</c>. Keys are member
    /// paths (e.g., <c>"email"</c>, <c>"address.city"</c>) and values are the descriptors that
    /// failed for that field.
    /// </param>
    ProblemDetails UnprocessableEntity(Dictionary<string, List<ErrorDescriptor>> errors);

    /// <summary>
    /// Builds a normalized 409 <see cref="ProblemDetails"/> for conflicts (typically mapped from
    /// <see cref="Headless.Exceptions.ConflictException"/>, EF concurrency failures, or duplicate
    /// idempotency keys).
    /// </summary>
    /// <param name="errors">
    /// One or more <see cref="ErrorDescriptor"/>s written to <c>Extensions["errors"]</c>. Clients
    /// branch on the descriptor codes to distinguish concurrency failures from domain conflicts.
    /// </param>
    ProblemDetails Conflict(params IReadOnlyCollection<ErrorDescriptor> errors);

    /// <summary>
    /// Builds a normalized 403 <see cref="ProblemDetails"/> for authorization failures (typically
    /// emitted by <c>StatusCodesRewriterMiddleware</c> when ASP.NET Core's authorization pipeline
    /// produces a bare 403).
    /// </summary>
    /// <param name="detail">
    /// Optional detail message. Defaults to the framework's generic forbidden message when
    /// <see langword="null"/>.
    /// </param>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> written to <c>Extensions["error"]</c>. Use this for a
    /// single stable discriminator such as a tenant-context failure. Omit to emit a 403 carrying no
    /// machine-readable discriminator (the default for opaque permission denials).
    /// </param>
    ProblemDetails Forbidden(string? detail = null, ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 401 <see cref="ProblemDetails"/> for unauthenticated requests (typically
    /// emitted by <c>StatusCodesRewriterMiddleware</c> when the authentication pipeline produces a
    /// bare 401). Callers are responsible for any <c>WWW-Authenticate</c> response header.
    /// </summary>
    /// <param name="error">
    /// Optional <see cref="ErrorDescriptor"/> stamped into <c>Extensions["error"]</c>.
    /// </param>
    ProblemDetails Unauthorized(ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 408 <see cref="ProblemDetails"/> for request-timeout responses
    /// (typically mapped from <see cref="System.TimeoutException"/>).
    /// </summary>
    ProblemDetails RequestTimeout(ErrorDescriptor? error = null);

    /// <summary>
    /// Builds a normalized 501 <see cref="ProblemDetails"/> for unimplemented-functionality
    /// responses (typically mapped from <see cref="System.NotImplementedException"/>).
    /// </summary>
    ProblemDetails NotImplemented(ErrorDescriptor? error = null);

    /// <summary>
    /// Backfills the framework's standard fields on an externally-produced <see cref="ProblemDetails"/>
    /// so empty-body responses written by upstream middleware (e.g., <c>RequestTimeoutsMiddleware</c>
    /// for 408, anything that just sets a 501 status) match the shape produced by the factories on
    /// this interface.
    /// </summary>
    /// <remarks>
    /// Resolves <c>Title</c>/<c>Type</c> from <see cref="Microsoft.AspNetCore.Mvc.ApiBehaviorOptions.ClientErrorMapping"/>,
    /// then fills missing <c>Title</c>/<c>Type</c>/<c>Detail</c> for status codes the framework
    /// cares about (404, 408, 413, 500, 501) from <see cref="HeadlessProblemDetailsConstants"/>.
    /// Always stamps <c>traceId</c>, <c>buildNumber</c>, <c>commitNumber</c>, and <c>timestamp</c>
    /// extensions, plus <c>Instance</c> from the current request path. Idempotent: existing values
    /// are preserved.
    /// </remarks>
    void Normalize(ProblemDetails problemDetails);
}
