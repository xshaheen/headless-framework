// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;

namespace Headless.FluentValidation;

/// <summary>
/// A FluentValidation property validator that bounds the size of a file, so API documentation can describe the bound
/// without depending on the package that implements the rule.
/// </summary>
/// <remarks>
/// The <c>FileNotEmpty</c>, <c>GreaterThanOrEqualTo</c>, and <c>LessThanOrEqualTo</c> rules for an ASP.NET Core
/// <c>IFormFile</c> in Headless.Api.FluentValidation implement it. Headless.OpenApi adds the bound to the
/// description of the file part in a multipart request body.
/// </remarks>
[PublicAPI]
public interface IFileSizeValidator : IPropertyValidator
{
    /// <summary>The smallest accepted file size in bytes, or <see langword="null"/> when the rule sets no lower bound.</summary>
    long? MinBytes { get; }

    /// <summary>The largest accepted file size in bytes, or <see langword="null"/> when the rule sets no upper bound.</summary>
    long? MaxBytes { get; }
}
