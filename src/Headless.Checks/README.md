# Headless.Checks

Guard clause library for argument validation and defensive programming.

## Problem Solved

Provides a fluent, expressive API for validating method arguments and ensuring preconditions, eliminating boilerplate validation code and standardizing error messages.

## Key Features

- **Argument Validation**: Extensive static methods on `Argument` class
- **Runtime Assertions**: `Ensure` class for internal state validation
- **Performance Optimized**: `AggressiveInlining` and `DebuggerStepThrough`
- **Caller Expression Support**: Automatic parameter name capture
- **Type Support**: Nullable, `Span<T>`, `ReadOnlySpan<T>`, collections, strings

## Installation

```bash
dotnet add package Headless.Checks
```

## Quick Start

### Argument Validation

```csharp
using Headless.Checks;

public void CreateUser(string name, int age, List<string> roles)
{
    Argument.IsNotNullOrEmpty(name);
    Argument.IsPositive(age);
    Argument.IsNotNullOrEmpty(roles);
    Argument.HasNoNulls(roles);
}
```

### Common Checks

- `Argument.IsNotNull(value)`
- `Argument.IsNotNullOrEmpty(string|collection)`
- `Argument.IsNotNullOrWhiteSpace(string)`
- `Argument.IsNotEmpty(guid)` — rejects `Guid.Empty` (also `Guid?`; null passes through)
- `Argument.IsPositive(number)` / `IsNegative` / `IsPositiveOrZero` / `IsNegativeOrZero`
- `Argument.IsZero(number)` / `IsNotZero(number)` — `INumber<T>`, nullable, and `TimeSpan` overloads
- `Argument.IsEqualTo(value, expected)` / `IsNotEqualTo(value, other)` — value equality (optional `IEqualityComparer<T>` overload); contrast `IsReferenceEqualTo`/`IsReferenceNotEqualTo` for identity
- `Argument.IsOneOf(value, allowedValues)`
- `Argument.IsInEnum(enumValue)`
- `Argument.HasNoNulls(collection)` / `HasNoDuplicates(collection)` — `HasNoDuplicates` takes an optional `IEqualityComparer<T>`
- `Argument.HasLength` / `HasMinLength` / `HasMaxLength` / `HasLengthBetween(string, …)` — string length bounds (throw `ArgumentOutOfRangeException`)
- `Argument.HasCount` / `HasMinCount` / `HasMaxCount` / `HasCountBetween(collection, …)` — item-count bounds (`IReadOnlyCollection<T>` fast-path + `IEnumerable<T>`)
- `Argument.StartsWith` / `EndsWith` / `Contains(string, value, comparison)` — string content (`StringComparison.Ordinal` by default)
- `Argument.IsValidIndex(index, count | collection | span)` — bounds-checks an index against a length/collection/span
- `Argument.FileExists(path)` / `DirectoryExists(path)`
- `Argument.Matches(string, regex)` — throws `ArgumentException` when the string does not match the pattern
- `Argument.Is(condition, message, nameof(arg))` / `IsFalse(condition, …)` — custom argument precondition that must hold / must not hold; throws `ArgumentException`

### Runtime Assertions

```csharp
using Headless.Checks;

public void ProcessOrder()
{
    Ensure.True(_initialized, "Service must be initialized.");
    Ensure.NotDisposed(_disposed, this);
    Ensure.False(_queue.IsEmpty, "Queue should not be empty.");
    var connection = Ensure.NotNull(_connection); // state must-be-present; throws InvalidOperationException
}
```

## Configuration

No configuration required.

## Dependencies

None.

## Side Effects

None.
