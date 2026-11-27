// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.RateLimiting;

/// <summary>
/// The Rate limits module's one command, Reset, on a <c>rate-limit-subject</c>: it removes the current window's
/// counter of one subject for one purpose, which lifts a lockout until the window would have closed anyway.
/// </summary>
/// <remarks>
/// The target id is <c>{purpose}:{fingerprint}</c> and the subject is the command's <c>subject</c> argument, because the
/// shell writes ids and reasons to the operator log as they are and never writes arguments. The shell enforces the
/// operate permission, the confirmation, the reason, and the argument schema before this class runs; this class then
/// checks that the subject matches the fingerprint and that the reason does not repeat it. A refused call changes
/// nothing and answers with the refusal, which the shell returns as <c>409</c> without an operator-log entry.
/// </remarks>
internal sealed class RateLimitingCommands(
    string moduleId,
    IAttemptLimiter limiter,
    AttemptSubjectFingerprinter fingerprinter,
    AttemptLimiterOptions limiterOptions
)
{
    public const string SubjectKind = "rate-limit-subject";
    public const string SubjectArgument = "subject";
    public const string LimitArgument = "limit";
    public const string WindowSecondsArgument = "windowSeconds";
    public const string NoAttemptsReason = "No attempts in the current window";

    // AttemptSubjectFingerprinter writes 32 bytes as unpadded base64url: always 43 characters.
    private const int _FingerprintLength = 43;

    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly IAttemptLimiter _limiter = Argument.IsNotNull(limiter);
    private readonly AttemptSubjectFingerprinter _fingerprinter = Argument.IsNotNull(fingerprinter);
    private readonly AttemptLimiterOptions _limiterOptions = Argument.IsNotNull(limiterOptions);

    public string ResetId => _moduleId + ".reset";

    public IReadOnlyList<DashboardCommand> Commands =>
        field ??= [
            new()
            {
                Id = ResetId,
                Label = "Reset this window",
                ObjectKind = SubjectKind,
                Confirmation = DashboardConfirmation.Confirm,
                RequiresReason = true,
                Arguments =
                [
                    new()
                    {
                        Name = SubjectArgument,
                        Label = "Subject",
                        Kind = DashboardArgumentKind.Text,
                        Required = true,
                        MaxLength = RateLimitingLookupBodyValidator.MaxSubjectLength,
                    },
                    new()
                    {
                        Name = LimitArgument,
                        Label = "Limit",
                        Kind = DashboardArgumentKind.WholeNumber,
                        Minimum = 1,
                        Maximum = int.MaxValue,
                    },
                    new()
                    {
                        Name = WindowSecondsArgument,
                        Label = "Window (seconds)",
                        Kind = DashboardArgumentKind.WholeNumber,
                        Minimum = 1,
                        Maximum = RateLimitingQuotas.MaxWindowSeconds,
                    },
                ],
            },
        ];

    /// <summary>
    /// Allows Reset on each of this module's subjects. Whether the subject has attempts cannot be read without the
    /// subject itself, so the view adds that reason from its lookup; the shell refuses a user who may only view.
    /// </summary>
    public IReadOnlyList<DashboardCommandAvailability> GetAllowed(IReadOnlyList<ObjectRef> targets)
    {
        return
        [
            .. targets
                .Where(target =>
                    string.Equals(target.Module, _moduleId, StringComparison.Ordinal)
                    && string.Equals(target.Kind, SubjectKind, StringComparison.Ordinal)
                )
                .Select(target => new DashboardCommandAvailability
                {
                    Target = target,
                    CommandId = ResetId,
                    Allowed = true,
                }),
        ];
    }

    public async Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    )
    {
        // One subject argument names one subject, so a call names one target.
        if (request.Ids.Count != 1)
        {
            const string many = "Reset one subject at a time.";

            return new DashboardCommandResult
            {
                Rows =
                [
                    .. request.Ids.Select(static id => new DashboardRowResult
                    {
                        Id = id,
                        Outcome = DashboardRowOutcome.Skipped,
                        Reason = many,
                    }),
                ],
                Refusal = many,
            };
        }

        var id = request.Ids[0];

        if (_Check(request, id, out var purpose, out var subject) is { } refusal)
        {
            return _Refuse(id, refusal);
        }

        var resolution = RateLimitingQuotas.Resolve(
            _limiterOptions,
            purpose,
            _ReadInt32(request, LimitArgument),
            _ReadInt64(request, WindowSecondsArgument)
        );

        if (resolution.Quota is not { } quota)
        {
            return _Refuse(id, resolution.Refusal!);
        }

        bool removed;

        try
        {
            removed = await _limiter.ResetAsync(purpose, subject, quota, cancellationToken).ConfigureAwait(false);
        }
        catch (AttemptLimiterUnavailableException)
        {
            // A failed row, not a throw: the shell logs a failed attempt instead of answering 500. The store's answer
            // can be lost after it removed the counter, so the reason says the outcome is unknown rather than that
            // nothing changed, and names the purpose only.
            return _Row(id, DashboardRowOutcome.Failed, _UnknownOutcome(purpose));
        }

        return removed
            ? _Row(id, DashboardRowOutcome.Done, reason: null)
            : _Row(id, DashboardRowOutcome.Skipped, NoAttemptsReason);
    }

    private string? _Check(DashboardCommandRequest request, string id, out string purpose, out string subject)
    {
        purpose = subject = string.Empty;

        if (_Parse(id) is not var (parsedPurpose, fingerprint))
        {
            return "The target must be {purpose}:{fingerprint}.";
        }

        purpose = parsedPurpose;

        // The shell's schema check accepts an empty string for a required text argument.
        if (
            !request.Arguments.TryGetValue(SubjectArgument, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 } argument
        )
        {
            return "Reset needs the subject the target's fingerprint was computed from.";
        }

        subject = argument;

        // The shell writes the reason to the operator log as it is, so a reason that repeats the subject would put it
        // there.
        if (request.Reason is { } reason && reason.Contains(subject, StringComparison.OrdinalIgnoreCase))
        {
            return "The reason must not repeat the subject: the operator log records reasons as written.";
        }

        if (!string.Equals(_fingerprinter.Compute(purpose, subject), fingerprint, StringComparison.Ordinal))
        {
            return "The subject does not match the target's fingerprint; look the subject up again.";
        }

        return null;
    }

    // A purpose may itself contain ':', so the fingerprint is read from the end.
    private static (string Purpose, string Fingerprint)? _Parse(string id)
    {
        if (id.Length < _FingerprintLength + 2 || id[^(_FingerprintLength + 1)] != ':')
        {
            return null;
        }

        return (id[..^(_FingerprintLength + 1)], id[^_FingerprintLength..]);
    }

    private static string _UnknownOutcome(string purpose) =>
        $"The attempt limiter for '{purpose}' failed, so the reset's outcome is unknown: the store may have removed the "
        + "counter. Look the subject up again before you retry.";

    private static int? _ReadInt32(DashboardCommandRequest request, string name) =>
        request.Arguments.TryGetValue(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static long? _ReadInt64(DashboardCommandRequest request, string name) =>
        request.Arguments.TryGetValue(name, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static DashboardCommandResult _Refuse(string id, string refusal) =>
        new()
        {
            Rows =
            [
                new()
                {
                    Id = id,
                    Outcome = DashboardRowOutcome.Skipped,
                    Reason = refusal,
                },
            ],
            Refusal = refusal,
        };

    private static DashboardCommandResult _Row(string id, DashboardRowOutcome outcome, string? reason) =>
        new()
        {
            Rows =
            [
                new()
                {
                    Id = id,
                    Outcome = outcome,
                    Reason = reason,
                },
            ],
        };
}
