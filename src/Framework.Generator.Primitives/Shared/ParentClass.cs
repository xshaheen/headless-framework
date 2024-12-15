// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Generator.Primitives.Shared;

public sealed class ParentClass(string keyword, string name, string constraints, ParentClass? child)
{
    public ParentClass? Child { get; } = child;

    public string Keyword { get; } = keyword;

    public string Name { get; } = name;

    public string Constraints { get; } = constraints;
}
