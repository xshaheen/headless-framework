// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Primitives;

/// <summary>
/// Represents the outcome of an operation that may fail.
/// Success contains a value; failure contains an error.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
#pragma warning disable CA2225 // Operator overloads have named alternates
#pragma warning disable CA1000 // Do not declare static members on generic types
public readonly struct ApiResult<T> : IEquatable<ApiResult<T>>
{
    private const byte _SuccessState = 1;
    private const byte _FailureState = 2;
    private readonly T? _value;
    private readonly ApiResultError? _error;
    private readonly byte _state;

    private ApiResult(T value)
    {
        Argument.IsNotNull(value);
        _value = value;
        _error = null;
        _state = _SuccessState;
    }

    private ApiResult(ApiResultError error)
    {
        _value = default;
        _error = Argument.IsNotNull(error);
        _state = _FailureState;
    }

    /// <summary>True if operation succeeded.</summary>
    public bool IsSuccess => _state == _SuccessState;

    /// <summary>True if operation failed.</summary>
    public bool IsFailure => _state == _FailureState;

    /// <summary>The success value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure (<see cref="IsFailure"/> is <see langword="true"/>).</exception>
    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException(
                IsFailure
                    ? $"Cannot access Value on failed result. Error: {_error}"
                    : "ApiResult<T> was not properly initialized. Value was accessed on a default instance."
            );

    /// <summary>The error describing the failure.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the result is a success (<see cref="IsSuccess"/> is <see langword="true"/>), or when accessed on a
    /// default-initialized instance (which is neither success nor failure).
    /// </exception>
    public ApiResultError Error
    {
        get
        {
            if (IsSuccess)
            {
                throw new InvalidOperationException("Cannot access Error on successful result.");
            }

            // A default(ApiResult<T>) is uninitialized; throw clearly instead of a downstream NRE.
            return _error
                ?? throw new InvalidOperationException(
                    "ApiResult<T> was not properly initialized. Error was accessed on a default instance."
                );
        }
    }

    /// <summary>Tries to get the value without throwing.</summary>
    /// <param name="value">When this method returns <see langword="true"/>, the success value; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a success; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>Tries to get the error without throwing.</summary>
    /// <param name="error">When this method returns <see langword="true"/>, the failure error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
    public bool TryGetError([MaybeNullWhen(false)] out ApiResultError error)
    {
        error = IsFailure ? _error : null;
        return IsFailure;
    }

    /// <summary>Gets the success value, or <paramref name="defaultValue"/> when the result is a failure.</summary>
    /// <param name="defaultValue">The value to return when the result is a failure.</param>
    /// <returns>The success value when successful; otherwise <paramref name="defaultValue"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when called on a default-initialized result.</exception>
    public T GetValueOrDefault(T defaultValue)
    {
        _EnsureInitialized();
        return IsSuccess ? _value! : defaultValue;
    }

    /// <summary>Invokes <paramref name="success"/> with the value or <paramref name="failure"/> with the error, returning its result.</summary>
    /// <typeparam name="TResult">The type produced by both branches.</typeparam>
    /// <param name="success">The function invoked on success, receiving the value.</param>
    /// <param name="failure">The function invoked on failure, receiving the error.</param>
    /// <returns>The value produced by the invoked branch.</returns>
    public TResult Match<TResult>(Func<T, TResult> success, Func<ApiResultError, TResult> failure)
    {
        return IsSuccess ? success(_value!) : failure(Error);
    }

    /// <summary>Transforms the success value with <paramref name="mapper"/>, propagating the error unchanged on failure.</summary>
    /// <typeparam name="TOut">The mapped value type.</typeparam>
    /// <param name="mapper">The projection applied to the success value.</param>
    /// <returns>A successful result holding the mapped value, or the original failure.</returns>
    public ApiResult<TOut> Map<TOut>(Func<T, TOut> mapper)
    {
        _EnsureInitialized();
        return IsSuccess ? ApiResult<TOut>.Ok(mapper(_value!)) : ApiResult<TOut>.Fail(_error!);
    }

    /// <summary>Chains another result-producing operation on success, propagating the error unchanged on failure.</summary>
    /// <typeparam name="TOut">The value type produced by <paramref name="binder"/>.</typeparam>
    /// <param name="binder">The function applied to the success value, producing the next result.</param>
    /// <returns>The result produced by <paramref name="binder"/>, or the original failure.</returns>
    public ApiResult<TOut> Bind<TOut>(Func<T, ApiResult<TOut>> binder)
    {
        _EnsureInitialized();
        return IsSuccess ? binder(_value!) : ApiResult<TOut>.Fail(_error!);
    }

    /// <summary>Invokes <paramref name="action"/> with the value when successful, then returns this result.</summary>
    /// <param name="action">The action to run on success, receiving the value.</param>
    /// <returns>This result, to allow chaining.</returns>
    public ApiResult<T> OnSuccess(Action<T> action)
    {
        _EnsureInitialized();

        if (IsSuccess)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>Invokes <paramref name="action"/> with the error when failed, then returns this result.</summary>
    /// <param name="action">The action to run on failure, receiving the error.</param>
    /// <returns>This result, to allow chaining.</returns>
    public ApiResult<T> OnFailure(Action<ApiResultError> action)
    {
        _EnsureInitialized();

        if (IsFailure)
        {
            action(Error);
        }

        return this;
    }

    // Factory methods

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Ok(T value)
    {
        return new(value);
    }

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult{T}"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static ApiResult<T> Fail(ApiResultError error)
    {
        return new(error);
    }

    // Convenience factories

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing a missing entity.
    /// </summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key or identifier used to look up the entity.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="NotFoundError"/> describing the missing entity.
    /// </returns>
    public static ApiResult<T> NotFound(string entity, string key)
    {
        return new ApiResult<T>(new NotFoundError { Entity = entity, Key = key });
    }

    /// <summary>Creates a failed result representing a missing entity identified by a <see cref="Guid"/>.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key used to look up the entity.</param>
    /// <returns>A failed result containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult<T> NotFound(string entity, Guid key) => NotFound(entity, key.ToString());

    /// <summary>Creates a failed result representing a missing entity identified by an <see cref="int"/>.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key used to look up the entity.</param>
    /// <returns>A failed result containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult<T> NotFound(string entity, int key) =>
        NotFound(entity, key.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a failed result representing a missing entity identified by a <see cref="long"/>.</summary>
    /// <param name="entity">The logical name of the entity that could not be found.</param>
    /// <param name="key">The key used to look up the entity.</param>
    /// <returns>A failed result containing a <see cref="NotFoundError"/>.</returns>
    public static ApiResult<T> NotFound(string entity, long key) =>
        NotFound(entity, key.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing a conflict.
    /// </summary>
    /// <param name="code">A machine-readable code describing the type of conflict.</param>
    /// <param name="message">A human-readable message describing the conflict.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="ConflictError"/> with the provided details.
    /// </returns>
    public static ApiResult<T> Conflict(string code, string message)
    {
        return new ApiResult<T>(new ConflictError(code, message));
    }

    /// <summary>Creates a failed result representing a conflict with the default general error code.</summary>
    /// <param name="message">The human-readable message describing the conflict.</param>
    /// <returns>A failed result containing a <see cref="ConflictError"/>.</returns>
    public static ApiResult<T> Conflict(string message) =>
        Conflict(new ErrorDescriptor(ApiResultErrorCodes.Default, message));

    /// <summary>Creates a failed result representing one conflict descriptor.</summary>
    /// <param name="error">The descriptor describing the conflict.</param>
    /// <returns>A failed result containing the supplied conflict descriptor.</returns>
    public static ApiResult<T> Conflict(ErrorDescriptor error) => new(new ConflictError(error));

    /// <summary>Creates a failed result representing one or more conflict descriptors.</summary>
    /// <param name="errors">The descriptors describing all conflicting conditions.</param>
    /// <returns>A failed result containing all supplied conflict descriptors.</returns>
    public static ApiResult<T> Conflict(params IReadOnlyCollection<ErrorDescriptor> errors) =>
        new(new ConflictError(errors));

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing validation errors.
    /// </summary>
    /// <param name="errors">An array of field-error pairs representing the validation issues.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="ValidationError"/> with the provided field errors.
    /// </returns>
    public static ApiResult<T> ValidationFailed(params (string Field, string Error)[] errors)
    {
        return new ApiResult<T>(ValidationError.FromFields(errors));
    }

    /// <summary>Creates a failed result from a structured validation-error map.</summary>
    /// <param name="errors">The field-keyed descriptors to expose in the 422 ProblemDetails response.</param>
    /// <returns>A failed result containing the supplied validation descriptors.</returns>
    public static ApiResult<T> ValidationFailed(IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors) =>
        new(ValidationError.FromErrorDescriptors(errors));

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing a forbidden operation.
    /// </summary>
    /// <param name="reason">The reason why the operation is not allowed.</param>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing a <see cref="ForbiddenError"/> with the provided reason.
    /// </returns>
    public static ApiResult<T> Forbidden(string reason)
    {
        return Forbidden(new ErrorDescriptor(ApiResultErrorCodes.Forbidden, reason));
    }

    /// <summary>Creates a failed result representing a forbidden operation.</summary>
    /// <param name="error">The descriptor exposed in the 403 ProblemDetails response.</param>
    /// <returns>A failed result containing the supplied forbidden descriptor.</returns>
    public static ApiResult<T> Forbidden(ErrorDescriptor error) => new(new ForbiddenError(error));

    /// <summary>
    /// Creates a failed <see cref="ApiResult{T}"/> representing an unauthorized operation.
    /// </summary>
    /// <returns>
    /// A failed <see cref="ApiResult{T}"/> containing an <see cref="UnauthorizedError"/>.
    /// </returns>
    public static ApiResult<T> Unauthorized()
    {
        return new(UnauthorizedError.Instance);
    }

    /// <summary>Creates a failed result representing an unauthorized operation.</summary>
    /// <param name="error">The descriptor exposed in the 401 ProblemDetails response.</param>
    /// <returns>A failed result containing the supplied unauthorized descriptor.</returns>
    public static ApiResult<T> Unauthorized(ErrorDescriptor error) => new(new UnauthorizedError(error));

    /// <summary>Creates a failed result representing an unauthorized operation.</summary>
    /// <param name="message">The human-readable unauthorized condition.</param>
    /// <param name="code">The machine-readable code; defaults to <see cref="ApiResultErrorCodes.Default"/>.</param>
    /// <returns>A failed result containing the supplied unauthorized condition.</returns>
    public static ApiResult<T> Unauthorized(string message, string code = ApiResultErrorCodes.Default) =>
        Unauthorized(new ErrorDescriptor(code, message));

    // Implicit conversions

    /// <summary>Implicitly wraps a value in a successful <see cref="ApiResult{T}"/>.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="ApiResult{T}"/> carrying <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator ApiResult<T>(T value) => Ok(value);

    /// <summary>Implicitly wraps an error in a failed <see cref="ApiResult{T}"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult{T}"/> carrying <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator ApiResult<T>(ApiResultError error) => Fail(error);

    /// <summary>Converts to the non-generic <see cref="ApiResult"/> (discards the value, keeps the success/error state).</summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>An <see cref="ApiResult"/> with the same success/error state.</returns>
    public static implicit operator ApiResult(ApiResult<T> result)
    {
        return result.IsSuccess ? ApiResult.Ok() : ApiResult.Fail(result.Error);
    }

    // Equality

    /// <summary>Determines whether this result equals <paramref name="other"/> in success state, value, and error.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><see langword="true"/> if both have the same success state, value, and error; otherwise <see langword="false"/>.</returns>
    public bool Equals(ApiResult<T> other)
    {
        return _state == other._state
            && EqualityComparer<T?>.Default.Equals(_value, other._value)
            && Equals(_error, other._error);
    }

    /// <summary>Determines whether <paramref name="obj"/> is an <see cref="ApiResult{T}"/> equal to this instance.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="ApiResult{T}"/>; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ApiResult<T> other && Equals(other);
    }

    /// <summary>Returns a hash code derived from the success state, value, and error.</summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(_state, _value, _error);
    }

    /// <summary>Determines whether two <see cref="ApiResult{T}"/> instances are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ApiResult<T> left, ApiResult<T> right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="ApiResult{T}"/> instances are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ApiResult<T> left, ApiResult<T> right) => !left.Equals(right);

    private void _EnsureInitialized()
    {
        if (_state == 0)
        {
            throw new InvalidOperationException("ApiResult<T> was not properly initialized.");
        }
    }
}
