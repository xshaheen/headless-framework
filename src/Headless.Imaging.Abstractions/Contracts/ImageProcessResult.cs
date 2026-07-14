// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Imaging;

/// <summary>Base class for the result of an image processing operation.</summary>
/// <typeparam name="T">The type of the processed result, such as a <see cref="Stream"/>.</typeparam>
[PublicAPI]
public abstract class ImageProcessResult<T>
{
    protected const string FailedError = "The image processing failed.";
    protected const string UnsupportedError = "The given image format is not supported.";
    protected const string CannotReadError = "Cannot read the image.";

    /// <summary>
    /// Gets a value indicating whether the operation completed successfully.
    /// When <see langword="true"/>, <see cref="Result"/> is non-null and <see cref="Error"/> is null.
    /// When <see langword="false"/>, <see cref="Error"/> is non-null and <see cref="Result"/> is null.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    [MemberNotNullWhen(true, nameof(Result))]
    public bool IsDone => State is ImageProcessState.Done;

    /// <summary>Gets the outcome of the processing operation.</summary>
    public ImageProcessState State { get; protected init; }

    /// <summary>
    /// Gets the processed content when <see cref="IsDone"/> is <see langword="true"/>;
    /// otherwise <see langword="null"/>.
    /// </summary>
    public T? Result { get; protected init; }

    /// <summary>
    /// Gets a human-readable error description when <see cref="IsDone"/> is <see langword="false"/>;
    /// otherwise <see langword="null"/>.
    /// </summary>
    public string? Error { get; protected init; }
}
