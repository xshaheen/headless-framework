using Framework.BuildingBlocks.Helpers;
using Framework.BuildingBlocks.Helpers.IdGenerators;

namespace Framework.BuildingBlocks.Abstractions;

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
