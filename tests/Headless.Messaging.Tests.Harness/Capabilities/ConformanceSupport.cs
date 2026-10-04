// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Capabilities;

/// <summary>Declares whether a transport conformance scenario has executable broker-backed evidence.</summary>
[PublicAPI]
public enum ConformanceStatus
{
    Supported,
    Unsupported,
    NotApplicable,
}

/// <summary>Describes the evidence status for one provider conformance scenario.</summary>
[PublicAPI]
public sealed record ConformanceSupport(ConformanceStatus Status, string? Rationale, string? IssueUrl)
{
    public static ConformanceSupport Supported { get; } = new(ConformanceStatus.Supported, null, null);

    public static ConformanceSupport Unsupported(string rationale, string issueUrl)
    {
        return new ConformanceSupport(ConformanceStatus.Unsupported, rationale, issueUrl);
    }

    public static ConformanceSupport NotApplicable(string rationale)
    {
        return new ConformanceSupport(ConformanceStatus.NotApplicable, rationale, null);
    }

    public IReadOnlyList<string> GetValidationErrors(TransportConformanceScenario scenario)
    {
        var errors = new List<string>();

        if (
            Status is ConformanceStatus.Unsupported or ConformanceStatus.NotApplicable
            && string.IsNullOrWhiteSpace(Rationale)
        )
        {
            errors.Add($"{scenario} {Status} cells require a non-empty rationale.");
        }

        if (Status == ConformanceStatus.Unsupported && !_IsValidIssueUrl(IssueUrl))
        {
            errors.Add($"{scenario} Unsupported cells require an absolute HTTP(S) issue URL.");
        }

        return errors;
    }

    private static bool _IsValidIssueUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            );
    }
}
