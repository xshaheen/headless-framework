// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;

namespace Headless.Api;

/// <summary>
/// Maps an application's own exceptions and <see cref="ApiResultError"/> types to problem-details responses, so
/// <see cref="HeadlessApiExceptionHandler"/>, the <c>ApiResult</c> conversions, and the OpenAPI document all give
/// them the same status. Configure it through <c>AddHeadlessProblemDetails(problems =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A mapped failure answers with the registered status and the shared Headless problem shape. A described exception
/// and a mapped error carry their <see cref="ErrorDescriptor"/> in the <c>error</c> extension and its localized
/// description as the <c>detail</c>; an exception's own message never reaches the response.
/// </para>
/// <para>
/// A mapping applies to the registered type and every type derived from it; the most derived registration wins, and
/// registering a type again replaces its mapping. The framework's own failures keep their mappings: registering a
/// type the framework already maps, or one derived from it, throws.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class HeadlessProblemDetailsOptions
{
    // The exceptions HeadlessApiExceptionHandler maps before it reads these options, so a registration for one of
    // them (or a type derived from one) could never take effect.
    private static readonly Type[] _FrameworkExceptions =
    [
        typeof(OperationCanceledException),
        typeof(MissingTenantContextException),
        typeof(UnauthorizedException),
        typeof(ForbiddenException),
        typeof(ConflictException),
        typeof(TooManyRequestsException),
        typeof(ValidationException),
        typeof(EntityNotFoundException),
        typeof(CrossTenantWriteException),
        typeof(TimeoutException),
        typeof(NotImplementedException),
        typeof(BadHttpRequestException),
    ];

    // Matched by full name in the handler, because this package references neither EF Core nor the lock package.
    private static readonly string[] _FrameworkExceptionNames =
    [
        "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException",
        "Headless.DistributedLocks.LockAcquisitionTimeoutException",
    ];

    private static readonly Type[] _FrameworkErrors =
    [
        typeof(NotFoundError),
        typeof(ValidationError),
        typeof(ForbiddenError),
        typeof(UnauthorizedError),
        typeof(ConflictError),
        typeof(AggregateError),
    ];

    private readonly Dictionary<Type, ProblemMapping> _exceptions = [];
    private readonly Dictionary<Type, int> _errors = [];

    /// <summary>
    /// Answers <typeparamref name="TException"/>, and every exception derived from it, with
    /// <paramref name="statusCode"/>.
    /// </summary>
    /// <typeparam name="TException">The exception type to map.</typeparam>
    /// <param name="statusCode">The response status, from 400 to 599.</param>
    /// <param name="describe">
    /// Returns the descriptor the body carries, or <see langword="null"/> for a body with only the standard fields.
    /// Omit it to write no descriptor.
    /// </param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statusCode"/> is outside 400 to 599.</exception>
    /// <exception cref="InvalidOperationException">The framework already maps <typeparamref name="TException"/>.</exception>
    public HeadlessProblemDetailsOptions MapException<TException>(
        int statusCode,
        Func<TException, ErrorDescriptor?>? describe = null
    )
        where TException : Exception
    {
        _EnsureErrorStatus(statusCode);

        var type = typeof(TException);
        if (_IsFrameworkException(type))
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' is a failure the framework already maps, so a mapping for it would never apply."
            );
        }

        _exceptions[type] = new ProblemMapping(
            statusCode,
            describe is null ? null : exception => describe((TException)exception)
        );

        return this;
    }

    /// <summary>
    /// Answers <typeparamref name="TError"/>, and every error derived from it, with <paramref name="statusCode"/>
    /// instead of the default 409. The body carries the error's <see cref="ApiResultError.ToErrorDescriptor"/>.
    /// </summary>
    /// <typeparam name="TError">The error type to map.</typeparam>
    /// <param name="statusCode">The response status, from 400 to 599.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statusCode"/> is outside 400 to 599.</exception>
    /// <exception cref="InvalidOperationException">The framework already maps <typeparamref name="TError"/>.</exception>
    public HeadlessProblemDetailsOptions MapError<TError>(int statusCode)
        where TError : ApiResultError
    {
        _EnsureErrorStatus(statusCode);

        var type = typeof(TError);
        if (Array.Exists(_FrameworkErrors, known => known.IsAssignableFrom(type)))
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' is an error the framework already maps, so a mapping for it would never apply."
            );
        }

        _errors[type] = statusCode;

        return this;
    }

    /// <summary>Every status a mapping answers with, so a document generator can list them on each operation.</summary>
    public IReadOnlySet<int> MappedStatusCodes =>
        _exceptions.Values.Select(x => x.StatusCode).Concat(_errors.Values).ToHashSet();

    internal bool TryGetMapping(Exception exception, out ProblemMapping mapping)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (_exceptions.TryGetValue(type, out mapping!))
            {
                return true;
            }
        }

        mapping = null!;
        return false;
    }

    internal bool TryGetStatusCode(ApiResultError error, out int statusCode)
    {
        for (var type = error.GetType(); type is not null; type = type.BaseType)
        {
            if (_errors.TryGetValue(type, out statusCode))
            {
                return true;
            }
        }

        statusCode = 0;
        return false;
    }

    private static bool _IsFrameworkException(Type type)
    {
        if (Array.Exists(_FrameworkExceptions, known => known.IsAssignableFrom(type)))
        {
            return true;
        }

        for (var current = type; current is not null && current != typeof(Exception); current = current.BaseType)
        {
            if (
                Array.Exists(
                    _FrameworkExceptionNames,
                    name => string.Equals(current.FullName, name, StringComparison.Ordinal)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static void _EnsureErrorStatus(int statusCode)
    {
        Argument.IsInclusiveBetween(statusCode, 400, 599);
    }

    internal sealed record ProblemMapping(int StatusCode, Func<Exception, ErrorDescriptor?>? Describe);
}
