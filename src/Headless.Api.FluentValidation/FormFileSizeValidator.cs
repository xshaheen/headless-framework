// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Validators;
using Headless.Api;
using Microsoft.AspNetCore.Http;

namespace Headless.FluentValidation;

/// <summary>
/// Bounds an uploaded file's size. A <see langword="null"/> file passes. A typed validator rather than a predicate, so
/// API documentation can read the bounds through <see cref="IFormFileSizeValidator"/>.
/// </summary>
/// <remarks>
/// When <paramref name="formatSizes"/> is set, a failure adds the <c>{MinSize}</c> or <c>{MaxSize}</c> and
/// <c>{TotalLength}</c> message arguments, each formatted in megabytes under the current culture.
/// </remarks>
internal sealed class FormFileSizeValidator<T>(long? minBytes, long? maxBytes, bool formatSizes)
    : PropertyValidator<T, IFormFile?>,
        IFormFileSizeValidator
{
    private const double _BytesPerMegabyte = 1048576d;

    public override string Name => "FormFileSizeValidator";

    public long? MinBytes { get; } = minBytes;

    public long? MaxBytes { get; } = maxBytes;

    public override bool IsValid(ValidationContext<T> context, IFormFile? value)
    {
        if (value is null)
        {
            return true;
        }

        if (MinBytes is { } min && value.Length < min)
        {
            _AppendSizes(context, "MinSize", min, value.Length);

            return false;
        }

        if (MaxBytes is { } max && value.Length > max)
        {
            _AppendSizes(context, "MaxSize", max, value.Length);

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
