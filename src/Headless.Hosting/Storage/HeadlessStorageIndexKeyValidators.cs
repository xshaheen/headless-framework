// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Linq.Expressions;
using Headless.Checks;

namespace FluentValidation;

/// <summary>FluentValidation rules for configurable column lengths that feed an index key.</summary>
[PublicAPI]
public static class HeadlessStorageIndexKeyValidators
{
    /// <summary>The widest key, in bytes, SQL Server accepts in a nonclustered index.</summary>
    public const int SqlServerIndexKeyMaxBytes = 1700;

    /// <summary>
    /// Refuses column lengths whose combined <c>nvarchar</c> width, two bytes per character, would exceed
    /// <see cref="SqlServerIndexKeyMaxBytes"/> in the index named <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// SQL Server creates such an index with only a warning, then fails each insert or update whose key is wider than
    /// the limit, so the overflow would surface on the first long value instead of at startup. It is the strictest
    /// supported database, so every provider applies this bound and a configuration valid on one stays valid on the
    /// others. A length that is not positive is left to the column-length rule.
    /// </remarks>
    /// <param name="validator">The validator of the storage options.</param>
    /// <param name="index">The index's name in messages, such as <c>SettingValues (Name, ProviderName, ProviderKey)</c>.</param>
    /// <param name="columns">The length option of each indexed column.</param>
    /// <returns>The rule builder options for chaining.</returns>
    public static IRuleBuilderOptions<T, T> AddSqlServerIndexKeyRule<T>(
        this AbstractValidator<T> validator,
        string index,
        params Expression<Func<T, int>>[] columns
    )
    {
        Argument.IsNotNull(validator);
        Argument.IsNotNullOrWhiteSpace(index);
        Argument.IsNotNullOrEmpty(columns);

        var names = columns.Select(_MemberName).ToArray();
        var lengths = columns.Select(column => column.Compile()).ToArray();

        long keyBytes(T options) => lengths.Sum(length => 2L * Math.Max(length(options), 0));

        return validator
            .RuleFor(options => options)
            .Must(options => keyBytes(options) <= SqlServerIndexKeyMaxBytes)
            .OverridePropertyName(string.Join(" + ", names))
            .WithMessage(
                (options, _) =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The index {index} would be up to {keyBytes(options)} bytes wide, "
                            + $"past SQL Server's {SqlServerIndexKeyMaxBytes}-byte index key limit: "
                            + $"{string.Join(" + ", names.Select((name, i) => $"{name} ({lengths[i](options)})"))} "
                            + $"must total at most {SqlServerIndexKeyMaxBytes / 2} characters."
                    )
            );
    }

    private static string _MemberName<T>(Expression<Func<T, int>> column)
    {
        return column.Body is MemberExpression member
            ? member.Member.Name
            : throw new ArgumentException($"'{column}' must select a property.", nameof(column));
    }
}
