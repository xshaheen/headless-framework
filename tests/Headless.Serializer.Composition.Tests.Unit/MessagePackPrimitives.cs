// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;

namespace Tests;

// Compiled with PrimitiveMessagePackFormatters on (see the project file), so each primitive carries the generated
// [MessagePackFormatter] the MessagePack tests rely on.

public readonly partial struct CustomerId : IPrimitive<Guid>
{
    public static PrimitiveValidationResult Validate(Guid value) =>
        value == Guid.Empty ? "Customer id must not be empty" : PrimitiveValidationResult.Ok;
}

public sealed partial class ContactEmail : IPrimitive<string>
{
    public static PrimitiveValidationResult Validate(string value) =>
        value.Contains('@', StringComparison.Ordinal) ? PrimitiveValidationResult.Ok : "Email must contain '@'";
}

public readonly partial struct ItemQuantity : IPrimitive<int>
{
    public static PrimitiveValidationResult Validate(int value) =>
        value < 0 ? "Quantity must not be negative" : PrimitiveValidationResult.Ok;
}
