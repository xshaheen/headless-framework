// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Stable general error codes emitted by the built-in <see cref="ApiResultError"/> types.</summary>
[PublicAPI]
public static class ApiResultErrorCodes
{
    /// <summary>An unclassified expected error in the framework's general descriptor namespace.</summary>
    public const string Default = "g:error";

    /// <summary>Caller is authenticated but is not permitted to perform the operation.</summary>
    public const string Forbidden = "g:forbidden";

    /// <summary>A field value failed validation without a more specific descriptor code.</summary>
    public const string ValidationFailed = "g:validation_failed";
}
