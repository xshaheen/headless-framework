using Framework.BuildingBlocks.Helpers;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Arabic char extensions.</summary>
[PublicAPI]
public static class ArCharExtensions
{
    /// <summary>Checks for Arabic Shadda Mark.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsShadda(this char c)
    {
        return c == ArabicLetters.Shadda;
    }

    /// <summary>Checks for Arabic Tatweel letter modifier.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsTatweel(this char c)
    {
        return c == ArabicLetters.Tatweel;
    }

    /// <summary>Checks for Arabic Tatweel letter modifier.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsTanwin(this char c)
    {
        return c.In(ArabicLetters.Tanwin);
    }

    /// <summary>Checks for Arabic Tashkeel.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsTashkeel(this char c)
    {
        return c.In(ArabicLetters.Tashkeel);
    }

    /// <summary>Checks for Arabic moon letters.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsMoon(this char c)
    {
        return c.In(ArabicLetters.Moon);
    }

    /// <summary>Checks for Arabic sun letters.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsSun(this char c)
    {
        return c.In(ArabicLetters.Sun);
    }

    /// <summary>Checks for Arabic hamza like letter.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsHamza(this char c)
    {
        return c.In(ArabicLetters.Hamzat);
    }

    /// <summary>Checks for Arabic alef like letter.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsAlef(this char c)
    {
        return c.In(ArabicLetters.Alefat);
    }

    /// <summary>Checks for Arabic yeh like letter.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsYehLike(this char c)
    {
        return c.In(ArabicLetters.YehLike);
    }

    /// <summary>Checks for Arabic waw like letter.</summary>
    /// <param name="c">arabic unicode char</param>
    [SystemPure, JetBrainsPure]
    public static bool IsWawLike(this char c)
    {
        return c.In(ArabicLetters.WawLike);
    }
}
