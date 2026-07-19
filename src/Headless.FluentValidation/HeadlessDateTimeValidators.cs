// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.FluentValidation.Resources;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace FluentValidation;

/// <summary>
/// FluentValidation extension rules for validating <see cref="DateTime"/>, <see cref="DateTimeOffset"/>,
/// and <see cref="DateOnly"/> properties relative to the current instant.
/// </summary>
/// <remarks>
/// <para>
/// Every rule reads "now" from a <see cref="TimeProvider"/> <b>at validation time</b> (defaulting to
/// <see cref="TimeProvider.System"/>), so the same validator instance stays correct over time and can
/// be made deterministic in tests by passing a fake provider.
/// </para>
/// <para>
/// For <see cref="DateTime"/>, the value is normalized to UTC before comparison:
/// <see cref="DateTimeKind.Local"/> is converted and <see cref="DateTimeKind.Unspecified"/> is treated
/// as UTC. For <see cref="DateOnly"/>, comparisons use the current UTC date.
/// </para>
/// <para>
/// <c>NotInThePast</c> accepts the present instant or later; <c>NotInTheFuture</c> accepts the present
/// instant or earlier. <c>InThePast</c> / <c>InTheFuture</c> are strict (the present instant fails both).
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessDateTimeValidators
{
    extension<T>(IRuleBuilder<T, DateTimeOffset> rule)
    {
        /// <summary>Validates that the value is strictly before the current instant.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset> InThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value < provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInPast());
        }

        /// <summary>Validates that the value is strictly after the current instant.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset> InTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value > provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInFuture());
        }

        /// <summary>Validates that the value is the current instant or later.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset> NotInThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value >= provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInPast());
        }

        /// <summary>Validates that the value is the current instant or earlier.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset> NotInTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value <= provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInFuture());
        }
    }

    extension<T>(IRuleBuilder<T, DateTimeOffset?> rule)
    {
        /// <summary>Validates that the value, when present, is strictly before the current instant.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset?> InThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value < provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInPast());
        }

        /// <summary>Validates that the value, when present, is strictly after the current instant.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset?> InTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value > provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInFuture());
        }

        /// <summary>Validates that the value, when present, is the current instant or later.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset?> NotInThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value >= provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInPast());
        }

        /// <summary>Validates that the value, when present, is the current instant or earlier.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTimeOffset?> NotInTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value <= provider.GetUtcNow())
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInFuture());
        }
    }

    extension<T>(IRuleBuilder<T, DateTime> rule)
    {
        /// <summary>Validates that the value is strictly before the current instant (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime> InThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => _ToUtc(value) < provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInPast());
        }

        /// <summary>Validates that the value is strictly after the current instant (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime> InTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => _ToUtc(value) > provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInFuture());
        }

        /// <summary>Validates that the value is the current instant or later (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime> NotInThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => _ToUtc(value) >= provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInPast());
        }

        /// <summary>Validates that the value is the current instant or earlier (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime> NotInTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => _ToUtc(value) <= provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInFuture());
        }
    }

    extension<T>(IRuleBuilder<T, DateTime?> rule)
    {
        /// <summary>Validates that the value, when present, is strictly before the current instant (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime?> InThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || _ToUtc(value.Value) < provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInPast());
        }

        /// <summary>Validates that the value, when present, is strictly after the current instant (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime?> InTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || _ToUtc(value.Value) > provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInFuture());
        }

        /// <summary>Validates that the value, when present, is the current instant or later (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime?> NotInThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || _ToUtc(value.Value) >= provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInPast());
        }

        /// <summary>Validates that the value, when present, is the current instant or earlier (compared in UTC).</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateTime?> NotInTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || _ToUtc(value.Value) <= provider.GetUtcNow().UtcDateTime)
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInFuture());
        }
    }

    extension<T>(IRuleBuilder<T, DateOnly> rule)
    {
        /// <summary>Validates that the value is strictly before the current UTC date.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly> InThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value < _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInPast());
        }

        /// <summary>Validates that the value is strictly after the current UTC date.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly> InTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value > _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInFuture());
        }

        /// <summary>Validates that the value is the current UTC date or later.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly> NotInThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value >= _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInPast());
        }

        /// <summary>Validates that the value is the current UTC date or earlier.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly> NotInTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value <= _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInFuture());
        }

        /// <summary>
        /// Validates that the value is a birth date at least <paramref name="minimumAge"/> whole years
        /// before the current UTC date.
        /// </summary>
        /// <param name="minimumAge">The minimum required age in whole years. Must be zero or positive.</param>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minimumAge"/> is negative.</exception>
        public IRuleBuilderOptions<T, DateOnly> MinimumAge(int minimumAge, TimeProvider? timeProvider = null)
        {
            Argument.IsPositiveOrZero(minimumAge);

            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(
                    (_, value, context) =>
                    {
                        if (_AgeInYears(value, _Today(provider)) >= minimumAge)
                        {
                            return true;
                        }

                        context.MessageFormatter.AppendArgument("MinimumAge", minimumAge);

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MinimumAge());
        }
    }

    extension<T>(IRuleBuilder<T, DateOnly?> rule)
    {
        /// <summary>Validates that the value, when present, is strictly before the current UTC date.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly?> InThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value < _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInPast());
        }

        /// <summary>Validates that the value, when present, is strictly after the current UTC date.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly?> InTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value > _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustBeInFuture());
        }

        /// <summary>Validates that the value, when present, is the current UTC date or later.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly?> NotInThePast(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value >= _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInPast());
        }

        /// <summary>Validates that the value, when present, is the current UTC date or earlier.</summary>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, DateOnly?> NotInTheFuture(TimeProvider? timeProvider = null)
        {
            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(value => value is null || value.Value <= _Today(provider))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MustNotBeInFuture());
        }

        /// <summary>
        /// Validates that the value, when present, is a birth date at least <paramref name="minimumAge"/>
        /// whole years before the current UTC date.
        /// </summary>
        /// <param name="minimumAge">The minimum required age in whole years. Must be zero or positive.</param>
        /// <param name="timeProvider">The time source. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <returns>The rule builder options for chaining.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minimumAge"/> is negative.</exception>
        public IRuleBuilderOptions<T, DateOnly?> MinimumAge(int minimumAge, TimeProvider? timeProvider = null)
        {
            Argument.IsPositiveOrZero(minimumAge);

            var provider = timeProvider ?? TimeProvider.System;

            return rule.Must(
                    (_, value, context) =>
                    {
                        if (value is null || _AgeInYears(value.Value, _Today(provider)) >= minimumAge)
                        {
                            return true;
                        }

                        context.MessageFormatter.AppendArgument("MinimumAge", minimumAge);

                        return false;
                    }
                )
                .WithErrorDescriptor(FluentValidatorErrorDescriber.DateTimes.MinimumAge());
        }
    }

    private static DateTime _ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Unspecified kind has no offset information; treat it as UTC for a deterministic comparison.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }

    private static DateOnly _Today(TimeProvider provider)
    {
        return DateOnly.FromDateTime(provider.GetUtcNow().UtcDateTime);
    }

    private static int _AgeInYears(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;

        if (birthDate > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }
}
