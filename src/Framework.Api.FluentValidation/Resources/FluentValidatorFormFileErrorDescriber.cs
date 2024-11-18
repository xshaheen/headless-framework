// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.FluentValidation.Resources;

public static class FluentValidatorFormFileErrorDescriber
{
    public static ErrorDescriptor FileNotEmpty()
    {
        return new(code: "file:not_empty", description: FluentValidatorErrors.file_not_empty);
    }

    public static ErrorDescriptor FileGreaterThanOrEqualToValidator()
    {
        return new(
            code: "file:greater_than_or_equal_to",
            description: FluentValidatorErrors.file_greater_than_or_equal_to
        );
    }

    public static ErrorDescriptor FileLessThanOrEqualToValidator()
    {
        return new(code: "file:less_than_or_equal_to", description: FluentValidatorErrors.file_less_than_or_equal_to);
    }

    public static ErrorDescriptor FileContentTypeValidator()
    {
        return new(code: "file:unexpected_signature", description: FluentValidatorErrors.file_unexpected_signature);
    }

    public static ErrorDescriptor FileSignatureValidator()
    {
        return new(code: "file:unexpected_signature", description: FluentValidatorErrors.file_unexpected_signature);
    }
}
