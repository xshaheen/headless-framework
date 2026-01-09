# Framework.Generator.Primitives.Abstractions

Abstractions and attributes for the domain primitives source generator.

## Problem Solved

Provides the runtime contracts and attributes needed to define strongly-typed domain primitives that work with the source generator, ensuring type safety and domain constraints at compile time.

## Key Features

- `IPrimitive<T>` - Core interface for domain primitives
- `PrimitiveValidationResult` - Validation result model
- Attributes for generator configuration:
  - `StringLengthAttribute` - Min/max length constraints
  - `SupportedOperationsAttribute` - Enable comparison, math operations
  - `SerializationFormatAttribute` - Custom serialization formats
  - `UnderlyingPrimitiveTypeAttribute` - Specify underlying type
- Helper extensions for DateOnly, XML serialization

## Installation

```bash
dotnet add package Framework.Generator.Primitives.Abstractions
```

## Quick Start

```csharp
using Framework.Generator.Primitives;

[StringLength(1, 100)]
public readonly partial struct ProductName : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return PrimitiveValidationResult.Error("Name cannot be empty");

        return PrimitiveValidationResult.Ok;
    }
}
```

### With Supported Operations

```csharp
[SupportedOperations(Comparison = true, Math = true)]
public readonly partial struct Quantity : IPrimitive<int>
{
    public static PrimitiveValidationResult Validate(int value)
    {
        return value >= 0
            ? PrimitiveValidationResult.Ok
            : PrimitiveValidationResult.Error("Quantity cannot be negative");
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
