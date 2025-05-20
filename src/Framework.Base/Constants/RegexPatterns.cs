// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Framework.Constants;

[PublicAPI]
public static partial class RegexPatterns
{
    public const int MatchTimeoutMilliseconds = 100;

    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds);

    /// <summary>
    ///  ref.: https://html.spec.whatwg.org/multipage/forms.html#valid-e-mail-address (HTML5 living standard, willful violation of RFC 3522)
    /// </summary>
    [GeneratedRegex(
        pattern: @"^[a-zA-Z0-9.!#$%&'*+\/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex EmailAddress { get; }

    /// <summary>
    /// https://en.wikipedia.org/wiki/Arabic_script_in_Unicode
    ///
    /// <para>
    /// Arabic (0600–06FF, 255 characters) --  is a Unicode block, containing the
    /// standard letters and the most common diacritics of the Arabic script, and
    /// the Arabic-Indic digits.
    /// See: https://www.unicode.org/charts/PDF/U0600.pdf
    /// </para>
    ///
    /// <para>
    /// Arabic Supplement (0750–077F, 48 characters) -- is a Unicode block that
    /// encodes Arabic letter variants used for writing non-Arabic languages,
    /// including languages of Pakistan and Africa, and old Persian.
    /// See: https://www.unicode.org/charts/PDF/U0750.pdf
    /// </para>
    ///
    /// <para>
    /// Arabic Presentation Forms-A (FB50–FDFF, 611 characters) --  a Unicode block encoding
    /// contextual forms and ligatures of letter variants needed for Persian, Urdu, Sindhi
    /// and Central Asian languages. This block also encodes 32 noncharacters in Unicode.
    /// See: https://www.unicode.org/charts/PDF/UFB50.pdf
    /// </para>
    ///
    /// <para>
    /// Arabic Presentation Forms-B (FE70–FEFF, 141 characters) -- a Unicode block encoding
    /// spacing forms of Arabic diacritics, and contextual letter forms. The special codepoint,
    /// ZWNBSP is also here, which is used as a BOM.
    /// See: https://www.unicode.org/charts/PDF/UFE70.pdf
    /// </para>
    ///
    /// <para>
    /// Arabic Extended-A (08A0–08FF, 84 characters) is a Unicode block encoding Qur'anic
    /// annotations and letter variants used for various non-Arabic languages.
    /// See: https://www.unicode.org/charts/PDF/U08A0.pdf
    /// </para>
    /// </summary>
    [GeneratedRegex(
        pattern: @"[\u0600-\u06FF\u0750-\u077F\uFB50-\uFDFF\uFE70-\uFEFF\u08A0–\u08FF]",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex ArabicCharacters { get; }

    ///<summary>Represent RTL characters range use it to check whither a string contains RTL characters.</summary>
    [GeneratedRegex(
        pattern: @"[\u0600-\u06ff]|[\u0750-\u077f]|[\ufb50-\ufbc1]|[\ufbd3-\ufd3f]|[\ufd50-\ufd8f]|[\ufd92-\ufdc7]|[\ufe70-\ufefc]|[\uFDF0-\uFDFD]",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex RtlCharacters { get; }

    /// <summary>Matches a 14-digit national ID.</summary>
    [GeneratedRegex(
        pattern: @"^[0-9]{14}$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex EgyptianNationalId { get; }

    /// <summary>Matches one or more whitespace characters.</summary>
    [GeneratedRegex(
        pattern: @"\s+",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Spaces { get; }

    /// <summary>Matches hidden characters not in the specified Unicode ranges.</summary>
    [GeneratedRegex(
        pattern: "[^\u0009\u000A\u000D\u0020-\uD7FF\uE000-\uFFFD]",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex HiddenChars { get; }

    /// <summary>Matches leading and trailing quotes.</summary>
    [GeneratedRegex(
        pattern: "^\\s*['\"]+|['\"]+\\s*$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Quotes { get; }

    /// <summary>Matches IPv4 IP address.</summary>
    [GeneratedRegex(
        pattern: @"^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Ip4 { get; }

    /// <summary>Matches IPv6 IP address.</summary>
    [GeneratedRegex(
        pattern: @"^(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,}|::(ffff(:0{1,4}){0,1}:){0,1}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])|([0-9a-fA-F]{1,4}:){1,4}:((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9]))$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Ip6 { get; }

    /// <summary>Match both IPv4, IPv6 addresses.</summary>
    [GeneratedRegex(
        pattern: @"((^\s*((([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5]))\s*$)|(^\s*((([0-9A-Fa-f]{1,4}:){7}([0-9A-Fa-f]{1,4}|:))|(([0-9A-Fa-f]{1,4}:){6}(:[0-9A-Fa-f]{1,4}|((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})|:))|(([0-9A-Fa-f]{1,4}:){5}(((:[0-9A-Fa-f]{1,4}){1,2})|:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})|:))|(([0-9A-Fa-f]{1,4}:){4}(((:[0-9A-Fa-f]{1,4}){1,3})|((:[0-9A-Fa-f]{1,4})?:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(([0-9A-Fa-f]{1,4}:){3}(((:[0-9A-Fa-f]{1,4}){1,4})|((:[0-9A-Fa-f]{1,4}){0,2}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(([0-9A-Fa-f]{1,4}:){2}(((:[0-9A-Fa-f]{1,4}){1,5})|((:[0-9A-Fa-f]{1,4}){0,3}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(([0-9A-Fa-f]{1,4}:){1}(((:[0-9A-Fa-f]{1,4}){1,6})|((:[0-9A-Fa-f]{1,4}){0,4}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:))|(:(((:[0-9A-Fa-f]{1,4}){1,7})|((:[0-9A-Fa-f]{1,4}){0,5}:((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}))|:)))(%.+)?\s*$))",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Ip { get; }

    /// <summary>Represent a URL with optional protocol.</summary>
    [GeneratedRegex(
        pattern: @"^((https?|ftp|file):\/\/)?([\da-z\.-]+)\.([a-z\.]{2,6})([\/\w \.-]*)*\/?$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Url { get; }

    ///<summary>Represent a URL with HTTP(s) protocol.</summary>
    [GeneratedRegex(
        pattern: @"^https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex HttpUrl { get; }

    /// <summary>Represent a YouTube video URL.</summary>
    [GeneratedRegex(
        pattern: @"^http(?:s?):\/\/(?:www\.)?youtu(?:be\.com\/watch\?v=|\.be\/)([\w\-_]*)(&(amp;)?[\w?=]*)?$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex YoutubeVideoUrl { get; }

    ///<summary>Represent ZIP Code.</summary>
    [GeneratedRegex(
        pattern: @"^[a-zA-Z0-9][a-zA-Z0-9\- ]{0,10}[a-zA-Z0-9]$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex ZipCode { get; }

    /// <summary>Represent a UserName consist of "A-Za-Z-_.", can't repeat ".-_" and can't use them as suffix or prefix.</summary>
    [GeneratedRegex(
        pattern: "^[a-zA-Z0-9]([-_.](?![-_.])|[a-zA-Z0-9]){1,28}[a-zA-Z0-9]$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Username { get; }

    /// <summary>Represent a slug.</summary>
    /// <example>hello, Hello, hello-world, hello-456-world, 456-hello, 456</example>
    [GeneratedRegex(
        pattern: "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Slug { get; }

    /// <summary>Represent xml/html tag.</summary>
    [GeneratedRegex(
        pattern: @"^<(?<1>[a-z1-6]+)([^<]+)*(?:>(.*)<\/\1>| *\/>)$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex XmlTag { get; }

    [GeneratedRegex(
        pattern:  "<!--.*?-->",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex HtmlComments { get; }

    [GeneratedRegex(
        pattern:  "(?s)<script.*?(/>|</script>)",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex HtmlScripts { get; }

    [GeneratedRegex(
        pattern:  "(?s)<style.*?(/>|</style>)",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex HtmlStyles { get; }


    /// <summary>Represent a file with extension absolute/relative URL.</summary>
    /// <example>/api.example.com/file.jpg</example>
    /// <example>/builder/file.png</example>
    /// <example>/directory/file.ext</example>
    /// <example>http://download.org/file.mov</example>
    /// <example>https://google.com/builder.example.com/2042/Bundle_develop_US.Localization_NP12416_2042.zip</example>
    [GeneratedRegex(
        pattern: @"((\/|\\|\/\/|https?:\\\\|https?:\/\/)[a-z0-9 _@\-^!#$%&+={}.\/\\\[\]]+)+\.[a-z]+$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex FilePathUrl { get; }

    /// <summary>Represent integer number.</summary>
    [GeneratedRegex(
        pattern: "^((-?[1-9]+)|[0-9]+)$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex IntegerNumber { get; }

    /// <summary>Represent decimal number.</summary>
    [GeneratedRegex(
        pattern: @"^((-?[1-9]+)|[0-9]+)(\.?|\,?)([0-9]*)$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex DecimalNumber { get; }

    /// <summary>Represent hex number.</summary>
    [GeneratedRegex(
        pattern: "^#?([a-f0-9]{6}|[a-f0-9]{3})$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex HexNumber { get; }

    /// <summary>Represent date.</summary>
    [GeneratedRegex(
        pattern: @"^((((0?[1-9]|[12]\d|3[01])[\.\-\/](0?[13578]|1[02])[\.\-\/]((1[6-9]|[2-9]\d)?\d{2}))|((0?[1-9]|[12]\d|30)[\.\-\/](0?[13456789]|1[012])[\.\-\/]((1[6-9]|[2-9]\d)?\d{2}))|((0?[1-9]|1\d|2[0-8])[\.\-\/]0?2[\.\-\/]((1[6-9]|[2-9]\d)?\d{2}))|(29[\.\-\/]0?2[\.\-\/]((1[6-9]|[2-9]\d)?(0[48]|[2468][048]|[13579][26])|((16|[2468][048]|[3579][26])00)|00)))|(((0[1-9]|[12]\d|3[01])(0[13578]|1[02])((1[6-9]|[2-9]\d)?\d{2}))|((0[1-9]|[12]\d|30)(0[13456789]|1[012])((1[6-9]|[2-9]\d)?\d{2}))|((0[1-9]|1\d|2[0-8])02((1[6-9]|[2-9]\d)?\d{2}))|(2902((1[6-9]|[2-9]\d)?(0[48]|[2468][048]|[13579][26])|((16|[2468][048]|[3579][26])00)|00)))) ?((20|21|22|23|[01]\d|\d)(([:.][0-5]\d){1,2}))?$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex Date { get; } //46494941649

    /// <summary>Represent IpAddressRange.</summary>
    [GeneratedRegex(
        pattern: @"^(?:\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(?:-\d{1,3})?|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\/\d{1,2})$",
        options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: MatchTimeoutMilliseconds
    )]
    public static partial Regex IpAddressRange { get; }
}
