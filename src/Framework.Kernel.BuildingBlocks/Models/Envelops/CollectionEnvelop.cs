#pragma warning disable IDE0130, CA2225
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

public sealed record CollectionEnvelop<T>(IReadOnlyCollection<T> Items)
{
    public static implicit operator CollectionEnvelop<T>(T[]? operand) => new(operand ?? (IReadOnlyCollection<T>)[]);

    public static implicit operator CollectionEnvelop<T>(HashSet<T>? operand) =>
        new(operand ?? (IReadOnlyCollection<T>)[]);

    public static implicit operator CollectionEnvelop<T>(List<T>? operand) =>
        new(operand ?? (IReadOnlyCollection<T>)[]);
}

public static class CollectionEnvelop
{
    public static CollectionEnvelop<T> FromTArray<T>(T[]? operand) => operand;

    public static CollectionEnvelop<T> FromHashSet<T>(HashSet<T>? operand) => operand;

    public static CollectionEnvelop<T> FromList<T>(List<T>? operand) => operand;

    public static CollectionEnvelop<T> FromEnumerable<T>(IEnumerable<T>? operand) => operand?.ToArray();
}
