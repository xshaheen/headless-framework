# Headless.Generator.Primitives

Roslyn source generator for creating strongly-typed domain primitives.

## Problem Solved

Automatically generates boilerplate code for domain primitives including constructors, equality, comparison, JSON serialization, Entity Framework value converters, TypeConverters, and OpenAPI schema mappings.

## Key Features

- Roslyn incremental source generator
- Emits unique `HF1000`-series diagnostics for invalid primitive declarations; legacy `AL` diagnostic IDs are no longer emitted.
- Generates for types implementing `IPrimitive<T>`
- Auto-generated code:
  - Constructors and factory methods
  - `IEquatable<T>`, `IComparable<T>` implementations
  - JSON converters (System.Text.Json)
  - Entity Framework Core value converters
  - TypeConverter for model binding
  - Dapper type handlers
  - NSwag/Swashbuckle schema mappings

## Design Notes

Primitive generator diagnostics use the framework-wide `HF` prefix. Existing suppressions for legacy `AL` IDs must move to the corresponding `HF` ID. The former duplicate `AL1012` is split: date-format validation uses `HF1012`, while numeric-operation validation uses `HF1013`.

| ID | Severity | Meaning |
| --- | --- | --- |
| `HF1000` | Error | The generator failed with an exception. |
| `HF1001` | Error | The primitive has an unsupported base type. |
| `HF1002` | Error | The primitive must be partial. |
| `HF1003` | Error | The primitive has a non-obsolete default constructor. |
| `HF1011` | Error | The primitive has a parameterized constructor. |
| `HF1012` | Error | `SerializationFormatAttribute` requires a date primitive. |
| `HF1013` | Error | `SupportedOperationsAttribute` requires an operational numeric primitive. |
| `HF1015` | Warning | A primitive wrapping a value type should be a value type. |
| `HF1016` | Warning | A primitive wrapping a reference type should be a reference type. |
| `HF1021` | Warning | Primitive validation throws an incompatible exception type. |

## Installation

```bash
dotnet add package Headless.Generator.Primitives
```

## Quick Start

```csharp
// Define your primitive
[StringLength(1, 50)]
public readonly partial struct Email : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value)
    {
        if (!value.Contains('@'))
            return PrimitiveValidationResult.Error("Invalid email format");

        return PrimitiveValidationResult.Ok;
    }
}

// Generated code provides:
var email = Email.From("user@example.com"); // Factory method
var value = email.Value; // Underlying value
var json = JsonSerializer.Serialize(email); // JSON: "user@example.com"
```

### Entity Framework Integration

```csharp
// Auto-generated value converter is registered via:
modelBuilder.Entity<User>().Property(u => u.Email).HasConversion<EmailValueConverter>();
```

## Configuration

### MSBuild Properties

```xml
<PropertyGroup>
  <PrimitiveDapperConverters>true</PrimitiveDapperConverters>
  <PrimitiveEntityFrameworkValueConverters>true</PrimitiveEntityFrameworkValueConverters>
  <PrimitiveSwashbuckleSwaggerConverters>false</PrimitiveSwashbuckleSwaggerConverters>
  <PrimitiveNswagSwaggerConverters>false</PrimitiveNswagSwaggerConverters>
  <PrimitiveJsonConverters>true</PrimitiveJsonConverters>
  <PrimitiveTypeConverters>true</PrimitiveTypeConverters>
  <PrimitiveXmlConverters>false</PrimitiveXmlConverters>
</PropertyGroup>
```

## Dependencies

- `Headless.Generator.Primitives.Abstractions`
- `Microsoft.CodeAnalysis.CSharp` (compile-time only)

## Side Effects

- Generates source files at compile time
- No runtime dependencies added
