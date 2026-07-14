// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Exceptions;

/// <summary>Thrown when a scheduling request cannot be mapped to generated <c>[JobFunction]</c> metadata.</summary>
[PublicAPI]
public sealed class JobFunctionNotFoundException : Exception
{
    /// <summary>Creates an exception for an unmapped typed request payload.</summary>
    public JobFunctionNotFoundException(Type requestType)
        : base(_Message(requestType))
    {
        RequestType = requestType;
    }

    /// <summary>Creates an exception for an unknown descriptor function identity.</summary>
    public JobFunctionNotFoundException(string functionName)
        : base($"No job function descriptor is registered with name '{functionName}'.")
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException(
                "The function name cannot be null, empty, or whitespace.",
                nameof(functionName)
            );
        }

        FunctionName = functionName;
    }

    /// <summary>The unmapped typed request payload, when the lookup was type-based.</summary>
    public Type? RequestType { get; }

    /// <summary>The unknown durable function name, when the lookup was descriptor-based.</summary>
    public string? FunctionName { get; }

    private static string _Message(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return $"No job function is registered for request type '{requestType.FullName ?? requestType.Name}'.";
    }
}
