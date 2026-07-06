// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Describes a single error with a machine-readable <see cref="Code"/>, a human-readable <see cref="Description"/>,
/// a <see cref="Severity"/>, and an optional bag of <see cref="Params"/> used to format the description.
/// </summary>
[PublicAPI]
public sealed class ErrorDescriptor
{
    /// <summary>Initializes a new <see cref="ErrorDescriptor"/> without parameters.</summary>
    /// <param name="code">A distinct code indicating the cause of the error.</param>
    /// <param name="description">A human-readable description of the error.</param>
    /// <param name="severity">The severity of the error. Defaults to <see cref="ValidationSeverity.Information"/>.</param>
    public ErrorDescriptor(
        string code,
        [LocalizationRequired] string description,
        ValidationSeverity severity = ValidationSeverity.Information
    )
    {
        Code = code;
        Description = description;
        Severity = severity;
    }

    /// <summary>Initializes a new <see cref="ErrorDescriptor"/> with an initial set of parameters.</summary>
    /// <param name="code">A distinct code indicating the cause of the error.</param>
    /// <param name="description">A human-readable description of the error.</param>
    /// <param name="paramsDictionary">Parameter values related to the error, stored as the descriptor's parameter bag.</param>
    /// <param name="severity">The severity of the error. Defaults to <see cref="ValidationSeverity.Information"/>.</param>
    public ErrorDescriptor(
        string code,
        [LocalizationRequired] string description,
        Dictionary<string, object?> paramsDictionary,
        ValidationSeverity severity = ValidationSeverity.Information
    )
    {
        Code = code;
        Description = description;
        Severity = severity;
        _params = paramsDictionary;
    }

    private Dictionary<string, object?>? _params;

    /// <summary>A distinct code indicating the cause of the error.</summary>
    public string Code { get; private init; }

    /// <summary>A human-readable description of the error.</summary>
    public string Description { get; private init; }

    /// <summary>The severity of the error.</summary>
    [JsonIgnore]
    public ValidationSeverity Severity { get; private init; }

    /// <summary>Object containing parameter values related to the error.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? Params => _params;

    /// <summary>Adds (or overwrites) a parameter on this descriptor and returns the same instance for chaining.</summary>
    /// <param name="key">The parameter key. Keys are compared case-insensitively.</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>This same <see cref="ErrorDescriptor"/> instance.</returns>
    public ErrorDescriptor WithParam(string key, object? value)
    {
        _params ??= new(StringComparer.OrdinalIgnoreCase);
        _params[key] = value;

        return this;
    }

    /// <summary>Adds (or overwrites) the given parameters on this descriptor and returns the same instance for chaining.</summary>
    /// <param name="values">The parameters to merge. Existing keys (compared case-insensitively) are overwritten.</param>
    /// <returns>This same <see cref="ErrorDescriptor"/> instance.</returns>
    public ErrorDescriptor WithParams(IReadOnlyDictionary<string, object?> values)
    {
        _params ??= new(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values)
        {
            _params[key] = value;
        }

        return this;
    }

    /// <summary>Deconstructs the descriptor into its code, description, and severity.</summary>
    /// <param name="code">Receives the <see cref="Code"/>.</param>
    /// <param name="description">Receives the <see cref="Description"/>.</param>
    /// <param name="severity">Receives the <see cref="Severity"/>.</param>
    public void Deconstruct(out string code, out string description, out ValidationSeverity severity)
    {
        code = Code;
        description = Description;
        severity = Severity;
    }

    /// <summary>Returns the descriptor formatted as <c>{Code}: {Description}</c>.</summary>
    public override string ToString() => $"{Code}: {Description}";
}

/// <summary>The severity assigned to an <see cref="ErrorDescriptor"/>.</summary>
public enum ValidationSeverity
{
    /// <summary>Informational message; does not indicate a failure.</summary>
    Information = 0,

    /// <summary>A warning that does not by itself block the operation.</summary>
    Warning = 1,

    /// <summary>An error indicating the operation failed validation.</summary>
    Error = 2,
}
