// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Numeric clamping extensions that delegate to <see cref="Math.Clamp(int,int,int)"/> and its overloads.</summary>
[PublicAPI]
public static class NumberExtensions
{
    /// <summary>Clamps <paramref name="value"/> to the inclusive range <paramref name="min"/>..<paramref name="max"/>.</summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    /// <returns><paramref name="value"/> clamped to the range; <paramref name="min"/> if below it, <paramref name="max"/> if above it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="min"/> is greater than <paramref name="max"/>.</exception>
    [SystemPure]
    [JetBrainsPure]
    public static int Clamp(this int value, int min, int max)
    {
        return Math.Clamp(value, min, max);
    }

    /// <inheritdoc cref="Clamp(int,int,int)"/>
    [SystemPure]
    [JetBrainsPure]
    public static long Clamp(this long value, long min, long max)
    {
        return Math.Clamp(value, min, max);
    }

    /// <inheritdoc cref="Clamp(int,int,int)"/>
    [SystemPure]
    [JetBrainsPure]
    public static float Clamp(this float value, float min, float max)
    {
        return Math.Clamp(value, min, max);
    }

    /// <inheritdoc cref="Clamp(int,int,int)"/>
    [SystemPure]
    [JetBrainsPure]
    public static double Clamp(this double value, double min, double max)
    {
        return Math.Clamp(value, min, max);
    }

    /// <inheritdoc cref="Clamp(int,int,int)"/>
    [SystemPure]
    [JetBrainsPure]
    public static decimal Clamp(this decimal value, decimal min, decimal max)
    {
        return Math.Clamp(value, min, max);
    }
}
