// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Validators;

namespace Headless.Api;

/// <summary>
/// A FluentValidation property validator that limits the declared content type of an uploaded file, so API
/// documentation can describe the accepted types without depending on the package that implements the rule.
/// </summary>
/// <remarks>
/// The <c>ContentTypes</c> rule of <c>Headless.Api.FluentValidation</c> implements it. Headless.OpenApi writes the types
/// to the multipart encoding of the file part and to its description.
/// </remarks>
[PublicAPI]
public interface IFormFileContentTypeValidator : IPropertyValidator
{
    /// <summary>The accepted media types, compared case-insensitively with the file's declared content type.</summary>
    IReadOnlyList<string> ContentTypes { get; }
}
