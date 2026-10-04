// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Represents the outcome of an operation with no return value.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA2225 // Operator overloads have named alternates
public readonly struct ApiResult : IEquatable<ApiResult>
{
    private const byte _SuccessState = 1;
    private const byte _FailureState = 2;
    private static readonly ApiResult _Success = new(SuccessState.Value);
    private readonly byte _state;
    private readonly ApiResultError? _error;

    private ApiResult(SuccessState _)
    {
        _state = _SuccessState;
        _error = null;
    }

    private ApiResult(ApiResultError error)
    {
        _state = _FailureState;
        _error = Argument.IsNotNull(error);
    }

    /// <summary><see langword="true"/> if the operation succeeded.</summary>
    public bool IsSuccess => _state == _SuccessState;

    /// <summary><see langword="true"/> if the operation failed with a valid <see cref="Error"/>.</summary>
    public bool IsFailure => _state == _FailureState;

    /// <summary>The error describing the failure.</summary>
    /// <exception cref="InvalidOperationException">
    /// The result is successful or is a default-initialized, uninitialized instance.
    /// </exception>
    public ApiResultError Error =>
        IsFailure
            ? _error!
            : throw new InvalidOperationException(
                IsSuccess
                    ? "Cannot access Error on successful result."
                    : "ApiResult was not properly initialized. Error was accessed on a default instance."
            );

    /// <summary>Tries to get the error without throwing.</summary>
    /// <param name="error">When this method returns <see langword="true"/>, the failure error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([MaybeNullWhen(false)] out ApiResultError error)
    {
        error = IsFailure ? _error : null;
        return IsFailure;
    }

    /// <summary>Invokes <paramref name="success"/> when successful or <paramref name="failure"/> when failed, returning its result.</summary>
    /// <typeparam name="TResult">The type produced by both branches.</typeparam>
    /// <param name="success">The function invoked on success.</param>
    /// <param name="failure">The function invoked on failure, receiving the <see cref="Error"/>.</param>
    /// <returns>The value produced by the invoked branch.</returns>
    public TResult Match<TResult>(Func<TResult> success, Func<ApiResultError, TResult> failure)
    {
        _EnsureInitialized();
        return IsSuccess ? success() : failure(_error!);
    }

    /// <summary>Invokes <paramref name="action"/> when the result is a success, then returns this result.</summary>
    /// <param name="action">The action to run on success.</param>
    /// <returns>This result, to allow chaining.</returns>
    public ApiResult OnSuccess(Action action)
    {
        _EnsureInitialized();

        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Invokes <paramref name="action"/> with the error when the result is a failure, then returns this result.</summary>
    /// <param name="action">The action to run on failure, receiving the <see cref="Error"/>.</param>
    /// <returns>This result, to allow chaining.</returns>
    public ApiResult OnFailure(Action<ApiResultError> action)
    {
        _EnsureInitialized();

        if (IsFailure)
        {
            action(_error!);
        }

        return this;
    }

    // Factory methods

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful <see cref="ApiResult"/>.</returns>
    public static ApiResult Ok()
    {
        return _Success;
    }

    /// <summary>Creates a failed result carrying the supplied error.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static ApiResult Fail(ApiResultError error)
    {
        return new(error);
    }

    // Generic factory methods (type inference)

    /// <summary>Creates a successful <see cref="ApiResult{T}"/> with an inferred value type.</summary>
    /// <typeparam name="T">The success value type, inferred from <paramref name="value"/>.</typeparam>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Ok<T>(T value)
    {
        return ApiResult<T>.Ok(value);
    }

    /// <summary>Creates a failed <see cref="ApiResult{T}"/> with an inferred value type.</summary>
    /// <typeparam name="T">The success value type that the result would otherwise carry.</typeparam>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Fail<T>(ApiResultError error)
    {
        return ApiResult<T>.Fail(error);
    }

    /// <summary>Creates a failed result representing a missing entity.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key or identifier used to look up the entity.</param>
    /// <returns>A failed <see cref="ApiResult"/> containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult NotFound(string entity, string key)
    {
        return Fail(new NotFoundError { Entity = entity, Key = key });
    }

    /// <summary>Creates a failed result representing a missing entity identified by a <see cref="Guid"/>.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key used to look up the entity.</param>
    /// <returns>A failed result containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult NotFound(string entity, Guid key) => NotFound(entity, key.ToString());

    /// <summary>Creates a failed result representing a missing entity identified by an <see cref="int"/>.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key used to look up the entity.</param>
    /// <returns>A failed result containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult NotFound(string entity, int key) =>
        NotFound(entity, key.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a failed result representing a missing entity identified by a <see cref="long"/>.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key used to look up the entity.</param>
    /// <returns>A failed result containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult NotFound(string entity, long key) =>
        NotFound(entity, key.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a failed result representing a conflict.</summary>
    /// <param name="code">A machine-readable code describing the type of conflict.</param>
    /// <param name="message">A human-readable message describing the conflict.</param>
    /// <returns>A failed <see cref="ApiResult"/> containing a <see cref="ConflictError"/>.</returns>
    public static ApiResult Conflict(string code, string message)
    {
        return Fail(new ConflictError(code, message));
    }

    /// <summary>Creates a failed result representing a conflict with the default general error code.</summary>
    /// <param name="message">The human-readable message describing the conflict.</param>
    /// <returns>A failed result containing a <see cref="ConflictError"/>.</returns>
    public static ApiResult Conflict(string message) =>
        Conflict(new ErrorDescriptor(ApiResultErrorCodes.Default, message));

    /// <summary>Creates a failed result representing one conflict descriptor.</summary>
    /// <param name="error">The descriptor describing the conflict.</param>
    /// <returns>A failed result containing the supplied conflict descriptor.</returns>
    public static ApiResult Conflict(ErrorDescriptor error) => Fail(new ConflictError(error));

    /// <summary>Creates a failed result representing one or more conflict descriptors.</summary>
    /// <param name="errors">The descriptors describing all conflicting conditions.</param>
    /// <returns>A failed result containing all supplied conflict descriptors.</returns>
    public static ApiResult Conflict(params IReadOnlyCollection<ErrorDescriptor> errors) =>
        Fail(new ConflictError(errors));

    /// <summary>Creates a failed result representing validation errors.</summary>
    /// <param name="errors">The field/error-message pairs to expose in the 422 ProblemDetails response.</param>
    /// <returns>A failed result containing the supplied validation errors.</returns>
    public static ApiResult ValidationFailed(params (string Field, string Error)[] errors) =>
        Fail(ValidationError.FromFields(errors));

    /// <summary>Creates a failed result from a structured validation-error map.</summary>
    /// <param name="errors">The field-keyed descriptors to expose in the 422 ProblemDetails response.</param>
    /// <returns>A failed result containing the supplied validation descriptors.</returns>
    public static ApiResult ValidationFailed(IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors) =>
        Fail(ValidationError.FromErrorDescriptors(errors));

    /// <summary>Creates a failed result representing a forbidden operation.</summary>
    /// <param name="reason">The reason why the operation is not allowed.</param>
    /// <returns>A failed <see cref="ApiResult"/> containing a <see cref="ForbiddenError"/>.</returns>
    public static ApiResult Forbidden(string reason)
    {
        return Forbidden(new ErrorDescriptor(ApiResultErrorCodes.Forbidden, reason));
    }

    /// <summary>Creates a failed result representing a forbidden operation.</summary>
    /// <param name="error">The descriptor exposed in the 403 ProblemDetails response.</param>
    /// <returns>A failed result containing the supplied forbidden descriptor.</returns>
    public static ApiResult Forbidden(ErrorDescriptor error) => Fail(new ForbiddenError(error));

    /// <summary>Creates a failed result representing an unauthorized operation.</summary>
    /// <returns>A failed <see cref="ApiResult"/> containing an <see cref="UnauthorizedError"/>.</returns>
    public static ApiResult Unauthorized()
    {
        return Fail(UnauthorizedError.Instance);
    }

    /// <summary>Creates a failed result representing an unauthorized operation.</summary>
    /// <param name="error">The descriptor exposed in the 401 ProblemDetails response.</param>
    /// <returns>A failed result containing the supplied unauthorized descriptor.</returns>
    public static ApiResult Unauthorized(ErrorDescriptor error) => Fail(new UnauthorizedError(error));

    /// <summary>Creates a failed result representing an unauthorized operation.</summary>
    /// <param name="message">The human-readable unauthorized condition.</param>
    /// <param name="code">The machine-readable code; defaults to <see cref="ApiResultErrorCodes.Default"/>.</param>
    /// <returns>A failed result containing the supplied unauthorized condition.</returns>
    public static ApiResult Unauthorized(string message, string code = ApiResultErrorCodes.Default) =>
        Unauthorized(new ErrorDescriptor(code, message));

    // Implicit from error
    /// <summary>Implicitly converts a <see cref="ApiResultError"/> into a failed <see cref="ApiResult"/>.</summary>
    /// <param name="error">The error to wrap.</param>
    /// <returns>A failed <see cref="ApiResult"/> carrying <paramref name="error"/>.</returns>
    public static implicit operator ApiResult(ApiResultError error) => Fail(error);

    // Equality
    /// <summary>Determines whether this result equals <paramref name="other"/> in success state and error.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> if both have the same success state and error; otherwise <see langword="false"/>.</returns>
    public bool Equals(ApiResult other)
    {
        return _state == other._state && Equals(_error, other._error);
    }

    /// <summary>Determines whether <paramref name="obj"/> is an <see cref="ApiResult"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="ApiResult"/>; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ApiResult other && Equals(other);
    }

    /// <summary>Returns a hash code derived from the success state and error.</summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(_state, _error);
    }

    /// <summary>Determines whether two <see cref="ApiResult"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ApiResult left, ApiResult right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="ApiResult"/> instances are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ApiResult left, ApiResult right) => !left.Equals(right);

    private void _EnsureInitialized()
    {
        if (_state == 0)
        {
            throw new InvalidOperationException("ApiResult was not properly initialized.");
        }
    }

    private enum SuccessState : byte
    {
        Value,
    }
}
