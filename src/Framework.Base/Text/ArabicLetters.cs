// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Text;

/// <summary>Arabic letters &amp; classification</summary>
[PublicAPI]
// ReSharper disable IdentifierTypo
public static class ArabicLetters
{
    #region Main Letters

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

    #endregion

    #region Small Letters

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

    #endregion

    #region Ligatures

    /// <summary>Lam alef 'ﻻ'.</summary>
    public const char LamAlef = '\ufefb';

    /// <summary>Lam alef with hamza above 'ﻷ'.</summary>
    public const char LamAlefHamzaAbove = '\ufef7';

    /// <summary>Lam alef with hamza below 'ﻹ'.</summary>
    public const char LamAlefHamzaBelow = '\ufef9';

    /// <summary>Lam alef with madda above 'ﻵ'.</summary>
    public const char LamAlefMaddaAbove = '\ufef5';

    #endregion

    #region Diacritics

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

    #endregion

    #region Marks and Signs

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

    #endregion

    #region Ornaments

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

    #endregion

    public static FrozenSet<char> Sun { get; } = [Teh, Theh, Dal, Thal, Reh, Zain, Seen, Sheen, Sad, Dad, Tah, Zah, Lam, Noon];

    public static FrozenSet<char> Moon { get; } = [Hamza,AlefMadda,AlefHamzaAbove,AlefHamzaBelow,Alef,Beh,Jeem,Hah,Khah,Ain,Ghain,Feh,Qaf,Kaf,Meem,Heh,Waw,Yeh];

    public static FrozenSet<char> Tashkeel { get; } = [Fathatan, Dammatan, Kasratan, Fatha, Damma, Kasra, Sukun, Shadda];

    public static FrozenSet<char> Harakat { get; } = [Fathatan, Dammatan, Kasratan, Fatha, Damma, Kasra, Sukun];

    public static FrozenSet<char> Tanwin { get; } = [Fathatan, Dammatan, Kasratan];

    public static FrozenSet<char> Liguatures { get; } = [LamAlef, LamAlefHamzaAbove, LamAlefHamzaBelow, LamAlefMaddaAbove];

    public static FrozenSet<char> Hamzat { get; } = [Hamza, WawHamza, YehHamza, HamzaAbove, HamzaBelow, AlefHamzaAbove, AlefHamzaBelow];

    public static FrozenSet<char> Alefat { get; } = [Alef, AlefMadda, AlefHamzaAbove, AlefHamzaBelow, AlefWasla, AlefMaksura, SmallAlef];

    public static FrozenSet<char> WawLike { get; } = [Waw, WawHamza, SmallWaw];

    public static FrozenSet<char> YehLike { get; } = [Yeh, YehHamza, AlefMaksura, SmallYeh];

    public static FrozenSet<char> TehLike { get; } = [Teh, TehMarbuta];
}
