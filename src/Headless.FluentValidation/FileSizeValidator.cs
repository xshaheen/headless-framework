// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;

namespace Headless.FluentValidation;

/// <summary>
/// Bounds a file's size. A <see langword="null"/> file passes. A typed validator rather than a predicate, so API
/// documentation can read the bounds through <see cref="IFileSizeValidator"/>.
/// </summary>
/// <remarks>
/// <paramref name="getLength"/> reads the size, so one implementation serves every file type a rule accepts. When
/// <paramref name="formatSizes"/> is set, a failure adds the <c>{MinSize}</c> or <c>{MaxSize}</c> and
/// <c>{TotalLength}</c> message arguments, each formatted in megabytes under the current culture.
/// </remarks>
internal sealed class FileSizeValidator<T, TFile>(
    Func<TFile, long> getLength,
    long? minBytes,
    long? maxBytes,
    bool formatSizes
) : PropertyValidator<T, TFile?>, IFileSizeValidator
    where TFile : class
{
    private const double _BytesPerMegabyte = 1048576d;

    public override string Name => "FileSizeValidator";

    public long? MinBytes { get; } = minBytes;

    public long? MaxBytes { get; } = maxBytes;

    public override bool IsValid(ValidationContext<T> context, TFile? value)
    {
        if (value is null)
        {
            return true;
        }

        var length = getLength(value);

        if (MinBytes is { } min && length < min)
        {
            _AppendSizes(context, "MinSize", min, length);

            return false;
        }

        if (MaxBytes is { } max && length > max)
        {
            _AppendSizes(context, "MaxSize", max, length);

            return false;
        }

        return true;
    }

    private void _AppendSizes(ValidationContext<T> context, string boundName, long bound, long length)
    {
        if (!formatSizes)
        {
            return;
        }

        context
            .MessageFormatter.AppendArgument(
                boundName,
                (bound / _BytesPerMegabyte).ToString("N1", CultureInfo.CurrentCulture)
            )
            .AppendArgument("TotalLength", (length / _BytesPerMegabyte).ToString("N1", CultureInfo.CurrentCulture));
    }
}
