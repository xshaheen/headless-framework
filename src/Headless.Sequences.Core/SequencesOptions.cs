// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sequences;

/// <summary>The numbering policy of every counter: one per registered name, and a default for the rest.</summary>
[PublicAPI]
public sealed class SequencesOptions
{
    /// <summary>
    /// Gets or sets the policy of every name without its own entry in <see cref="Policies" />. Default: fast mode,
    /// start 1, step 1.
    /// </summary>
    public SequencePolicy DefaultPolicy { get; set; } = new();

    /// <summary>Gets the per-name policies, keyed by counter name and compared ordinally.</summary>
    public IDictionary<string, SequencePolicy> Policies { get; } =
        new Dictionary<string, SequencePolicy>(StringComparer.Ordinal);

    internal SequencePolicy GetPolicy(string name)
    {
        return Policies.TryGetValue(name, out var policy) ? policy : DefaultPolicy;
    }
}

internal sealed class SequencesOptionsValidator : AbstractValidator<SequencesOptions>
{
    public SequencesOptionsValidator()
    {
        RuleFor(x => x.DefaultPolicy).NotNull().SetValidator(new SequencePolicyValidator());

        RuleForEach(x => x.Policies)
            .ChildRules(entry =>
            {
                entry
                    .RuleFor(x => x.Key)
                    .NotEmpty()
                    .Must(static name => !string.IsNullOrWhiteSpace(name))
                    .WithMessage("A sequence name must not be blank.")
                    .Must(static name => !SequenceKeyText.HasSurroundingWhitespace(name))
                    .WithMessage("A sequence name must not start or end with whitespace.")
                    .MaximumLength(SequenceFieldLimits.NameMaxLength);

                entry.RuleFor(x => x.Value).NotNull().SetValidator(new SequencePolicyValidator());
            });
    }

    private sealed class SequencePolicyValidator : AbstractValidator<SequencePolicy>
    {
        public SequencePolicyValidator()
        {
            RuleFor(x => x.Step).GreaterThan(0);
            RuleFor(x => x.Mode).IsInEnum();
        }
    }
}
