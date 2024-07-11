using System.Text;
using Framework.BuildingBlocks.Helpers;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Utility functions used by to prepare a text to search and index.</summary>
[PublicAPI]
public static class SearchableStringExtensions
{
    /// <summary>
    /// Normalize string to search optimized. Remove any accent from the
    /// string and Convert any digit to english equivalent.
    /// </summary>
    [SystemPure, JetBrainsPure]
    public static string SearchString(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var withoutAccentAndSymbols =
            from ch in input[..].Trim().OneSpace().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            let category = CharUnicodeInfo.GetUnicodeCategory(ch)
            where
                category
                    is UnicodeCategory.LowercaseLetter
                        or UnicodeCategory.OtherLetter
                        or UnicodeCategory.SpaceSeparator
                        or UnicodeCategory.DecimalDigitNumber
            select ch;

        var str = withoutAccentAndSymbols.ToInvariantDigits();

        return str._SupportAr();
    }

    #region Helpers

    private static readonly Dictionary<char, string> _ReplacesMap =
        new()
        {
            // Alef
            [ArabicLetters.AlefMadda] = ArabicLetters.Alef.ToString(),
            [ArabicLetters.AlefHamzaAbove] = ArabicLetters.Alef.ToString(),
            [ArabicLetters.AlefHamzaBelow] = ArabicLetters.Alef.ToString(),
            [ArabicLetters.AlefWasla] = ArabicLetters.Alef.ToString(),
            [ArabicLetters.HamzaAbove] = ArabicLetters.Alef.ToString(),
            [ArabicLetters.HamzaBelow] = ArabicLetters.Alef.ToString(),
            // Hamza
            [ArabicLetters.WawHamza] = ArabicLetters.Hamza.ToString(),
            [ArabicLetters.YehHamza] = ArabicLetters.Hamza.ToString(),
            // Lam alef
            [ArabicLetters.LamAlef] = ArabicLetters.Lam + ArabicLetters.Alef.ToString(),
            [ArabicLetters.LamAlefHamzaAbove] = ArabicLetters.Lam + ArabicLetters.Alef.ToString(),
            [ArabicLetters.LamAlefHamzaBelow] = ArabicLetters.Lam + ArabicLetters.Alef.ToString(),
            [ArabicLetters.LamAlefMaddaAbove] = ArabicLetters.Lam + ArabicLetters.Alef.ToString(),
            // Uthmani symbols
            [ArabicLetters.SmallAlef] = "",
            [ArabicLetters.SmallWaw] = "",
            [ArabicLetters.SmallYeh] = "",
            // Common spell errors
            [ArabicLetters.TehMarbuta] = ArabicLetters.Heh.ToString(),
            [ArabicLetters.AlefMaksura] = ArabicLetters.Yeh.ToString(),
            // Yeh like
            ['ی'] = ArabicLetters.Yeh.ToString(), // Farsi Yeh
            ['ۍ'] = ArabicLetters.Yeh.ToString(), // Yeh With Tail
            ['ێ'] = ArabicLetters.Yeh.ToString(), // Yeh With Small V
            ['ؠ'] = ArabicLetters.Yeh.ToString(), // Arabic Letter Kashmiri Yeh
            ['ې'] = ArabicLetters.Yeh.ToString(), // E
            ['ۑ'] = ArabicLetters.Yeh.ToString(), // Yeh With Three Dots Below
            ['ؽ'] = ArabicLetters.Yeh.ToString(), // Farsi Yeh With Inverted V
            ['ؾ'] = ArabicLetters.Yeh.ToString(), // Farsi Yeh With Two Dots Above
            ['ؿ'] = ArabicLetters.Yeh.ToString(), // Farsi Yeh With Three Dots Above
            // Waw like
            ['ۏ'] = ArabicLetters.Waw.ToString(), // Waw With Dot Above
            ['ۋ'] = ArabicLetters.Waw.ToString(), // Ve
            ['ۊ'] = ArabicLetters.Waw.ToString(), // Waw With Two Dots Above
            ['ۉ'] = ArabicLetters.Waw.ToString(), // Kirghiz Yu
            ['ۈ'] = ArabicLetters.Waw.ToString(), // Yu
            ['ۇ'] = ArabicLetters.Waw.ToString(), // U
            ['ۆ'] = ArabicLetters.Waw.ToString(), // Oe
            ['ۅ'] = ArabicLetters.Waw.ToString(), // Kirghiz Oe
            ['ۄ'] = ArabicLetters.Waw.ToString(), // Waw With Ring
            // Lam like
            ['ڵ'] = ArabicLetters.Lam.ToString(), // Lam With Small V
            ['ڶ'] = ArabicLetters.Lam.ToString(), // Lam With Dot Above
            ['ڷ'] = ArabicLetters.Lam.ToString(), // Lam With Three Dots Above
            ['ڸ'] = ArabicLetters.Lam.ToString(), // Lam With Three Dots Below
            // Kaf like
            ['ػ'] = ArabicLetters.Kaf.ToString(), // Keheh With Two Dots Above
            ['ؼ'] = ArabicLetters.Kaf.ToString(), // Keheh With Three Dots Below
            ['ک'] = ArabicLetters.Kaf.ToString(), // Keheh
            ['ڪ'] = ArabicLetters.Kaf.ToString(), // Swash Kaf
            ['ګ'] = ArabicLetters.Kaf.ToString(), // Kaf With Ring
            ['ڬ'] = ArabicLetters.Kaf.ToString(), // Kaf With Dot Above
            ['ڭ'] = ArabicLetters.Kaf.ToString(), // Ng
            ['ڮ'] = ArabicLetters.Kaf.ToString(), // Kaf With Three Dots Below
            ['گ'] = ArabicLetters.Kaf.ToString(), // Gaf
            ['ڰ'] = ArabicLetters.Kaf.ToString(), // Gaf With Ring
            ['ڱ'] = ArabicLetters.Kaf.ToString(), // Ngoeh
            ['ڲ'] = ArabicLetters.Kaf.ToString(), // Gaf With Two Dots Below
            ['ڳ'] = ArabicLetters.Kaf.ToString(), // Gueh
            ['ڴ'] = ArabicLetters.Kaf.ToString(), // Gaf With Three Dots Above
            // Hef like
            ['ۿ'] = ArabicLetters.Heh.ToString(), // Heh With Inverted V
            ['ھ'] = ArabicLetters.Heh.ToString(), // Heh Doachashmee
            ['ۀ'] = ArabicLetters.Heh.ToString(), // Heh With Yeh Above
            ['ہ'] = ArabicLetters.Heh.ToString(), // Heh Goal
            ['ۂ'] = ArabicLetters.Heh.ToString(), // Heh Goal With Hamza Above
            ['ۃ'] = ArabicLetters.Heh.ToString(), // Teh Marbuta Goal
            // Dal like
            ['ۮ'] = ArabicLetters.Dal.ToString(), // Dal With Inverted V
            ['ڈ'] = ArabicLetters.Dal.ToString(), // Ddal
            ['ډ'] = ArabicLetters.Dal.ToString(), // Dal With Ring
            ['ڊ'] = ArabicLetters.Dal.ToString(), // Dal With Dot Below
            ['ڋ'] = ArabicLetters.Dal.ToString(), // Dal With Dot Below And Small Tah
            ['ڍ'] = ArabicLetters.Dal.ToString(), // Ddahal
            ['ڌ'] = ArabicLetters.Thal.ToString(), // Dahal
            ['ڎ'] = ArabicLetters.Thal.ToString(), // Dul
            ['ڏ'] = ArabicLetters.Thal.ToString(), // Dal With Three Dots Above Downwards
            ['ڐ'] = ArabicLetters.Thal.ToString(), // Dal With Four Dots Above
            // Qaf like
            ['ٯ'] = ArabicLetters.Qaf.ToString(), // Dotless Qaf
            // Beh like
            ['ٮ'] = ArabicLetters.Beh.ToString(), // Dotless Beh
            ['ﺑ'] = ArabicLetters.Beh.ToString(),
            // Reh like
            ['ۯ'] = ArabicLetters.Reh.ToString(), // Reh With Inverted V
            // Feh like
            ['ڥ'] = ArabicLetters.Feh.ToString(),
            // Dad like
            ['ۻ'] = ArabicLetters.Dad.ToString(),
            // Jeem like
            ['ﺞ'] = ArabicLetters.Jeem.ToString(),
        };

    private static readonly List<char> _RemovesList = [ArabicLetters.Tatweel];

    /// <summary>
    /// Opt in arabic normalization for search.
    /// <para>Extra normalization is:</para>
    /// <para>* Replace teh marbuta to heh.</para>
    /// <para>* Replace alef maksura (dotless yeh) to yeh.</para>
    /// <para>* Removal of tatweel (stretching character)..</para>
    /// </summary>
    [SystemPure, JetBrainsPure]
    private static string _SupportAr(this string input)
    {
        var sb = new StringBuilder();

        foreach (var cur in input)
        {
            if (_RemovesList.Contains(cur))
            {
                continue;
            }

            sb.Append(_ReplacesMap.TryGetValue(cur, out var replace) ? replace : cur);
        }

        return sb.ToString();
    }

    #endregion
}
