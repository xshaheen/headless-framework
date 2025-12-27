# Framework.Checks

`Framework.Checks` is a lightweight, high-performance guard clause library for .NET 10.0 defined to simplify argument validation and runtime state assertions. It provides a fluent, static API to ensure code correctness by validating inputs and object states, throwing appropriate exceptions (like `ArgumentNullException`, `ArgumentException`, `InvalidOperationException`) when checks fail.

## Features

-   **Argument Validation**: Extensive set of static methods on the `Argument` class to validate method parameters.
-   **Runtime Assertions**: `Ensure` class for validating internal state or business logic invariants.
-   **Performance Optimized**: Uses `[MethodImpl(MethodImplOptions.AggressiveInlining)]` and `[DebuggerStepThrough]` for minimal overhead and better debugging experience.
-   **Caller Expression Support**: Automatically captures parameter names using `[CallerArgumentExpression]`, reducing boilerplate code.
-   **Comprehensive Type Support**: built-in support for `nullable` types, `Span<T>`, `ReadOnlySpan<T>`, `IEnumerable<T>`, `IReadOnlyCollection<T>`, and `string`.

## Dependencies

-   .NET 10.0

## Usage

### Argument Validation

Use the `Argument` static class to validate parameters at the beginning of your methods. These methods typically return the checked value.

```csharp
using Framework.Checks;

public void CreateUser(string name, int age, List<string> roles)
{
    // Throws ArgumentNullException if name is null
    // Throws ArgumentException if name is empty
    Argument.IsNotNullOrEmpty(name);

    // Throws ArgumentOutOfRangeException if age is negative or zero
    Argument.IsPositive(age);

    // Throws ArgumentNullException if roles is null
    // Throws ArgumentException if roles is empty
    Argument.IsNotNullOrEmpty(roles);

    // Throws ArgumentException if any element in roles is null
    Argument.HasNoNulls(roles);
}
```

### Common Argument Checks

-   `Argument.IsNotNull(value)`
-   `Argument.IsNull(value)`
-   `Argument.IsNotEmpty(collection)`
-   `Argument.IsNotNullOrEmpty(string|collection)`
-   `Argument.IsNotNullOrWhiteSpace(string)`
-   `Argument.IsPositive(number)`
-   `Argument.IsPositiveOrZero(number)`
-   `Argument.IsNegative(number)`
-   `Argument.IsNegativeOrZero(number)`
-   `Argument.IsOneOf(value, allowedValues)`
-   `Argument.IsInEnum(enumValue)`
-   `Argument.HasNoNulls(collection)`
-   `Argument.IsAssignableTo<T>(value)`
-   `Argument.FileExists(path)`
-   `Argument.DirectoryExists(path)`

### Runtime Assertions

Use the `Ensure` class for internal state checks that imply a bug in the code if they fail. These throw `InvalidOperationException` or `ObjectDisposedException`.

```csharp
using Framework.Checks;

public void ProcessOrder()
{
    Ensure.True(_initialized, "Service must be initialized before processing.");
    Ensure.NotDisposed(_disposed, this);

    // ... logic ...

    Ensure.False(_queue.IsEmpty, "Queue should not be empty at this stage.");
}
```
