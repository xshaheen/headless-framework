# Test Case Design: Framework.Serializer (All Packages)

**Packages:**
- `src/Framework.Serializer.Abstractions`
- `src/Framework.Serializer.Json`
- `src/Framework.Serializer.MessagePack`

**Test Projects:** `Framework.Serializer.Tests.Unit` (existing)
**Generated:** 2026-01-25

## Package Analysis

### Framework.Serializer.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `ISerializer.cs` | Serializer interface hierarchy | Low (interface) |
| `SerializerExtensions.cs` | Extension methods for ISerializer | High |

### Framework.Serializer.Json

| File | Purpose | Testable |
|------|---------|----------|
| `SystemJsonSerializer.cs` | IJsonSerializer implementation | High |
| `JsonConstants.cs` | Default JSON options | High |
| `JsonOptionsProvider.cs` | IJsonOptionsProvider interface + impl | Medium |
| `Converters/IpAddressJsonConverter.cs` | IPAddress JSON converter | High |
| `Converters/StringToGuidJsonConverter.cs` | String to Guid converter | High |
| `Converters/NullableStringToGuidJsonConverter.cs` | Nullable Guid converter | High |
| `Converters/StringToBooleanJsonConverter.cs` | String to bool converter | High |
| `Converters/UnixTimeJsonConverter.cs` | Unix timestamp converter | High |
| `Converters/CollectionItemJsonConverter.cs` | Collection item converter | High |
| `Converters/EmptyStringAsNullJsonConverter.cs` | Empty string to null | High |
| `Converters/ObjectToInferredTypesJsonConverter.cs` | Dynamic type inference | High |
| `Converters/SingleOrCollectionJsonConverter.cs` | Single/array normalization | High |
| `Extensions/ToObjectExtensions.cs` | JsonElement.ToObject extensions | High |
| `Modifiers/JsonPropertiesModifiers.cs` | Property modifiers | Medium |
| `Modifiers/SystemJsonTypeInfoResolver.cs` | Type info resolver | Medium |

### Framework.Serializer.MessagePack

| File | Purpose | Testable |
|------|---------|----------|
| `MessagePackSerializer.cs` | IBinarySerializer implementation | High |

## Current Test Coverage

**Existing Unit Tests:** 12+ test files
- `SystemJsonSerializerTests.cs` - 6 tests
- `MessagePackSerializerTests.cs` - Basic tests
- `JsonConstantsTests.cs` - Options creation
- `ToObjectExtensionsTests.cs` - JsonElement extensions
- `IpAddressJsonConverterTests.cs` - IPAddress conversion
- `NullableStringToGuidJsonConverterTests.cs` - Nullable Guid
- `StringToGuidJsonConverterTests.cs` - Guid conversion
- `StringToBooleanJsonConverterTests.cs` - Boolean conversion
- `UnixTimeJsonConverterTests.cs` - Unix time conversion
- `CollectionItemJsonConverterTests.cs` - Collection items
- `ObjectToInferredTypesJsonConverterTests.cs` - Type inference
- `SingleOrCollectionJsonConverterTests.cs` - Single/array

---

## Missing: SerializerExtensions Tests

**File:** `tests/Framework.Serializer.Tests.Unit/SerializerExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_serialize_to_bytes` | SerializeToBytes extension |
| `should_deserialize_from_bytes` | DeserializeFromBytes extension |
| `should_serialize_to_string` | SerializeToString extension (text serializer) |
| `should_deserialize_from_string` | DeserializeFromString extension (text serializer) |

---

## Missing: EmptyStringAsNullJsonConverter Tests

**File:** `tests/Framework.Serializer.Tests.Unit/Converters/EmptyStringAsNullJsonConverterTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_convert_empty_string_to_null` | "" → null |
| `should_preserve_non_empty_string` | "value" preserved |
| `should_preserve_null_value` | null unchanged |
| `should_convert_whitespace_to_null` | " " → null (if configured) |
| `should_write_null_as_null` | Write null value |
| `should_write_string_as_string` | Write non-null value |

---

## Missing: JsonPropertiesModifiers Tests

**File:** `tests/Framework.Serializer.Tests.Unit/Modifiers/JsonPropertiesModifiersTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_apply_camel_case_naming` | Property naming |
| `should_ignore_null_values` | Null handling |
| `should_apply_custom_converters` | Converter application |

---

## Missing: SystemJsonSerializer Additional Tests

**File:** `tests/Framework.Serializer.Tests.Unit/SystemJsonSerializerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_use_custom_options_provider` | IJsonOptionsProvider injection |
| `should_deserialize_with_type_parameter` | Deserialize(Stream, Type) |
| `should_handle_complex_nested_objects` | Deep nesting |
| `should_handle_collections` | Array/List serialization |
| `should_respect_json_attributes` | JsonPropertyName, etc. |

---

## Missing: MessagePackSerializer Additional Tests

**File:** `tests/Framework.Serializer.Tests.Unit/MessagePackSerializerTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_serialize_and_deserialize_complex_object` | Round-trip |
| `should_handle_null_value` | Null handling |
| `should_be_smaller_than_json` | Size comparison |
| `should_handle_datetime` | DateTime serialization |
| `should_handle_guid` | Guid serialization |
| `should_deserialize_with_type_parameter` | Deserialize(Stream, Type) |

---

## Test Summary

| Component | Existing | New Unit | Total |
|-----------|----------|----------|-------|
| SystemJsonSerializer | 6 | 5 | 11 |
| MessagePackSerializer | ~3 | 6 | 9 |
| SerializerExtensions | 0 | 4 | 4 |
| EmptyStringAsNullJsonConverter | 0 | 6 | 6 |
| JsonPropertiesModifiers | 0 | 3 | 3 |
| Other converters (existing) | ~40 | 0 | 40 |
| **Total** | **~49** | **24** | **73** |

---

## Priority Order

1. **SerializerExtensions** - Convenience methods used throughout
2. **EmptyStringAsNullJsonConverter** - Common use case
3. **SystemJsonSerializer edge cases** - Complex scenarios
4. **MessagePackSerializer** - Binary serialization

---

## Notes

1. **ISerializer hierarchy**:
   - `ISerializer` - Base interface (Stream-based)
   - `IBinarySerializer` - Marker for binary formats
   - `ITextSerializer` - Marker for text formats
   - `IJsonSerializer` - JSON-specific (extends ITextSerializer)
2. **JsonConstants** - Provides pre-configured JsonSerializerOptions:
   - `CreateWebJsonOptions()` - For web APIs
   - `DefaultInternalJsonOptions` - For internal use
3. **Converters** - Custom JsonConverters for common scenarios
4. **MessagePack** - Binary serialization using MessagePack-CSharp

---

## ISerializer Architecture

```
ISerializer
├── Serialize<T>(T value, Stream output)
├── Serialize(object? value, Stream output)
├── Deserialize<T>(Stream data)
└── Deserialize(Stream data, Type objectType)

IBinarySerializer : ISerializer
└── MessagePackSerializer

ITextSerializer : ISerializer
└── IJsonSerializer
    └── SystemJsonSerializer
```

---

## Recommendation

**Low Priority** - Existing test coverage is comprehensive for converters. Additional tests would add value for:
- SerializerExtensions (convenience methods)
- EmptyStringAsNullJsonConverter
- MessagePackSerializer edge cases
