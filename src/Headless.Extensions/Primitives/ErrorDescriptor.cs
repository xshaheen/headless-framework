// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

[PublicAPI]
public sealed class ErrorDescriptor
{
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

    private readonly Dictionary<string, object?>? _params;

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

    // WithParam/WithParams are immutable builders: each returns a NEW descriptor with a cloned params bag.
    // This keeps shared/cached descriptors (e.g. static MessageDescriber instances) from being mutated by callers.
    public ErrorDescriptor WithParam(string key, object? value)
    {
        var copy = _CloneParams();
        copy[key] = value;

        return new ErrorDescriptor(Code, Description, copy, Severity);
    }

    public ErrorDescriptor WithParams(IReadOnlyDictionary<string, object?> values)
    {
        var copy = _CloneParams();

        foreach (var (key, value) in values)
        {
            copy[key] = value;
        }

        return new ErrorDescriptor(Code, Description, copy, Severity);
    }

    private Dictionary<string, object?> _CloneParams()
    {
        return _params is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(_params, StringComparer.OrdinalIgnoreCase);
    }

    public void Deconstruct(out string code, out string description, out ValidationSeverity severity)
    {
        code = Code;
        description = Description;
        severity = Severity;
    }

    public override string ToString() => $"{Code}: {Description}";
}

public enum ValidationSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2,
}
