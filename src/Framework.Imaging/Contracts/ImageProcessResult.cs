// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;

namespace Framework.Imaging.Contracts;

public abstract class ImageProcessResult<T>
{
    protected const string FailedError = "The image processing failed.";
    protected const string UnsupportedError = "The given image format is not supported.";
    protected const string CannotReadError = "Cannot read the image.";

    [MemberNotNullWhen(false, nameof(Error))]
    [MemberNotNullWhen(true, nameof(Result))]
    public bool IsDone => State is ImageProcessState.Done;

    public ImageProcessState State { get; protected init; }

    public T? Result { get; protected init; }

    public string? Error { get; protected init; }
}
