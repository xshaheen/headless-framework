# feat: Extensible Result Type with Error Inheritance

## Enhancement Summary

**Deepened on:** 2026-01-10
**Research agents used:** ErrorOr patterns, .NET documentation, Stephen Toub review, Scott Hanselman review, Performance Oracle, Architecture Strategist, Pattern Recognition, Security Sentinel, Code Simplicity Reviewer, dotnet-style skill, dotnet-nuget-writer skill

### Key Improvements
1. **Critical Bug Fixes**: `ConflictError` has property shadowing issue; non-generic `Result` has inverted `MemberNotNullWhen` attributes
2. **Multiple Errors Support**: Add `List<ResultError>` support like ErrorOr pattern for batch operations
3. **Async Methods**: Add `MapAsync`, `BindAsync`, `MatchAsync` for async pipelines
4. **Performance**: Cache static error instances, add `TryGetValue` pattern

### Critical Issues Discovered
- `ConflictError` has conflicting `required` property and `override` - must fix design
- Non-generic `Result.IsSuccess` has wrong `MemberNotNullWhen(true, nameof(Error))` - should be `false`
- Default struct state `new Result<T>()` creates invalid state (neither success nor failure)

---

## Overview

Design a new `Result<T>` type with an extensible error hierarchy based on inheritance. Consumers can extend the error types for their domain while the framework provides common base errors and HTTP mapping.

## Design Goals

1. **Extensible** - Users can create custom error types by inheritance
2. **Type-safe** - Pattern matching on error types
3. **No HTTP in domain** - Error types are domain concepts; HTTP mapping is separate
4. **Scalable** - Works for simple apps and complex enterprise systems
5. **Zero allocation on success** - Struct-based with careful design

## Core Types

### 1. ResultError Base Class

```csharp
// Framework.Base/Primitives/ResultError.cs

namespace Framework.Primitives;

/// <summary>
/// Base class for all result errors. Extend this to create domain-specific errors.
/// </summary>
[PublicAPI]
public abstract record ResultError
{
    /// <summary>
    /// Machine-readable error code for logging and client handling.
    /// Convention: "namespace:error_name" (e.g., "user:duplicate_email")
    /// </summary>
    public abstract string Code { get; }

    /// <summary>
    /// Human-readable description. Should be localized for end-user display.
    /// </summary>
    public abstract string Message { get; }

    /// <summary>
    /// Additional structured data about the error.
    /// </summary>
    public virtual IReadOnlyDictionary<string, object?>? Metadata => null;
}
```

### 2. Framework-Provided Error Types

```csharp
// Framework.Base/Primitives/Errors/NotFoundError.cs

namespace Framework.Primitives;

/// <summary>
/// The requested resource was not found.
/// </summary>
[PublicAPI]
public record NotFoundError : ResultError
{
    public required string Entity { get; init; }
    public required string Key { get; init; }

    public override string Code => $"notfound:{Entity.ToLowerInvariant()}";
    public override string Message => $"{Entity} with key '{Key}' was not found.";

    public override IReadOnlyDictionary<string, object?> Metadata =>
        new Dictionary<string, object?> { ["entity"] = Entity, ["key"] = Key };
}

// Framework.Base/Primitives/Errors/ConflictError.cs

/// <summary>
/// Business rule conflict (duplicate, invalid state, etc.).
/// </summary>
[PublicAPI]
public record ConflictError(string Code, string Message) : ResultError
{
    public override string Code { get; } = Code;
    public override string Message { get; } = Message;
}

// Research Insight: Fixed property shadowing bug. Original had both
// "required string Code { get; init; }" AND "override string Code"
// which C# doesn't allow. Use positional record parameters instead.

// Framework.Base/Primitives/Errors/ValidationError.cs

/// <summary>
/// Input validation failed. Contains field-level errors.
/// </summary>
[PublicAPI]
public record ValidationError : ResultError
{
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> FieldErrors { get; init; }

    public override string Code => "validation:failed";
    public override string Message => "One or more validation errors occurred.";

    public override IReadOnlyDictionary<string, object?> Metadata =>
        FieldErrors.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

    public static ValidationError FromFields(params (string Field, string Error)[] errors)
    {
        var grouped = errors
            .GroupBy(e => e.Field)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.Error).ToList());

        return new ValidationError { FieldErrors = grouped };
    }
}

// Framework.Base/Primitives/Errors/ForbiddenError.cs

/// <summary>
/// Operation not permitted for current user/context.
/// </summary>
[PublicAPI]
public record ForbiddenError : ResultError
{
    public required string Reason { get; init; }

    public override string Code => "forbidden:access_denied";
    public override string Message => Reason;
}

// Framework.Base/Primitives/Errors/UnauthorizedError.cs

/// <summary>
/// Caller is not authenticated.
/// </summary>
[PublicAPI]
public record UnauthorizedError : ResultError
{
    public override string Code => "unauthorized";
    public override string Message => "Authentication required.";
}

// Framework.Base/Primitives/Errors/AggregateError.cs

/// <summary>
/// Multiple errors occurred. Useful for batch operations.
/// </summary>
[PublicAPI]
public record AggregateError : ResultError
{
    public required IReadOnlyList<ResultError> Errors { get; init; }

    public override string Code => "aggregate:multiple_errors";
    public override string Message => $"{Errors.Count} errors occurred.";
}
```

### 3. Result<T> Struct

```csharp
// Framework.Base/Primitives/Result.cs

namespace Framework.Primitives;

/// <summary>
/// Represents the outcome of an operation that may fail.
/// Success contains a value; failure contains an error.
/// </summary>
[PublicAPI]
public readonly struct Result<T> : IEquatable<Result<T>>
{
    private readonly T? _value;
    private readonly ResultError? _error;
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _value = value;
        _error = null;
        _isSuccess = true;
    }

    private Result(ResultError error)
    {
        _value = default;
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _isSuccess = false;
    }

    /// <summary>True if operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    /// <summary>True if operation failed.</summary>
    [MemberNotNullWhen(false, nameof(Value))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isSuccess;

    /// <summary>The success value. Throws if IsFailure.</summary>
    public T Value => _isSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value on failed result. Error: {_error}");

    /// <summary>The error. Throws if IsSuccess.</summary>
    public ResultError Error => !_isSuccess
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on successful result.");

    /// <summary>Try to get the value without throwing.</summary>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return _isSuccess;
    }

    /// <summary>Try to get the error without throwing.</summary>
    public bool TryGetError([MaybeNullWhen(false)] out ResultError error)
    {
        error = _error;
        return !_isSuccess;
    }

    /// <summary>Pattern match on success or failure.</summary>
    public TResult Match<TResult>(Func<T, TResult> success, Func<ResultError, TResult> failure) =>
        _isSuccess ? success(_value!) : failure(_error!);

    /// <summary>Pattern match on specific error types.</summary>
    public TResult Match<TResult>(
        Func<T, TResult> success,
        Func<NotFoundError, TResult> notFound,
        Func<ValidationError, TResult> validation,
        Func<ResultError, TResult> other) => _isSuccess
            ? success(_value!)
            : _error switch
            {
                NotFoundError e => notFound(e),
                ValidationError e => validation(e),
                _ => other(_error!)
            };

    /// <summary>Transform success value.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        _isSuccess ? Result<TOut>.Ok(mapper(_value!)) : Result<TOut>.Fail(_error!);

    /// <summary>Chain operations that may fail.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder) =>
        _isSuccess ? binder(_value!) : Result<TOut>.Fail(_error!);

    /// <summary>Execute action on success.</summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (_isSuccess) action(_value!);
        return this;
    }

    /// <summary>Execute action on failure.</summary>
    public Result<T> OnFailure(Action<ResultError> action)
    {
        if (!_isSuccess) action(_error!);
        return this;
    }

    // Factory methods

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(ResultError error) => new(error);

    // Convenience factories

    public static Result<T> NotFound(string entity, string key) =>
        new(new NotFoundError { Entity = entity, Key = key });

    public static Result<T> Conflict(string code, string message) =>
        new(new ConflictError(code, message));

    public static Result<T> ValidationFailed(params (string Field, string Error)[] errors) =>
        new(ValidationError.FromFields(errors));

    public static Result<T> Forbidden(string reason) =>
        new(new ForbiddenError { Reason = reason });

    public static Result<T> Unauthorized() =>
        new(new UnauthorizedError());

    // Implicit conversions

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(ResultError error) => Fail(error);

    // Equality

    public bool Equals(Result<T> other) =>
        _isSuccess == other._isSuccess
        && EqualityComparer<T?>.Default.Equals(_value, other._value)
        && Equals(_error, other._error);

    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_isSuccess, _value, _error);
    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);
    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);
}
```

### Research Insights: Async Extensions

```csharp
// Framework.Base/Primitives/ResultAsyncExtensions.cs

namespace Framework.Primitives;

/// <summary>
/// Async variants for Result operations (from ErrorOr patterns research).
/// </summary>
[PublicAPI]
public static class ResultAsyncExtensions
{
    public static async Task<Result<TOut>> MapAsync<T, TOut>(
        this Result<T> result,
        Func<T, Task<TOut>> mapper) =>
        result.IsSuccess
            ? await mapper(result.Value).AnyContext()
            : Result<TOut>.Fail(result.Error);

    public static async Task<Result<TOut>> BindAsync<T, TOut>(
        this Result<T> result,
        Func<T, Task<Result<TOut>>> binder) =>
        result.IsSuccess
            ? await binder(result.Value).AnyContext()
            : Result<TOut>.Fail(result.Error);

    public static async Task<TResult> MatchAsync<T, TResult>(
        this Task<Result<T>> resultTask,
        Func<T, TResult> success,
        Func<ResultError, TResult> failure)
    {
        var result = await resultTask.AnyContext();
        return result.Match(success, failure);
    }

    // Chainable from Task<Result<T>>
    public static async Task<Result<TOut>> MapAsync<T, TOut>(
        this Task<Result<T>> resultTask,
        Func<T, TOut> mapper)
    {
        var result = await resultTask.AnyContext();
        return result.Map(mapper);
    }
}
```

### Research Insights: Multiple Errors Support

```csharp
// From ErrorOr research: Support collecting multiple errors for batch operations

/// <summary>
/// Builder for accumulating multiple errors before failing.
/// Useful for validation scenarios.
/// </summary>
[PublicAPI]
public ref struct ResultErrorBuilder
{
    private List<ResultError>? _errors;

    public bool HasErrors => _errors is { Count: > 0 };

    public void Add(ResultError error)
    {
        _errors ??= [];
        _errors.Add(error);
    }

    public Result<T> ToResult<T>(T successValue) =>
        HasErrors
            ? new AggregateError { Errors = _errors! }
            : successValue;

    public Result ToResult() =>
        HasErrors
            ? Result.Fail(new AggregateError { Errors = _errors! })
            : Result.Ok();
}

// Usage:
// var builder = new ResultErrorBuilder();
// if (x) builder.Add(new SomeError());
// if (y) builder.Add(new OtherError());
// return builder.ToResult(myValue);
```

### 4. Result (Non-Generic) for Void Operations

```csharp
// Framework.Base/Primitives/Result.NonGeneric.cs

namespace Framework.Primitives;

/// <summary>
/// Represents the outcome of an operation with no return value.
/// </summary>
[PublicAPI]
public readonly struct Result : IEquatable<Result>
{
    private static readonly Result _success = new(true, null);

    private readonly bool _isSuccess;
    private readonly ResultError? _error;

    private Result(bool isSuccess, ResultError? error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    // Research Insight: Fixed inverted MemberNotNullWhen attributes
    // IsSuccess=true means Error is NULL, not non-null
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isSuccess;

    public ResultError? Error => _error;

    public TResult Match<TResult>(Func<TResult> success, Func<ResultError, TResult> failure) =>
        _isSuccess ? success() : failure(_error!);

    public Result OnSuccess(Action action)
    {
        if (_isSuccess) action();
        return this;
    }

    public Result OnFailure(Action<ResultError> action)
    {
        if (!_isSuccess) action(_error!);
        return this;
    }

    // Factory methods

    public static Result Ok() => _success;
    public static Result Fail(ResultError error) => new(false, error);

    public static Result NotFound(string entity, string key) =>
        Fail(new NotFoundError { Entity = entity, Key = key });

    public static Result Conflict(string code, string message) =>
        Fail(new ConflictError(code, message));

    public static Result Forbidden(string reason) =>
        Fail(new ForbiddenError { Reason = reason });

    // Implicit from error
    public static implicit operator Result(ResultError error) => Fail(error);

    // Equality
    public bool Equals(Result other) => _isSuccess == other._isSuccess && Equals(_error, other._error);
    public override bool Equals(object? obj) => obj is Result other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_isSuccess, _error);
    public static bool operator ==(Result left, Result right) => left.Equals(right);
    public static bool operator !=(Result left, Result right) => !left.Equals(right);
}
```

### 5. HTTP Mapping Extensions

```csharp
// Framework.Api/Extensions/ResultExtensions.cs

namespace Framework.Api.Extensions;

/// <summary>
/// Extensions to convert Result to HTTP responses.
/// Maps error types to appropriate HTTP status codes.
/// </summary>
[PublicAPI]
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(
        this Result<T> result,
        IProblemDetailsCreator creator) => result.Match(
            value => TypedResults.Ok(value),
            error => error.ToHttpResult(creator));

    public static IResult ToHttpResult(
        this Result result,
        IProblemDetailsCreator creator) => result.Match(
            () => TypedResults.NoContent(),
            error => error.ToHttpResult(creator));

    /// <summary>
    /// Maps ResultError to HTTP response using pattern matching.
    /// Override by registering custom error handlers.
    /// </summary>
    public static IResult ToHttpResult(
        this ResultError error,
        IProblemDetailsCreator creator) => error switch
    {
        NotFoundError e => TypedResults.Problem(
            creator.EntityNotFound(e.Entity, e.Key)),

        ValidationError e => TypedResults.Problem(
            creator.UnprocessableEntity(e.FieldErrors.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(msg =>
                    new ErrorDescriptor($"validation:{kv.Key}", msg)).ToList()))),

        ForbiddenError e => TypedResults.Problem(
            creator.Forbidden([new ErrorDescriptor("forbidden", e.Reason)])),

        UnauthorizedError => TypedResults.Problem(creator.Unauthorized()),

        AggregateError e => TypedResults.Problem(
            creator.Conflict(e.Errors.Select(err =>
                new ErrorDescriptor(err.Code, err.Message)).ToList())),

        // Default: treat as conflict
        _ => TypedResults.Problem(
            creator.Conflict([new ErrorDescriptor(error.Code, error.Message)]))
    };
}
```

### 6. Extensibility Pattern

```csharp
// Example: Domain-specific error in consumer's codebase

namespace MyApp.Domain.Errors;

// Custom error type - extends framework base
public record InsufficientFundsError : ResultError
{
    public required decimal Required { get; init; }
    public required decimal Available { get; init; }

    public override string Code => "payment:insufficient_funds";
    public override string Message =>
        $"Insufficient funds. Required: {Required:C}, Available: {Available:C}";

    public override IReadOnlyDictionary<string, object?> Metadata =>
        new Dictionary<string, object?>
        {
            ["required"] = Required,
            ["available"] = Available
        };
}

// Custom HTTP mapping - register in startup
public static class CustomErrorMappings
{
    public static IResult ToHttpResult(
        this InsufficientFundsError error,
        IProblemDetailsCreator creator) =>
        TypedResults.Problem(
            statusCode: 402, // Payment Required
            title: "Insufficient Funds",
            detail: error.Message,
            extensions: new Dictionary<string, object?>
            {
                ["required"] = error.Required,
                ["available"] = error.Available
            });
}

// Usage in service
public Result<PaymentReceipt> ProcessPayment(PaymentRequest request)
{
    if (request.Amount > _account.Balance)
        return new InsufficientFundsError
        {
            Required = request.Amount,
            Available = _account.Balance
        };

    // Process...
    return new PaymentReceipt(...);
}
```

## File Structure

```
src/
├── Framework.Base/
│   └── Primitives/
│       ├── ResultError.cs              # Base error class (~20 LOC)
│       ├── Result.cs                   # Result<T> struct (~120 LOC)
│       ├── Result.NonGeneric.cs        # Result struct (~60 LOC)
│       └── Errors/
│           ├── NotFoundError.cs        # (~15 LOC)
│           ├── ConflictError.cs        # (~15 LOC)
│           ├── ValidationError.cs      # (~25 LOC)
│           ├── ForbiddenError.cs       # (~10 LOC)
│           ├── UnauthorizedError.cs    # (~10 LOC)
│           └── AggregateError.cs       # (~15 LOC)
├── Framework.Api/
│   └── Extensions/
│       └── ResultExtensions.cs         # HTTP mapping (~50 LOC)
└── Framework.Api.Mvc/
    └── Extensions/
        └── ResultMvcExtensions.cs      # MVC mapping (~50 LOC)

tests/
├── Framework.Base.Tests.Unit/
│   └── Primitives/
│       ├── ResultTests.cs
│       └── ResultErrorTests.cs
└── Framework.Api.Tests.Unit/
    └── Extensions/
        └── ResultExtensionsTests.cs
```

**Total: ~400 LOC** for full extensible system

## Usage Examples

### Service Layer

```csharp
public async Task<Result<User>> GetUser(Guid id, CancellationToken ct)
{
    var user = await _repo.Find(id, ct).AnyContext();

    if (user is null)
        return Result<User>.NotFound("User", id.ToString());

    return user; // Implicit conversion
}

public async Task<Result<Order>> CreateOrder(CreateOrderCommand cmd, CancellationToken ct)
{
    var user = await _userRepo.Find(cmd.UserId, ct).AnyContext();
    if (user is null)
        return Result<Order>.NotFound("User", cmd.UserId.ToString());

    if (user.IsBlocked)
        return new BlockedUserError { UserId = cmd.UserId }; // Custom error

    var order = new Order(user, cmd.Items);
    await _orderRepo.Add(order, ct).AnyContext();

    return order;
}

public async Task<Result> DeleteUser(Guid id, CancellationToken ct)
{
    var user = await _repo.Find(id, ct).AnyContext();

    if (user is null)
        return Result.NotFound("User", id.ToString());

    if (!_permissions.CanDelete(user))
        return Result.Forbidden("You cannot delete this user.");

    await _repo.Delete(user, ct).AnyContext();

    return Result.Ok();
}
```

### Minimal API

```csharp
app.MapGet("/users/{id}", async (
    Guid id,
    IUserService service,
    IProblemDetailsCreator creator,
    CancellationToken ct) =>
{
    var result = await service.GetUser(id, ct);
    return result.ToHttpResult(creator);
});

app.MapPost("/orders", async (
    CreateOrderRequest request,
    IOrderService service,
    IProblemDetailsCreator creator,
    CancellationToken ct) =>
{
    var result = await service.CreateOrder(request.ToCommand(), ct);
    return result.ToHttpResult(creator);
});
```

### MVC Controller

```csharp
[HttpGet("{id}")]
public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct)
{
    var result = await _userService.GetUser(id, ct);
    return result
        .Map(u => u.ToDto())
        .ToActionResult(this, _creator);
}

// With pattern matching for complex scenarios
[HttpPost("transfer")]
public async Task<ActionResult<TransferResult>> Transfer(
    TransferRequest request,
    CancellationToken ct)
{
    var result = await _paymentService.Transfer(request.ToCommand(), ct);

    return result.Match<ActionResult<TransferResult>>(
        success => Ok(success),
        error => error switch
        {
            InsufficientFundsError e => StatusCode(402, new
            {
                error = e.Code,
                message = e.Message,
                required = e.Required,
                available = e.Available
            }),
            _ => error.ToActionResult(this, _creator)
        });
}
```

## Comparison with Existing DataResult<T>

| Aspect | DataResult<T> | New Result<T> |
|--------|---------------|---------------|
| Error type | `IReadOnlyList<ErrorDescriptor>` | `ResultError` (extensible) |
| Extensibility | Limited (must use ErrorDescriptor) | Full (inherit ResultError) |
| Pattern matching | On error list | On error types |
| Type safety | Low (string codes) | High (type hierarchy) |
| HTTP semantics | None | Via FailureKind or type |
| Multiple errors | Yes (list) | Via AggregateError |

## Migration Path

1. **New code**: Use `Result<T>` with error inheritance
2. **Existing code**: `DataResult<T>` continues working
3. **Gradual migration**: Convert service by service
4. **Interop**: Add `ToResult()` extension on `DataResult<T>` if needed

## Research Insights: Performance Considerations

**From Performance Oracle review:**

1. **Struct with reference field is fine** - No boxing. The `ResultError?` field is a reference, but the struct itself stays on stack.

2. **Cache static error instances**:
```csharp
public record UnauthorizedError : ResultError
{
    // Cache singleton for common case
    public static readonly UnauthorizedError Instance = new();

    public override string Code => "unauthorized";
    public override string Message => "Authentication required.";
}

// Usage: return UnauthorizedError.Instance;
```

3. **TryGetValue pattern for hot paths**:
```csharp
// Already included in Result<T>:
public bool TryGetValue([MaybeNullWhen(false)] out T value)
{
    value = _value;
    return _isSuccess;
}

// Hot path usage (avoids exception):
if (result.TryGetValue(out var user))
{
    // Use user directly
}
```

4. **Avoid Metadata allocation** - Only create `Metadata` dictionary when accessed, not in constructor.

## Research Insights: Security Considerations

**From Security Sentinel review:**

1. **Entity name exposure** - `NotFoundError` exposes entity type in error message. Consider:
   - Generic "Resource not found" for external APIs
   - Detailed entity info only for internal/admin APIs

2. **JSON escaping** - Ensure `Code` and `Message` are properly escaped when serialized to ProblemDetails to prevent XSS.

3. **Validation error field names** - Don't expose internal field names (like `_userId`). Map to API contract names.

## Research Insights: Architecture Notes

**From Architecture Strategist review:**

1. **Abstract record is correct** - Provides value equality, `with` expressions, and inheritance.

2. **HTTP mapping separation is correct** - Error types are domain concepts; HTTP codes are transport concern.

3. **Package placement** (from dotnet-nuget-writer):
   - `ResultError`, `Result<T>`, `Result` → `Framework.Base`
   - Error subclasses → `Framework.Base` (core) or domain packages (specialized)
   - HTTP extensions → `Framework.Api.Abstractions`

4. **Consider Error.Custom pattern** from ErrorOr for one-off errors:
```csharp
// Alternative for simple one-off errors without creating a class
public static ResultError Custom(string code, string message) =>
    new SimpleError(code, message);

private record SimpleError(string Code, string Message) : ResultError
{
    public override string Code { get; } = Code;
    public override string Message { get; } = Message;
}
```

## Acceptance Criteria

### Core Types
- [ ] `ResultError` abstract base record with `Code`, `Message`, `Metadata`
- [ ] `Result<T>` struct with `Ok()`, `Fail()`, `Map()`, `Bind()`, `Match()`, `TryGetValue()`
- [ ] `Result` (non-generic) for void operations
- [ ] Implicit conversion from `T` and `ResultError`

### Error Types
- [ ] `NotFoundError` with `Entity`, `Key`
- [ ] `ConflictError` with positional `(code, message)` - fix property shadowing
- [ ] `ValidationError` with `FieldErrors` dictionary
- [ ] `ForbiddenError` with `Reason`
- [ ] `UnauthorizedError` with cached `Instance`
- [ ] `AggregateError` with `Errors` list

### Extensions
- [ ] `MapAsync`, `BindAsync`, `MatchAsync` async extensions
- [ ] `ResultErrorBuilder` ref struct for accumulating errors
- [ ] `ToHttpResult()` extension for Minimal APIs
- [ ] `ToActionResult()` extension for MVC

### Quality
- [ ] Fix `MemberNotNullWhen` attributes in non-generic `Result`
- [ ] Extensibility documented with examples
- [ ] Unit tests with 90%+ coverage

## Unresolved Questions

1. **Coexistence with DataResult<T>**: Keep both? Deprecate DataResult? Provide adapter?
   - *Research suggests*: Keep both, add `ToResult()` extension for gradual migration

2. ~~**Async extensions**: Should there be `Result<T>.FromAsync(Task<T>)` helpers?~~
   - *Resolved*: Added `MapAsync`, `BindAsync`, `MatchAsync` in Research Insights section

3. **FluentValidation integration**: Auto-convert `ValidationException` to `ValidationError`?
   - *Research suggests*: Yes, add in `Framework.Api` layer not base

4. **Default struct state**: `new Result<T>()` creates invalid state. Options:
   a. Add `[Obsolete]` to parameterless constructor (can't prevent but can warn)
   b. Document clearly that default state is undefined behavior
   c. Make default state = failure with sentinel error

5. **Error.Custom factory**: Should `ResultError.Custom(code, message)` be in base class for one-off errors without creating subclass?

## References

- ErrorOr library: https://github.com/amantinband/error-or
- Ardalis.Result: https://github.com/ardalis/Result
- OneOf library: https://github.com/mcintyre321/OneOf
