// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Cysharp.Text;
using Framework.Primitives;

namespace Framework.Exceptions;

[PublicAPI]
public sealed class ConflictException : Exception
{
    public const string DefaultErrorCode = "error";

    public ConflictException(ErrorDescriptor error)
        : base(_BuildErrorMessage(error))
    {
        Errors = [error];
    }

    public ConflictException([LocalizationRequired] string error, string code = DefaultErrorCode)
        : base(_BuildErrorMessage(error))
    {
        Errors = [new(code, error)];
    }

    public ConflictException([LocalizationRequired] string error, Exception inner)
        : base(_BuildErrorMessage(error), inner)
    {
        Errors = [new(DefaultErrorCode, error)];
    }

    public ConflictException(IReadOnlyList<ErrorDescriptor> errors)
        : base(_BuildErrorMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<ErrorDescriptor> Errors { get; }

    private static string _BuildErrorMessage(IEnumerable<ErrorDescriptor> errors)
    {
        var builder = ZString.CreateStringBuilder();

        builder.Append("Conflict:");

        foreach (var error in errors)
        {
            builder.Append($"{Environment.NewLine}-- {_BuildErrorMessage(error)}");
        }

        return builder.ToString();
    }

    private static string _BuildErrorMessage(ErrorDescriptor error) => $"Conflict: {error}";

    private static string _BuildErrorMessage(string error) => $"Conflict: {error}";
}
