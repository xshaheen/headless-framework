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
- `Argument.IsPositive(number)` / `IsNegative` / `IsPositiveOrZero` / `IsNegativeOrZero`
- `Argument.IsOneOf(value, allowedValues)`
- `Argument.IsInEnum(enumValue)`
- `Argument.HasNoNulls(collection)`
- `Argument.FileExists(path)` / `DirectoryExists(path)`

### Runtime Assertions

```csharp
using Headless.Checks;

public void ProcessOrder()
{
    Ensure.True(_initialized, "Service must be initialized.");
    Ensure.NotDisposed(_disposed, this);
    Ensure.False(_queue.IsEmpty, "Queue should not be empty.");
}
```

## Configuration

No configuration required.

## Dependencies

None.

## Side Effects

None.
