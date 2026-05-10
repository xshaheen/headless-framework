// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Headless.Abstractions;

public interface IGuidGenerator
{
    /// <summary>Creates a new <see cref="Guid"/>.</summary>
    Guid Create();
}

public sealed class SequentialAtEndGuidGenerator : IGuidGenerator
{
    public Guid Create() => SequentialGuid.NextSequentialAtEnd();
}

public sealed class SequentialAsStringGuidGenerator : IGuidGenerator
{
    public Guid Create() => SequentialGuid.NextSequentialAsString();
}

public sealed class SequentialAsBinaryGuidGenerator : IGuidGenerator
{
    public Guid Create() => SequentialGuid.NextSequentialAsBinary();
}
