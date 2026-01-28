# Headless.Generator.Primitives

Roslyn source generator for creating strongly-typed domain primitives.

## Problem Solved

Automatically generates boilerplate code for domain primitives including constructors, equality, comparison, JSON serialization, Entity Framework value converters, TypeConverters, and OpenAPI schema mappings.

## Key Features

- Roslyn incremental source generator
- Generates for types implementing `IPrimitive<T>`
- Auto-generated code:
  - Constructors and factory methods
  - `IEquatable<T>`, `IComparable<T>` implementations
  - JSON converters (System.Text.Json)
  - Entity Framework Core value converters
  - TypeConverter for model binding
  - Dapper type handlers
  - NSwag/Swashbuckle schema mappings

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
var email = Email.From("user@example.com");  // Factory method
var value = email.Value;                      // Underlying value
var json = JsonSerializer.Serialize(email);   // JSON: "user@example.com"
```

### Entity Framework Integration

```csharp
// Auto-generated value converter is registered via:
modelBuilder.Entity<User>()
    .Property(u => u.Email)
    .HasConversion<EmailValueConverter>();
```

## Configuration

### MSBuild Properties

```xml
<PropertyGroup>
  <PrimitiveGenerator_GenerateDapper>true</PrimitiveGenerator_GenerateDapper>
  <PrimitiveGenerator_GenerateEfCore>true</PrimitiveGenerator_GenerateEfCore>
  <PrimitiveGenerator_GenerateSwashbuckle>false</PrimitiveGenerator_GenerateSwashbuckle>
</PropertyGroup>
```

## Dependencies

- `Headless.Generator.Primitives.Abstractions`
- `Microsoft.CodeAnalysis.CSharp` (compile-time only)

## Side Effects

- Generates source files at compile time
- No runtime dependencies added
