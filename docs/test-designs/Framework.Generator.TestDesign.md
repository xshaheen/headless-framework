# Framework.Generator Test Design

## Overview

The Framework.Generator packages provide Roslyn source generators for creating strongly-typed primitive value types with built-in validation, serialization, and ORM support.

### Packages
1. **Framework.Generator.Primitives.Abstractions** - Attributes, interfaces, and helpers for generated primitives
2. **Framework.Generator.Primitives** - Roslyn incremental source generator

### Key Components
- **PrimitiveGenerator** - Main IIncrementalGenerator implementation
- **Parser** - Extracts primitive type information from syntax
- **Emitter** - Generates C# source code
- Various emitters for EF Core, Dapper, TypeConverter, Swashbuckle, NSwag

### Existing Tests
**No existing tests found**

---

## 1. Framework.Generator.Primitives.Abstractions

### 1.1 PrimitiveValidationResult Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_create_success_result | Unit | Success factory |
| 2 | should_create_failure_result | Unit | Failure with message |
| 3 | should_report_is_valid_correctly | Unit | IsValid property |

### 1.2 InvalidPrimitiveValueException Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 4 | should_create_with_message | Unit | Exception message |
| 5 | should_create_with_value | Unit | Value property |

### 1.3 IPrimitive Interface Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 6 | should_define_validate_contract | Unit | Interface compliance |

### 1.4 Attribute Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 7 | should_create_primitive_assembly_attribute | Unit | PrimitiveAssemblyAttribute |
| 8 | should_create_serialization_format_attribute | Unit | SerializationFormatAttribute |
| 9 | should_create_string_length_attribute | Unit | StringLengthAttribute |
| 10 | should_create_supported_operations_attribute | Unit | SupportedOperationsAttribute |
| 11 | should_create_underlying_type_attribute | Unit | UnderlyingPrimitiveTypeAttribute |

### 1.5 DateOnlyExtensions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 12 | should_convert_dateonly_to_datetime | Unit | ToDateTime extension |
| 13 | should_convert_datetime_to_dateonly | Unit | ToDateOnly extension |

### 1.6 JsonInternalConverters Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 14 | should_convert_dateonly_json | Unit | DateOnly serialization |
| 15 | should_convert_timeonly_json | Unit | TimeOnly serialization |

### 1.7 ToXmlStringExtensions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 16 | should_convert_int_to_xml | Unit | Int32 XML format |
| 17 | should_convert_decimal_to_xml | Unit | Decimal XML format |
| 18 | should_convert_guid_to_xml | Unit | Guid XML format |
| 19 | should_convert_datetime_to_xml | Unit | DateTime XML format |
| 20 | should_convert_dateonly_to_xml | Unit | DateOnly XML format |

### 1.8 XmlReaderExtensions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 21 | should_read_int_from_xml | Unit | ParseInt helper |
| 22 | should_read_decimal_from_xml | Unit | ParseDecimal helper |
| 23 | should_read_guid_from_xml | Unit | ParseGuid helper |

---

## 2. Framework.Generator.Primitives (Source Generator)

### 2.1 Parser Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 24 | should_parse_primitive_type_info | Unit | Extract type from syntax |
| 25 | should_detect_underlying_type | Unit | UnderlyingPrimitiveType attribute |
| 26 | should_detect_string_length | Unit | StringLength attribute |
| 27 | should_detect_supported_operations | Unit | SupportedOperations attribute |
| 28 | should_detect_serialization_format | Unit | SerializationFormat attribute |

### 2.2 Emitter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 29 | should_emit_struct_definition | Unit | Basic struct output |
| 30 | should_emit_value_property | Unit | Value property |
| 31 | should_emit_constructor | Unit | Constructor with validation |
| 32 | should_emit_equality_operators | Unit | == and != operators |
| 33 | should_emit_comparison_operators | Unit | < > <= >= operators |
| 34 | should_emit_tostring | Unit | ToString override |
| 35 | should_emit_gethashcode | Unit | GetHashCode override |
| 36 | should_emit_parse_method | Unit | Parse/TryParse methods |

### 2.3 TypeConverterSourceFilesGeneratorEmitter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 37 | should_emit_type_converter | Unit | TypeConverter class |
| 38 | should_support_string_conversion | Unit | ConvertFrom string |
| 39 | should_support_underlying_conversion | Unit | ConvertFrom underlying |

### 2.4 EntityFrameworkValueConverterSourceFilesGeneratorEmitter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 40 | should_emit_ef_value_converter | Unit | EF ValueConverter class |
| 41 | should_emit_ef_comparer | Unit | ValueComparer class |

### 2.5 DapperSourceFilesGeneratorEmitter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 42 | should_emit_dapper_type_handler | Unit | SqlMapper.TypeHandler |

### 2.6 SwashbuckleSourceFilesGeneratorEmitter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 43 | should_emit_schema_filter | Unit | ISchemaFilter implementation |

### 2.7 NswagSourceFilesGeneratorEmitter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 44 | should_emit_nswag_type_mapper | Unit | ITypeMapper implementation |

### 2.8 Generator Integration Tests (Compilation Verification)

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 45 | should_generate_valid_code_for_int_primitive | Unit | Int underlying type |
| 46 | should_generate_valid_code_for_string_primitive | Unit | String underlying type |
| 47 | should_generate_valid_code_for_guid_primitive | Unit | Guid underlying type |
| 48 | should_generate_valid_code_for_decimal_primitive | Unit | Decimal underlying type |
| 49 | should_report_diagnostic_for_invalid_type | Unit | Error reporting |
| 50 | should_support_nullable_primitives | Unit | Nullable handling |

---

## Summary

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| Abstractions (Validation) | 3 | 0 | 3 |
| Abstractions (Exception) | 2 | 0 | 2 |
| Abstractions (Attributes) | 5 | 0 | 5 |
| Abstractions (Extensions) | 13 | 0 | 13 |
| Generator (Parser) | 5 | 0 | 5 |
| Generator (Emitter) | 8 | 0 | 8 |
| Generator (TypeConverter) | 3 | 0 | 3 |
| Generator (EF Core) | 2 | 0 | 2 |
| Generator (Dapper) | 1 | 0 | 1 |
| Generator (Swashbuckle) | 1 | 0 | 1 |
| Generator (NSwag) | 1 | 0 | 1 |
| Generator (Integration) | 6 | 0 | 6 |
| **Total** | **50** | **0** | **50** |

### Test Distribution
- **Unit tests**: 50 (all tests are unit tests for generators)
- **Integration tests**: 0 (generators tested via compilation verification)
- **Existing tests**: 0
- **Missing tests**: 50 (all tests new)

### Test Project Structure
```
tests/
├── Framework.Generator.Primitives.Abstractions.Tests.Unit/  (NEW - 23 tests)
│   ├── PrimitiveValidationResultTests.cs
│   ├── InvalidPrimitiveValueExceptionTests.cs
│   ├── Attributes/
│   │   └── AttributeTests.cs
│   └── Extensions/
│       ├── DateOnlyExtensionsTests.cs
│       ├── ToXmlStringExtensionsTests.cs
│       └── XmlReaderExtensionsTests.cs
└── Framework.Generator.Primitives.Tests.Unit/               (NEW - 27 tests)
    ├── ParserTests.cs
    ├── Emitters/
    │   ├── CoreEmitterTests.cs
    │   ├── TypeConverterEmitterTests.cs
    │   ├── EfCoreEmitterTests.cs
    │   ├── DapperEmitterTests.cs
    │   └── OpenApiEmitterTests.cs
    └── Integration/
        └── GeneratorCompilationTests.cs
```

### Key Testing Considerations

1. **Source Generator Testing**: Use Microsoft.CodeAnalysis.CSharp.Testing or VerifyCS patterns for generator tests.

2. **Compilation Verification**: Generator tests should compile test input and verify:
   - No compiler errors
   - Expected files generated
   - Generated code compiles

3. **Incremental Generator**: Headless.Generator.Primitives uses IIncrementalGenerator - tests should verify incremental behavior.

4. **Cross-Framework**: Abstractions target net10.0, Generator targets netstandard2.0 - tests should cover both.

5. **Diagnostic Reporting**: Generator emits diagnostics for invalid inputs - tests should verify error codes and messages.
