## Release 0.12.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
HF1000  | Headless.Generator.Primitives | Error | An exception was thrown by the PrimitiveGenerator generator
HF1001  | Headless.Generator.Primitives | Error | Primitive type must be a Numeric type, Date type or string type to use PrimitivesGenerator
HF1002  | Headless.Generator.Primitives | Error | Class must be partial to generate Empty constructor
HF1003  | Headless.Generator.Primitives | Error | Primitives Should not have non obsolete empty constructors
HF1011  | Headless.Generator.Primitives | Error | Primitives must not have a parameterized constructor to successfully generate members
HF1012  | Headless.Generator.Primitives | Error | SerializationFormatAttribute can only be used with Date types
HF1013  | Headless.Generator.Primitives | Error | SupportedOperationsAttribute can only be used with Operational Numeric types
HF1015  | Headless.Generator.Primitives | Warning | Type should be a value type
HF1016  | Headless.Generator.Primitives | Warning | Type should be a reference type
HF1021  | Headless.Generator.Primitives | Warning | InvalidPrimitiveValueException should be thrown in order to be converted to bad request
