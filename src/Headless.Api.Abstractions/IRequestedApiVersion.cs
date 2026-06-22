// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Provides the API version resolved from the current request.</summary>
public interface IRequestedApiVersion
{
    /// <summary>Gets the API version string resolved for the current request, or <see langword="null"/> when no version was specified or could be resolved.</summary>
    string? Current { get; }
}
