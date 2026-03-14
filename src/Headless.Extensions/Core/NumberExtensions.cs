// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class NumberExtensions
{
    [SystemPure]
    [JetBrainsPure]
    public static int Clamp(this int value, int min, int max) => Math.Clamp(value, min, max);

    [SystemPure]
    [JetBrainsPure]
    public static long Clamp(this long value, long min, long max) => Math.Clamp(value, min, max);

    [SystemPure]
    [JetBrainsPure]
    public static float Clamp(this float value, float min, float max) => Math.Clamp(value, min, max);

    [SystemPure]
    [JetBrainsPure]
    public static double Clamp(this double value, double min, double max) => Math.Clamp(value, min, max);

    [SystemPure]
    [JetBrainsPure]
    public static decimal Clamp(this decimal value, decimal min, decimal max) => Math.Clamp(value, min, max);
}
