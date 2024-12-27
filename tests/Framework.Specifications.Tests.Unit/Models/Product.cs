// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Models;

public sealed class Product
{
    public required string Name { get; set; }

    public decimal Price { get; set; }

    public required string Category { get; set; }

    public int Stock { get; set; }

    public Color Color { get; set; }

    public bool Available { get; set; }
}

public enum Color
{
    Default,
    Red,
    Green,
    Black,
    White,
}
