// Copyright (c) Mahmoud Shaheen. All rights reserved.

// ReSharper disable IdentifierTypo
namespace Framework.BuildingBlocks.Helpers.Ar;

/// <summary>Arabic letters &amp; classification</summary>
[PublicAPI]
public static class ArabicLetters
{
    /// <summary>Alef 'ا'.</summary>
    public const char Alef = '\u0627';

    /// <summary>Beh 'ب'.</summary>
    public const char Beh = '\u0628';

    /// <summary>Teh marbuta 'ة'.</summary>
    public const char TehMarbuta = '\u0629';

    /// <summary>Teh 'ت'.</summary>
    public const char Teh = '\u062a';

    /// <summary>Theh 'ث'.</summary>
    public const char Theh = '\u062b';

    /// <summary>Jeem 'ج'.</summary>
    public const char Jeem = '\u062c';

    /// <summary>Heh 'ح'.</summary>
    public const char Hah = '\u062d';

    /// <summary>Khah 'خ'.</summary>
    public const char Khah = '\u062e';

    /// <summary>Dal 'د'.</summary>
    public const char Dal = '\u062f';

    /// <summary>Thal 'ذ'.</summary>
    public const char Thal = '\u0630';

    /// <summary>Reh 'ر'.</summary>
    public const char Reh = '\u0631';

    /// <summary>Zain 'ز'.</summary>
    public const char Zain = '\u0632';

    /// <summary>Seen 'س'.</summary>
    public const char Seen = '\u0633';

    /// <summary>Sheen 'ش'.</summary>
    public const char Sheen = '\u0634';

    /// <summary>Sad 'ص'.</summary>
    public const char Sad = '\u0635';

    /// <summary>Dad 'ض'.</summary>
    public const char Dad = '\u0636';

    /// <summary>Tah 'ط'.</summary>
    public const char Tah = '\u0637';

    /// <summary>Zah 'ظ'.</summary>
    public const char Zah = '\u0638';

    /// <summary>Ain 'ع'.</summary>
    public const char Ain = '\u0639';

    /// <summary>Ghain 'غ'.</summary>
    public const char Ghain = '\u063a';

    /// <summary>Feh 'ف'.</summary>
    public const char Feh = '\u0641';

    /// <summary>Qaf 'ق'.</summary>
    public const char Qaf = '\u0642';

    /// <summary>Kaf 'ك'.</summary>
    public const char Kaf = '\u0643';

    /// <summary>Lam 'ل'.</summary>
    public const char Lam = '\u0644';

    /// <summary>Meem 'م'.</summary>
    public const char Meem = '\u0645';

    /// <summary>Noon 'ن'.</summary>
    public const char Noon = '\u0646';

    /// <summary>Heh 'ه'.</summary>
    public const char Heh = '\u0647';

    /// <summary>Waw 'و'.</summary>
    public const char Waw = '\u0648';

    /// <summary>Yeh 'ي'.</summary>
    public const char Yeh = '\u064a';

    /// <summary>Alef maksura 'ى'.</summary>
    public const char AlefMaksura = '\u0649';

    /// <summary>Hamza 'ء'.</summary>
    public const char Hamza = '\u0621';

    /// <summary>Alef with madda above 'آ'.</summary>
    public const char AlefMadda = '\u0622';

    /// <summary>Alef with hamza above 'أ'.</summary>
    public const char AlefHamzaAbove = '\u0623';

    /// <summary>Alef with hamza below 'إ'.</summary>
    public const char AlefHamzaBelow = '\u0625';

    /// <summary>Alef wasla 'ٱ'.</summary>
    public const char AlefWasla = '\u0671';

    /// <summary>Waw with hamza above 'ؤ'.</summary>
    public const char WawHamza = '\u0624';

    /// <summary>Yeh with hamza above 'ئ'.</summary>
    public const char YehHamza = '\u0626';

    // Small Letters

    /// <summary>Small alef ' ٰ'.</summary>
    public const char SmallAlef = '\u0670';

    /// <summary>Small alef 'ۥ '.</summary>
    public const char SmallWaw = '\u06E5';

    /// <summary>Small yeh 'ۦ '.</summary>
    public const char SmallYeh = '\u06E6';

    /// <summary>Madda above ' ٓ'.</summary>
    public const char MaddaAbove = '\u0653';

    /// <summary>Hamza above ' ٔ'.</summary>
    public const char HamzaAbove = '\u0654';

    /// <summary>Hamza below ' ٕ'.</summary>
    public const char HamzaBelow = '\u0655';

    // Ligatures

    /// <summary>Lam alef 'ﻻ'.</summary>
    public const char LamAlef = '\ufefb';

    /// <summary>Lam alef with hamza above 'ﻷ'.</summary>
    public const char LamAlefHamzaAbove = '\ufef7';

    /// <summary>Lam alef with hamza below 'ﻹ'.</summary>
    public const char LamAlefHamzaBelow = '\ufef9';

    /// <summary>Lam alef with madda above 'ﻵ'.</summary>
    public const char LamAlefMaddaAbove = '\ufef5';

    // Diacritics

    /// <summary>Fathatan ' ً_'.</summary>
    public const char Fathatan = '\u064b';

    /// <summary>Dammatan ' ٌ_'.</summary>
    public const char Dammatan = '\u064c';

    /// <summary>Kasratan ' ٍ_'.</summary>
    public const char Kasratan = '\u064d';

    /// <summary>Fatha ' َ_'.</summary>
    public const char Fatha = '\u064e';

    /// <summary>Damma ' ُ_'.</summary>
    public const char Damma = '\u064f';

    /// <summary>Kasra ' ِ_'.</summary>
    public const char Kasra = '\u0650';

    /// <summary>Shadda ' ّ_'.</summary>
    public const char Shadda = '\u0651';

    /// <summary>Sukun ' ْ_'.</summary>
    public const char Sukun = '\u0652';

    // Marks and Signs

    /// <summary>Arabic Comma '،'.</summary>
    public const char Comma = '\u060C';

    /// <summary>Arabic Semicolon '؛'.</summary>
    public const char Semicolon = '\u061B';

    /// <summary>Arabic full stop '۔'.</summary>
    public const char FullStop = '\u06d4';

    /// <summary>Tatweel 'ـ'.</summary>
    public const char Tatweel = '\u0640';

    /// <summary>Arabic Question Mark '؟'.</summary>
    public const char Question = '\u061F';

    /// <summary>Arabic Sign Sanah '؁'.</summary>
    public const char SanahSign = '\u0601';

    /// <summary>Arabic Number Sanah '؀'.</summary>
    public const char NumberSign = '\u0640';

    /// <summary>Triple Dot Punctuation Mark '؞'.</summary>
    public const char TripleDotPunctuation = '\u061E';

    // Ornaments

    /// <summary>Star of Rub El Hizb Mark '۞'.</summary>
    public const char StarOfRubElHizb = '\u06DE';

    /// <summary>End of Ayah Mark '۝'.</summary>
    public const char EndOfAyah = '\u06DD';

    /// <summary>Place Of Sajdah '۩'.</summary>
    public const char PlaceOfSajdah = '\u06E9';

    /// <summary>Ornate left parenthesis '﴾'.</summary>
    public const char OrnateLeftParenthesis = '\u06E9';

    /// <summary>Ornate Right parenthesis '﴿'.</summary>
    public const char OrnateRightParenthesis = '\u06E9';

    /// <summary>Five Pointed start '٭'.</summary>
    public const char Star = '\u06E9';

    public static FrozenSet<char> Sun { get; } =
        new[] { Teh, Theh, Dal, Thal, Reh, Zain, Seen, Sheen, Sad, Dad, Tah, Zah, Lam, Noon }.ToFrozenSet();

    public static FrozenSet<char> Moon { get; } =
        new[]
        {
            Hamza,
            AlefMadda,
            AlefHamzaAbove,
            AlefHamzaBelow,
            Alef,
            Beh,
            Jeem,
            Hah,
            Khah,
            Ain,
            Ghain,
            Feh,
            Qaf,
            Kaf,
            Meem,
            Heh,
            Waw,
            Yeh,
        }.ToFrozenSet();

    public static FrozenSet<char> Tashkeel { get; } =
        new[] { Fathatan, Dammatan, Kasratan, Fatha, Damma, Kasra, Sukun, Shadda }.ToFrozenSet();

    public static FrozenSet<char> Harakat { get; } =
        new[] { Fathatan, Dammatan, Kasratan, Fatha, Damma, Kasra, Sukun }.ToFrozenSet();

    public static FrozenSet<char> Tanwin { get; } = new[] { Fathatan, Dammatan, Kasratan }.ToFrozenSet();

    public static FrozenSet<char> Liguatures { get; } =
        new[] { LamAlef, LamAlefHamzaAbove, LamAlefHamzaBelow, LamAlefMaddaAbove }.ToFrozenSet();

    public static FrozenSet<char> Hamzat { get; } =
        new[] { Hamza, WawHamza, YehHamza, HamzaAbove, HamzaBelow, AlefHamzaAbove, AlefHamzaBelow }.ToFrozenSet();

    public static FrozenSet<char> Alefat { get; } =
        new[] { Alef, AlefMadda, AlefHamzaAbove, AlefHamzaBelow, AlefWasla, AlefMaksura, SmallAlef }.ToFrozenSet();

    public static FrozenSet<char> WawLike { get; } = new[] { Waw, WawHamza, SmallWaw }.ToFrozenSet();

    public static FrozenSet<char> YehLike { get; } = new[] { Yeh, YehHamza, AlefMaksura, SmallYeh }.ToFrozenSet();

    public static FrozenSet<char> TehLike { get; } = new[] { Teh, TehMarbuta }.ToFrozenSet();
}
