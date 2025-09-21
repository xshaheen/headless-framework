// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Framework.IO;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.IO;

[PublicAPI]
public static class ReadOnlySequenceExtensions
{
    /// <summary>
    /// Exposes a <see cref="ReadOnlySequence{T}"/> of <see cref="byte"/> as a <see cref="Stream"/>.
    /// </summary>
    /// <param name="readOnlySequence">The sequence of bytes to expose as a stream.</param>
    /// <returns>The readable stream.</returns>
    public static Stream ToStream(
        this ReadOnlySequence<byte> readOnlySequence,
        Action<object?>? disposeAction = null,
        object? disposeActionArg = null
    )
    {
        return new ReadOnlySequenceStream(readOnlySequence, disposeAction, disposeActionArg);
    }
}
