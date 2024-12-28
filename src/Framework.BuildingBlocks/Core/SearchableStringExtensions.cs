// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Cysharp.Text;
using static Framework.Text.ArabicLetters;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

/// <summary>Utility functions used by to prepare a text to search and index.</summary>
[PublicAPI]
// ReSharper disable IdentifierTypo
public static class SearchableStringExtensions
{
    /// <summary>
    /// Normalize string to search optimized.
    /// <list type="number">
    /// <item>Removing diacritics (to find their prototypes) and normalize the characters.</item>
    /// <item>Removing any non letter, digit, and spaces.</item>
    /// <item>Normalize white spaces to be one space and lower case.</item>
    /// <item>Its convert the digits to Arabic digits [0-9].</item>
    /// <item>Normalize arabic characters (e.g. alef like, waw like, yeh like, ...).</item>
    /// </list>
    /// </summary>
    [SystemPure]
    [JetBrainsPure]
    public static string SearchString(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return _SearchString(input.Normalize(NormalizationForm.FormD).AsSpan());
    }

    private static string _SearchString(this ReadOnlySpan<char> input)
    {
        input = input.Trim();

        if (input.IsEmpty)
        {
            return string.Empty;
        }

        var builder = ZString.CreateStringBuilder();

        var hasPreviousSpaces = false;

        foreach (var c in input)
        {
            // Only allow letters, digits, spaces
            if (!_IsLetterOrDigitOrSpace(c))
            {
                continue;
            }

            // Append only single space instead of whitespaces
            if (char.IsWhiteSpace(c))
            {
                hasPreviousSpaces = true;

                continue;
            }

            if (hasPreviousSpaces)
            {
                builder.Append(' ');
                hasPreviousSpaces = false;
            }

            // Normalize digits
            if (char.IsDigit(c))
            {
                builder.Append(char.GetNumericValue(c));

                continue;
            }

            // Stem arabic characters
            if (_ArabicStems.Contains(c))
            {
                continue;
            }

            // Normalize Arabic characters
            if (_TryArabicNormalize(c, out var normalized))
            {
                builder.Append(normalized);

                continue;
            }

            // Apply arabic normalization
            if (_ArabicExpander.TryGetValue(c, out var expanded))
            {
                builder.Append(expanded);

                continue;
            }

            // Normalize to lower case
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static readonly FrozenSet<char> _ArabicStems = [Tatweel, SmallAlef, SmallWaw, SmallYeh];

    private static readonly FrozenDictionary<char, string> _ArabicExpander = new Dictionary<char, string>
    {
        [LamAlef] = $"{Lam}{Alef}",
        [LamAlefHamzaAbove] = $"{Lam}{Alef}",
        [LamAlefHamzaBelow] = $"{Lam}{Alef}",
        [LamAlefMaddaAbove] = $"{Lam}{Alef}",
    }.ToFrozenDictionary();

    #region Helpers

    private static bool _IsLetterOrDigitOrSpace(char c)
    {
        return CharUnicodeInfo.GetUnicodeCategory(c)
            is UnicodeCategory.LowercaseLetter
                or UnicodeCategory.UppercaseLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.SpaceSeparator
                or UnicodeCategory.DecimalDigitNumber;
    }

    #endregion

    #region Arabic Normalizer

    private static bool _TryArabicNormalize(char c, out char normalized)
    {
        // Alef like
        if (_IsAlefLike(c))
        {
            normalized = Alef;
            return true;
        }

        // Waw like
        if (_IsWawLike(c))
        {
            normalized = Waw;

            return true;
        }

        // Yeh like
        if (_IsYehLike(c))
        {
            normalized = Yeh;

            return true;
        }

        // Lam like
        if (_IsLamLike(c))
        {
            normalized = Lam;

            return true;
        }

        // Heh like
        if (_IsHehLike(c))
        {
            normalized = Heh;

            return true;
        }

        // Kaf like
        if (_IsKafLike(c))
        {
            normalized = Kaf;

            return true;
        }

        // Dal like
        if (_IsDalLike(c))
        {
            normalized = Dal;

            return true;
        }

        // Feh like
        if (_IsFehLike(c))
        {
            normalized = Feh;

            return true;
        }

        // Qaf like
        if (_IsQafLike(c))
        {
            normalized = Qaf;

            return true;
        }

        // Beh like
        if (_IsBehLike(c))
        {
            normalized = Beh;

            return true;
        }

        // Reh like
        if (_IsRehLike(c))
        {
            normalized = Reh;

            return true;
        }

        // Jeem like
        if (_IsJeemLike(c))
        {
            normalized = Jeem;

            return true;
        }

        // Dad like
        if (_IsDadLike(c))
        {
            normalized = Dad;

            return true;
        }

        normalized = c;

        return false;
    }

    private static bool _IsYehLike(char c)
    {
        return c switch
        {
            YehHamza => true, // Yeh With Ham
            AlefMaksura => true,
            'ی' => true, // Farsi Yeh
            'ۍ' => true, // Yeh With Tail
            'ێ' => true, // Yeh With Small V
            'ؠ' => true, // Arabic Letter Kashmiri Yeh
            'ې' => true, // E
            'ۑ' => true, // Yeh With Three Dots Below
            'ؽ' => true, // Farsi Yeh With Inverted V
            'ؾ' => true, // Farsi Yeh With Two Dots Above
            'ؿ' => true, // Farsi Yeh With Three Dots Above
            _ => false,
        };
    }

    private static bool _IsAlefLike(char c)
    {
        return c switch
        {
            AlefMadda => true,
            AlefHamzaAbove => true,
            AlefHamzaBelow => true,
            AlefWasla => true,
            HamzaAbove => true,
            HamzaBelow => true,
            _ => false,
        };
    }

    private static bool _IsWawLike(char c)
    {
        return c switch
        {
            'ۏ' => true, // Waw With Dot Above
            'ۋ' => true, // Ve
            'ۊ' => true, // Waw With Two Dots Above
            'ۉ' => true, // Kirghiz Yu
            'ۈ' => true, // Yu
            'ۇ' => true, // U
            'ۆ' => true, // Oe
            'ۅ' => true, // Kirghiz Oe
            'ۄ' => true, // Waw With Ring
            _ => false,
        };
    }

    private static bool _IsLamLike(char c)
    {
        return c switch
        {
            'ڵ' => true, // Lam With Small V
            'ڶ' => true, // Lam With Dot Above
            'ڷ' => true, // Lam With Three Dots Above
            'ڸ' => true, // Lam With Three Dots Below
            _ => false,
        };
    }

    private static bool _IsHehLike(char c)
    {
        return c switch
        {
            TehMarbuta => true, // Common spell errors
            'ۿ' => true, // Heh With Inverted V
            'ھ' => true, // Heh Doachashmee
            'ۀ' => true, // Heh With Yeh Above
            'ہ' => true, // Heh Goal
            'ۂ' => true, // Heh Goal With Hamza Above
            'ۃ' => true, // Teh Marbuta Goal
            _ => false,
        };
    }

    private static bool _IsKafLike(char c)
    {
        return c switch
        {
            // Kaf like
            'ػ' => true, // Keheh With Two Dots Above
            'ؼ' => true, // Keheh With Three Dots Below
            'ک' => true, // Keheh
            'ڪ' => true, // Swash Kaf
            'ګ' => true, // Kaf With Ring
            'ڬ' => true, // Kaf With Dot Above
            'ڭ' => true, // Ng
            'ڮ' => true, // Kaf With Three Dots Below
            'گ' => true, // Gaf
            'ڰ' => true, // Gaf With Ring
            'ڱ' => true, // Ngoeh
            'ڲ' => true, // Gaf With Two Dots Below
            'ڳ' => true, // Gueh
            'ڴ' => true, // Gaf With Three Dots Above
            _ => false,
        };
    }

    private static bool _IsDalLike(char c)
    {
        return c switch
        {
            Thal => true,
            'ۮ' => true, // Dal With Inverted V
            'ڈ' => true, // Ddal
            'ډ' => true, // Dal With Ring
            'ڊ' => true, // Dal With Dot Below
            'ڋ' => true, // Dal With Dot Below And Small Tah
            'ڍ' => true, // Ddahal
            'ڌ' => true, // Dahal
            'ڎ' => true, // Dul
            'ڏ' => true, // Dal With Three Dots Above Downwards
            'ڐ' => true, // Dal With Four Dots Above
            _ => false,
        };
    }

    private static bool _IsFehLike(char c)
    {
        return c switch
        {
            'ڥ' => true,
            _ => false,
        };
    }

    private static bool _IsQafLike(char c)
    {
        return c switch
        {
            'ٯ' => true, // Dotless Qaf
            _ => false,
        };
    }

    private static bool _IsBehLike(char c)
    {
        return c switch
        {
            'ٮ' => true, // Dotless Beh
            'ﺑ' => true,
            _ => false,
        };
    }

    private static bool _IsRehLike(char c)
    {
        return c switch
        {
            'ۯ' => true, // Reh With Inverted V
            _ => false,
        };
    }

    private static bool _IsJeemLike(char c)
    {
        return c switch
        {
            'ﺞ' => true,
            _ => false,
        };
    }

    private static bool _IsDadLike(char c)
    {
        return c switch
        {
            'ۻ' => true,
            _ => false,
        };
    }

    #endregion

    #region Arabic Stop Words

    private static bool _IsStopWord(this ReadOnlySpan<char> word)
    {
        return word.Trim() switch
        {
            "من" => true,
            "ومن" => true,
            "منها" => true,
            "منه" => true,
            "في" => true,
            "وفي" => true,
            "فيها" => true,
            "فيه" => true,
            "و" => true,
            "ف" => true,
            "ثم" => true,
            "او" => true,
            "أو" => true,
            "ب" => true,
            "بها" => true,
            "به" => true,
            "ا" => true,
            "أ" => true,
            "اى" => true,
            "اي" => true,
            "أي" => true,
            "أى" => true,
            "لا" => true,
            "ولا" => true,
            "الا" => true,
            "ألا" => true,
            "إلا" => true,
            "لكن" => true,
            "ما" => true,
            "وما" => true,
            "كما" => true,
            "فما" => true,
            "عن" => true,
            "مع" => true,
            "اذا" => true,
            "إذا" => true,
            "ان" => true,
            "أن" => true,
            "إن" => true,
            "انها" => true,
            "أنها" => true,
            "إنها" => true,
            "انه" => true,
            "أنه" => true,
            "إنه" => true,
            "بان" => true,
            "بأن" => true,
            "فان" => true,
            "فأن" => true,
            "وان" => true,
            "وأن" => true,
            "وإن" => true,
            "التى" => true,
            "التي" => true,
            "الذى" => true,
            "الذي" => true,
            "الذين" => true,
            "الى" => true,
            "الي" => true,
            "إلى" => true,
            "إلي" => true,
            "على" => true,
            "عليها" => true,
            "عليه" => true,
            "اما" => true,
            "أما" => true,
            "إما" => true,
            "ايضا" => true,
            "أيضا" => true,
            "كل" => true,
            "وكل" => true,
            "لم" => true,
            "ولم" => true,
            "لن" => true,
            "ولن" => true,
            "هى" => true,
            "هي" => true,
            "هو" => true,
            "وهى" => true,
            "وهي" => true,
            "وهو" => true,
            "فهى" => true,
            "فهي" => true,
            "فهو" => true,
            "انت" => true,
            "أنت" => true,
            "لك" => true,
            "لها" => true,
            "له" => true,
            "هذه" => true,
            "هذا" => true,
            "تلك" => true,
            "ذلك" => true,
            "هناك" => true,
            "كانت" => true,
            "كان" => true,
            "يكون" => true,
            "تكون" => true,
            "وكانت" => true,
            "وكان" => true,
            "غير" => true,
            "بعض" => true,
            "قد" => true,
            "نحو" => true,
            "بين" => true,
            "بينما" => true,
            "منذ" => true,
            "ضمن" => true,
            "حيث" => true,
            "الان" => true,
            "الآن" => true,
            "خلال" => true,
            "بعد" => true,
            "قبل" => true,
            "حتى" => true,
            "عند" => true,
            "عندما" => true,
            "لدى" => true,
            "جميع" => true,
            _ => false,
        };
    }

    #endregion

    #region Arabic Stemmers

    private static int _LengthOfArabicSuffixStems(this ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return 0;
        }

        return word switch
        {
            [Heh, Alef] => 2, // ها
            [Alef, Noon] => 2, // ان
            [Alef, Teh] => 2, // ات
            [Waw, Noon] => 2, // ون
            [Yeh, Noon] => 2, // ين
            [Yeh, Heh] => 2, // يه
            [Yeh, TehMarbuta] => 2, // ية
            [Heh] => 1, // ه
            [TehMarbuta] => 1, // ة
            [Yeh] => 1, // ي
            _ => 0,
        };
    }

    private static int _LengthOfArabicPrefixStems(this ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return 0;
        }

        return word switch
        {
            [Alef, Lam] => 2, // ال
            [Waw, Alef, Lam] => 3, // وال
            [Beh, Alef, Lam] => 3, // بال
            [Kaf, Alef, Lam] => 3, // كال
            [Feh, Alef, Lam] => 3, // فال
            [Lam, Lam] => 2, // لل
            [Waw] => 1, // و
            _ => 0,
        };
    }

    #endregion
}
