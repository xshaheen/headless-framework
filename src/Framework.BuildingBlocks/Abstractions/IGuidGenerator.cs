// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;

namespace Framework.Abstractions;

public interface IGuidGenerator
{
    /// <summary>Creates a new <see cref="Guid"/>.</summary>
    Guid Create();
}

/// <inheritdoc cref="SequentialGuid.NextSequentialAtEnd"/>
public sealed class SequentialAtEndGuidGenerator : IGuidGenerator
{
    public Guid Create() => SequentialGuid.NextSequentialAtEnd();
}

/// <inheritdoc cref="SequentialGuid.NextSequentialAsString"/>
public sealed class SequentialAsStringGuidGenerator : IGuidGenerator
{
    public Guid Create() => SequentialGuid.NextSequentialAsString();
}

/// <inheritdoc cref="SequentialGuid.NextSequentialAsBinary"/>
public sealed class SequentialAsBinaryGuidGenerator : IGuidGenerator
{
    public Guid Create() => SequentialGuid.NextSequentialAsBinary();
}
