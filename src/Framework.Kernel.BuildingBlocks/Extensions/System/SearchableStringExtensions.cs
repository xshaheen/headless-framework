// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using static Framework.Kernel.BuildingBlocks.Helpers.Ar.ArabicLetters;

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
    [SystemPure]
    [JetBrainsPure]
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

        return str._ToArabicSearchString();
    }

    #region Helpers

    /// <summary>
    /// Opt in arabic normalization for search.
    /// <para>Extra normalization is:</para>
    /// <para>* Replace teh marbuta to heh.</para>
    /// <para>* Replace alef maksura (dotless yeh) to yeh.</para>
    /// <para>* Removal of tatweel (stretching character)..</para>
    /// <list type="bullet">
    ///     <item><description>Removal of Arabic diacritics (the harakat)</description></item>
    ///     <item><description>Removal of tatweel (stretching character).</description></item>
    /// </list>
    /// </summary>
    [SystemPure]
    [JetBrainsPure]
    private static string _ToArabicSearchString(this string input)
    {
        var sb = new StringBuilder();

        foreach (var cur in input)
        {
            if (_ArabicStems.Contains(cur))
            {
                continue;
            }

            sb.Append(_ArabicNormalizations.TryGetValue(cur, out var replace) ? replace : cur);
        }

        return sb.ToString();
    }

    #endregion
    // The sequence below is how ISRI Stemmer finds roots without a Root Dictionary.

    // 1. Removing diacritics (to find their prototypes)
    // 2. Removing hamza and using its prototype
    // 3. Removing prefixes if they are 3 or 2 in length
    // 4. if the word starts with و, delete the waw
    // 5. alif-hamza also uses the alif prototype, not hamza
    // 6. Terminate if the stemmed token is 3 or less in length
    // 7. Depending on the length of the token, this is handled as following conditions
    // -- Length 4) If it matches the value of PR4 [specified in the paper], extract that value and terminate. Otherwise, compare suffix 1, prefix 1 against S1, P1 and delete it if it contains it and return.
    // -- Length 5) Similar to length 4 but with PR53, PR54
    // -- Length 6) Similar to length 4, but using PR63
    // -- Length 7) The goal here is to delete suffix 1, prefix 1. Process one more time in the same way as for length 6, then look at the value and return
    // https://github.com/bunseokbot/elasticsearch-arabic-dialect-plugin/blob/main/src/main/java/org/elasticsearch/index/analysis/stemmer/AbstractArabicISRIStemmer.java
    // https://github.com/msarhan/lucene-arabic-analyzer/blob/master/src/main/java/com/github/msarhan/lucene/ArabicNormalizer.java

    /*
      "decimal_digit", // Its convert the arabic digits to english digits
      "arabic_stop", // Its remove the stop words
      "arabic_normalization", // Its remove the diacritics from the arabic text and normalize the characters
      "arabic_keywords",
      "arabic_stemmer", // Its stem some of the known combinations from each token known combinations
     */

    /// <summary>
    /// Do the following normalization for Arabic text:
    /// <list type="bullet">
    /// <item>hamza with alef seat to a bare alef.</item>
    /// <item>combined lam alef with separate lam alef.</item>
    /// <item>dotless yeh (alef maksura) to yeh.</item>
    /// <item>teh marbuta to heh.</item>
    /// <item>yeh, waw, lam, kaf, heh, dal, beh, reh, feh, dad, jeem like.</item>
    /// </list>
    /// </summary>
    private static readonly FrozenDictionary<char, string> _ArabicNormalizations = _CreateArabicNormalizations();

    private static readonly FrozenSet<char> _ArabicStems = _CreateArabicStems();

    #region Helpers

    private static FrozenSet<char> _CreateArabicStems()
    {
        var set = new HashSet<char>
        {
            Tatweel,
            // Uthmani symbols
            SmallAlef,
            SmallWaw,
            SmallYeh,
        };

        return set.ToFrozenSet();
    }

    /*private static char[][] _CreateArabicPrefixStems()
    {
        return
        [
            [Alef, Lam], // ال
            [Waw, Alef, Lam], // وال
            [Beh, Alef, Lam], // بال
            [Kaf, Alef, Lam], // كال
            [Feh, Alef, Lam], // فال
            [Lam, Lam], // لل
            [Waw], // و
        ];
    }

    private static char[][] _CreateArabicSuffixStems()
    {
        return
        [
            [Heh, Alef], // ها
            [Alef, Noon], // ان
            [Alef, Teh], // ات
            [Waw, Noon], // ون
            [Yeh, Noon], // ين
            [Yeh, Heh], // يه
            [Yeh, TehMarbuta], // ية
            [Heh], // ه
            [TehMarbuta], // ة
            [Yeh], // ي
        ];
    }

    private static FrozenSet<string> _CreateArabicStopWords()
    {
        HashSet<string> arabicStopWords =
        [
            "من",
            "ومن",
            "منها",
            "منه",
            "في",
            "وفي",
            "فيها",
            "فيه",
            "و",
            "ف",
            "ثم",
            "او",
            "أو",
            "ب",
            "بها",
            "به",
            "ا",
            "أ",
            "اى",
            "اي",
            "أي",
            "أى",
            "لا",
            "ولا",
            "الا",
            "ألا",
            "إلا",
            "لكن",
            "ما",
            "وما",
            "كما",
            "فما",
            "عن",
            "مع",
            "اذا",
            "إذا",
            "ان",
            "أن",
            "إن",
            "انها",
            "أنها",
            "إنها",
            "انه",
            "أنه",
            "إنه",
            "بان",
            "بأن",
            "فان",
            "فأن",
            "وان",
            "وأن",
            "وإن",
            "التى",
            "التي",
            "الذى",
            "الذي",
            "الذين",
            "الى",
            "الي",
            "إلى",
            "إلي",
            "على",
            "عليها",
            "عليه",
            "اما",
            "أما",
            "إما",
            "ايضا",
            "أيضا",
            "كل",
            "وكل",
            "لم",
            "ولم",
            "لن",
            "ولن",
            "هى",
            "هي",
            "هو",
            "وهى",
            "وهي",
            "وهو",
            "فهى",
            "فهي",
            "فهو",
            "انت",
            "أنت",
            "لك",
            "لها",
            "له",
            "هذه",
            "هذا",
            "تلك",
            "ذلك",
            "هناك",
            "كانت",
            "كان",
            "يكون",
            "تكون",
            "وكانت",
            "وكان",
            "غير",
            "بعض",
            "قد",
            "نحو",
            "بين",
            "بينما",
            "منذ",
            "ضمن",
            "حيث",
            "الان",
            "الآن",
            "خلال",
            "بعد",
            "قبل",
            "حتى",
            "عند",
            "عندما",
            "لدى",
            "جميع",
        ];

        return arabicStopWords.ToFrozenSet();
    }*/

    private static FrozenDictionary<char, string> _CreateArabicNormalizations()
    {
        var map = new Dictionary<char, string>
        {
            // Alef
            [AlefMadda] = Alef.ToString(),
            [AlefHamzaAbove] = Alef.ToString(),
            [AlefHamzaBelow] = Alef.ToString(),
            [AlefWasla] = Alef.ToString(),
            [HamzaAbove] = Alef.ToString(),
            [HamzaBelow] = Alef.ToString(),
            // Lam alef
            [LamAlef] = Lam + Alef.ToString(),
            [LamAlefHamzaAbove] = Lam + Alef.ToString(),
            [LamAlefHamzaBelow] = Lam + Alef.ToString(),
            [LamAlefMaddaAbove] = Lam + Alef.ToString(),
            // Common spell errors
            [TehMarbuta] = Heh.ToString(),
            [AlefMaksura] = Yeh.ToString(),
            // Yeh like
            ['ی'] = Yeh.ToString(), // Farsi Yeh
            ['ۍ'] = Yeh.ToString(), // Yeh With Tail
            ['ێ'] = Yeh.ToString(), // Yeh With Small V
            ['ؠ'] = Yeh.ToString(), // Arabic Letter Kashmiri Yeh
            ['ې'] = Yeh.ToString(), // E
            ['ۑ'] = Yeh.ToString(), // Yeh With Three Dots Below
            ['ؽ'] = Yeh.ToString(), // Farsi Yeh With Inverted V
            ['ؾ'] = Yeh.ToString(), // Farsi Yeh With Two Dots Above
            ['ؿ'] = Yeh.ToString(), // Farsi Yeh With Three Dots Above
            // Waw like
            ['ۏ'] = Waw.ToString(), // Waw With Dot Above
            ['ۋ'] = Waw.ToString(), // Ve
            ['ۊ'] = Waw.ToString(), // Waw With Two Dots Above
            ['ۉ'] = Waw.ToString(), // Kirghiz Yu
            ['ۈ'] = Waw.ToString(), // Yu
            ['ۇ'] = Waw.ToString(), // U
            ['ۆ'] = Waw.ToString(), // Oe
            ['ۅ'] = Waw.ToString(), // Kirghiz Oe
            ['ۄ'] = Waw.ToString(), // Waw With Ring
            // Lam like
            ['ڵ'] = Lam.ToString(), // Lam With Small V
            ['ڶ'] = Lam.ToString(), // Lam With Dot Above
            ['ڷ'] = Lam.ToString(), // Lam With Three Dots Above
            ['ڸ'] = Lam.ToString(), // Lam With Three Dots Below
            // Kaf like
            ['ػ'] = Kaf.ToString(), // Keheh With Two Dots Above
            ['ؼ'] = Kaf.ToString(), // Keheh With Three Dots Below
            ['ک'] = Kaf.ToString(), // Keheh
            ['ڪ'] = Kaf.ToString(), // Swash Kaf
            ['ګ'] = Kaf.ToString(), // Kaf With Ring
            ['ڬ'] = Kaf.ToString(), // Kaf With Dot Above
            ['ڭ'] = Kaf.ToString(), // Ng
            ['ڮ'] = Kaf.ToString(), // Kaf With Three Dots Below
            ['گ'] = Kaf.ToString(), // Gaf
            ['ڰ'] = Kaf.ToString(), // Gaf With Ring
            ['ڱ'] = Kaf.ToString(), // Ngoeh
            ['ڲ'] = Kaf.ToString(), // Gaf With Two Dots Below
            ['ڳ'] = Kaf.ToString(), // Gueh
            ['ڴ'] = Kaf.ToString(), // Gaf With Three Dots Above
            // Heh like
            ['ۿ'] = Heh.ToString(), // Heh With Inverted V
            ['ھ'] = Heh.ToString(), // Heh Doachashmee
            ['ۀ'] = Heh.ToString(), // Heh With Yeh Above
            ['ہ'] = Heh.ToString(), // Heh Goal
            ['ۂ'] = Heh.ToString(), // Heh Goal With Hamza Above
            ['ۃ'] = Heh.ToString(), // Teh Marbuta Goal
            // Dal like
            ['ۮ'] = Dal.ToString(), // Dal With Inverted V
            ['ڈ'] = Dal.ToString(), // Ddal
            ['ډ'] = Dal.ToString(), // Dal With Ring
            ['ڊ'] = Dal.ToString(), // Dal With Dot Below
            ['ڋ'] = Dal.ToString(), // Dal With Dot Below And Small Tah
            ['ڍ'] = Dal.ToString(), // Ddahal
            ['ڌ'] = Thal.ToString(), // Dahal
            ['ڎ'] = Thal.ToString(), // Dul
            ['ڏ'] = Thal.ToString(), // Dal With Three Dots Above Downwards
            ['ڐ'] = Thal.ToString(), // Dal With Four Dots Above
            // Qaf like
            ['ٯ'] = Qaf.ToString(), // Dotless Qaf
            // Beh like
            ['ٮ'] = Beh.ToString(), // Dotless Beh
            ['ﺑ'] = Beh.ToString(),
            // Reh like
            ['ۯ'] = Reh.ToString(), // Reh With Inverted V
            // Feh like
            ['ڥ'] = Feh.ToString(),
            // Dad like
            ['ۻ'] = Dad.ToString(),
            // Jeem like
            ['ﺞ'] = Jeem.ToString(),
        };

        return map.ToFrozenDictionary();
    }

    #endregion
}
