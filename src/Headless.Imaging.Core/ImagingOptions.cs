// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Imaging;

/// <summary>Options that govern the core imaging pipeline.</summary>
public sealed class ImagingOptions
{
    /// <summary>
    /// Gets or sets the resize mode applied when a caller passes
    /// <see cref="ImageResizeMode.Default"/> in <see cref="ImageResizeArgs.Mode"/>.
    /// Defaults to <see cref="ImageResizeMode.None"/>, which leaves the image at its original size.
    /// </summary>
    public ImageResizeMode DefaultResizeMode { get; set; } = ImageResizeMode.None;
}

internal sealed class ImagingOptionsValidator : AbstractValidator<ImagingOptions>
{
    public ImagingOptionsValidator()
    {
        RuleFor(x => x.DefaultResizeMode).IsInEnum();
    }
}
