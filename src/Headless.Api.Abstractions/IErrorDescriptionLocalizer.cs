// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api;

/// <summary>
/// Localizes the description of an <see cref="ErrorDescriptor"/> before it is written to a
/// <c>ProblemDetails</c> response.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IProblemDetailsCreator.Normalize"/> calls this for every descriptor in the response's
/// <c>error</c> and <c>errors</c> extensions, so the factories on <see cref="IProblemDetailsCreator"/>,
/// the Minimal API <c>ToHttpResult</c> bridge, and the MVC <c>ToActionResult</c> bridge all apply it.
/// Framework error codes already resolve their descriptions from localized resources; implement this
/// to localize your own application's codes.
/// </para>
/// <para>
/// Key the lookup on <see cref="ErrorDescriptor.Code"/>. The code, severity, and parameters are never
/// changed, so clients that branch on the code are unaffected. The default registration returns
/// <see langword="null"/> for every descriptor. The implementation is resolved as a singleton and
/// is called concurrently, so read the culture per call (for example from
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>) rather than capturing it.
/// </para>
/// </remarks>
public interface IErrorDescriptionLocalizer
{
    /// <summary>Returns the localized description for <paramref name="error"/>.</summary>
    /// <param name="error">The descriptor about to be written to the response.</param>
    /// <returns>
    /// The localized description, or <see langword="null"/> to keep <see cref="ErrorDescriptor.Description"/>.
    /// </returns>
    string? Localize(ErrorDescriptor error);
}
