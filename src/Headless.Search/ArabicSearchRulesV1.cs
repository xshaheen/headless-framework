// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Search;

/// <summary>
/// The V1 rows. Frozen once released: any change here changes every V1 key already stored, so new rules go in a new
/// version. Presentation forms are listed by the base letter of their Unicode 16.0 compatibility decomposition and take
/// that letter's result, so they always fold like the letter they draw.
/// </summary>
internal static class ArabicSearchRulesV1
{
    public static ArabicSearchRuleSet Create()
    {
        var rules = new Builder();

        // Output alphabet: ASCII digits and lowercase letters, and the 29 base Arabic letters (hamza included).
        rules.Output(0x0030, 0x0039);
        rules.Output(0x0061, 0x007A);
        rules.Output(0x0621);
        rules.Output(0x0627, 0x0628);
        rules.Output(0x062A, 0x063A);
        rules.Output(0x0641, 0x0648);
        rules.Output(0x064A);

        // ASCII uppercase; ASCII only, so no culture or Unicode case table is involved.
        for (var c = 0x0041; c <= 0x005A; c++)
        {
            rules.Map(c, (char)(c + 0x20));
        }

        // Arabic-Indic and Extended Arabic-Indic (Persian, Urdu) digits.
        for (var i = 0; i <= 9; i++)
        {
            rules.Map(0x0660 + i, (char)('0' + i));
            rules.Map(0x06F0 + i, (char)('0' + i));
        }

        // Alef: hamza seats, madda, wasla, wavy hamza, and high hamza alef.
        rules.Map('ا', 0x0622, 0x0623, 0x0625, 0x0671, 0x0672, 0x0673, 0x0675);

        // Waw: hamza seat, high hamza, and the Persian, Turkic, and Kurdish waw variants.
        rules.Map('و', 0x0624, 0x0676, 0x0677, 0x06C4, 0x06C5, 0x06C6, 0x06C7, 0x06C8, 0x06C9, 0x06CA, 0x06CB, 0x06CF);

        // Yeh: alef maksura (a search-key fold only), the hamza seat, Persian yeh, yeh barree, and their variants.
        rules.Map(
            'ي',
            0x0649,
            0x0626,
            0x0620,
            0x063D,
            0x063E,
            0x063F,
            0x0678,
            0x06CC,
            0x06CD,
            0x06CE,
            0x06D0,
            0x06D1,
            0x06D2,
            0x06D3
        );

        // Heh: teh marbuta (a search-key fold only), heh doachashmee, heh goal, ae, and their variants.
        rules.Map('ه', 0x0629, 0x06BE, 0x06C0, 0x06C1, 0x06C2, 0x06C3, 0x06D5, 0x06FF);

        // Kaf: keheh, swash kaf, ng, and gaf with its variants.
        rules.Map(
            'ك',
            0x063B,
            0x063C,
            0x06A9,
            0x06AA,
            0x06AB,
            0x06AC,
            0x06AD,
            0x06AE,
            0x06AF,
            0x06B0,
            0x06B1,
            0x06B2,
            0x06B3,
            0x06B4
        );

        rules.Map('ل', 0x06B5, 0x06B6, 0x06B7, 0x06B8);

        // Dal variants. Thal (ذ) stays distinct: folding it into dal is a dialect habit that merges different names.
        rules.Map('د', 0x0688, 0x0689, 0x068A, 0x068B, 0x068C, 0x068D, 0x068E, 0x068F, 0x0690, 0x06EE);

        rules.Map('ر', 0x0691, 0x0695, 0x06EF);
        rules.Map('ز', 0x0698);
        rules.Map('ب', 0x066E, 0x067B, 0x067E, 0x0680);
        rules.Map('ت', 0x0679, 0x067A, 0x067F);
        rules.Map('ج', 0x0683, 0x0684, 0x0686, 0x0687);
        rules.Map('ف', 0x06A4, 0x06A5, 0x06A6);
        rules.Map('ق', 0x066F, 0x06A8);
        rules.Map('ن', 0x06BA, 0x06BB);
        rules.Map('ض', 0x06FB);

        // Latin-1 Supplement and Latin Extended-A letters, folded to their ASCII base.
        rules.MapRanges('a', (0x00C0, 0x00C5), (0x00E0, 0x00E5), (0x0100, 0x0105));
        rules.MapRanges('c', (0x00C7, 0x00C7), (0x00E7, 0x00E7), (0x0106, 0x010D));
        rules.MapRanges('d', (0x00D0, 0x00D0), (0x00F0, 0x00F0), (0x010E, 0x0111));
        rules.MapRanges('e', (0x00C8, 0x00CB), (0x00E8, 0x00EB), (0x0112, 0x011B));
        rules.MapRanges('g', (0x011C, 0x0123));
        rules.MapRanges('h', (0x0124, 0x0127));
        rules.MapRanges('i', (0x00CC, 0x00CF), (0x00EC, 0x00EF), (0x0128, 0x0131));
        rules.MapRanges('j', (0x0134, 0x0135));
        rules.MapRanges('k', (0x0136, 0x0138));
        rules.MapRanges('l', (0x0139, 0x0142));
        rules.MapRanges('n', (0x00D1, 0x00D1), (0x00F1, 0x00F1), (0x0143, 0x014B));
        rules.MapRanges('o', (0x00D2, 0x00D6), (0x00D8, 0x00D8), (0x00F2, 0x00F6), (0x00F8, 0x00F8), (0x014C, 0x0151));
        rules.MapRanges('r', (0x0154, 0x0159));
        rules.MapRanges('s', (0x015A, 0x0161), (0x017F, 0x017F));
        rules.MapRanges('t', (0x0162, 0x0167));
        rules.MapRanges('u', (0x00D9, 0x00DC), (0x00F9, 0x00FC), (0x0168, 0x0173));
        rules.MapRanges('w', (0x0174, 0x0175));
        rules.MapRanges('y', (0x00DD, 0x00DD), (0x00FD, 0x00FD), (0x00FF, 0x00FF), (0x0176, 0x0178));
        rules.MapRanges('z', (0x0179, 0x017E));

        rules.Expand("لا", 0xFEF5, 0xFEF6, 0xFEF7, 0xFEF8, 0xFEF9, 0xFEFA, 0xFEFB, 0xFEFC);
        rules.Expand("الله", 0xFDF2);
        rules.Expand("ae", 0x00C6, 0x00E6);
        rules.Expand("oe", 0x0152, 0x0153);
        rules.Expand("ij", 0x0132, 0x0133);
        rules.Expand("th", 0x00DE, 0x00FE);
        rules.Expand("ss", 0x00DF);

        // Deletions: marks that sit on a letter, and invisible formatting characters.
        rules.Delete(0x0640, 0x0640); // tatweel
        rules.Delete(0x064B, 0x0652); // tanwin, harakat, shadda, sukun
        rules.Delete(0x0653, 0x0655); // combining madda, hamza above, hamza below
        rules.Delete(0x0656, 0x065F); // other Arabic combining marks
        rules.Delete(0x0670, 0x0670); // superscript alef
        rules.Delete(0x0610, 0x061A); // honorific and Quranic signs above letters
        rules.Delete(0x06D6, 0x06DC); // Quranic annotation marks
        rules.Delete(0x06DF, 0x06E4);
        rules.Delete(0x06E5, 0x06E6); // small waw, small yeh
        rules.Delete(0x06E7, 0x06E8);
        rules.Delete(0x06EA, 0x06ED);
        rules.Delete(0x0674, 0x0674); // high hamza
        rules.Delete(0x00AD, 0x00AD); // soft hyphen
        rules.Delete(0x061C, 0x061C); // Arabic letter mark
        rules.Delete(0x200B, 0x200F); // zero-width space, joiners, direction marks
        rules.Delete(0x202A, 0x202E); // bidi embeddings and overrides
        rules.Delete(0x2060, 0x2060); // word joiner
        rules.Delete(0x2066, 0x2069); // bidi isolates
        rules.Delete(0xFEFF, 0xFEFF); // byte order mark
        rules.Delete(0x0300, 0x036F); // combining diacritical marks, so decomposed Latin folds like composed
        rules.Delete(0xFE70, 0xFE74); // presentation-form harakat and tail fragment (U+FE75 is unassigned)
        rules.Delete(0xFE76, 0xFE7F);

        // Presentation Forms-B, by base letter.
        rules.Forms(0x0621, 0xFE80, 0xFE80);
        rules.Forms(0x0622, 0xFE81, 0xFE82);
        rules.Forms(0x0623, 0xFE83, 0xFE84);
        rules.Forms(0x0624, 0xFE85, 0xFE86);
        rules.Forms(0x0625, 0xFE87, 0xFE88);
        rules.Forms(0x0626, 0xFE89, 0xFE8C);
        rules.Forms(0x0627, 0xFE8D, 0xFE8E);
        rules.Forms(0x0628, 0xFE8F, 0xFE92);
        rules.Forms(0x0629, 0xFE93, 0xFE94);
        rules.Forms(0x062A, 0xFE95, 0xFE98);
        rules.Forms(0x062B, 0xFE99, 0xFE9C);
        rules.Forms(0x062C, 0xFE9D, 0xFEA0);
        rules.Forms(0x062D, 0xFEA1, 0xFEA4);
        rules.Forms(0x062E, 0xFEA5, 0xFEA8);
        rules.Forms(0x062F, 0xFEA9, 0xFEAA);
        rules.Forms(0x0630, 0xFEAB, 0xFEAC);
        rules.Forms(0x0631, 0xFEAD, 0xFEAE);
        rules.Forms(0x0632, 0xFEAF, 0xFEB0);
        rules.Forms(0x0633, 0xFEB1, 0xFEB4);
        rules.Forms(0x0634, 0xFEB5, 0xFEB8);
        rules.Forms(0x0635, 0xFEB9, 0xFEBC);
        rules.Forms(0x0636, 0xFEBD, 0xFEC0);
        rules.Forms(0x0637, 0xFEC1, 0xFEC4);
        rules.Forms(0x0638, 0xFEC5, 0xFEC8);
        rules.Forms(0x0639, 0xFEC9, 0xFECC);
        rules.Forms(0x063A, 0xFECD, 0xFED0);
        rules.Forms(0x0641, 0xFED1, 0xFED4);
        rules.Forms(0x0642, 0xFED5, 0xFED8);
        rules.Forms(0x0643, 0xFED9, 0xFEDC);
        rules.Forms(0x0644, 0xFEDD, 0xFEE0);
        rules.Forms(0x0645, 0xFEE1, 0xFEE4);
        rules.Forms(0x0646, 0xFEE5, 0xFEE8);
        rules.Forms(0x0647, 0xFEE9, 0xFEEC);
        rules.Forms(0x0648, 0xFEED, 0xFEEE);
        rules.Forms(0x0649, 0xFEEF, 0xFEF0);
        rules.Forms(0x064A, 0xFEF1, 0xFEF4);

        // Presentation Forms-A, single letters only; the multi-letter ligatures are separators in V1.
        rules.Forms(0x0671, 0xFB50, 0xFB51);
        rules.Forms(0x067B, 0xFB52, 0xFB55);
        rules.Forms(0x067E, 0xFB56, 0xFB59);
        rules.Forms(0x0680, 0xFB5A, 0xFB5D);
        rules.Forms(0x067A, 0xFB5E, 0xFB61);
        rules.Forms(0x067F, 0xFB62, 0xFB65);
        rules.Forms(0x0679, 0xFB66, 0xFB69);
        rules.Forms(0x06A4, 0xFB6A, 0xFB6D);
        rules.Forms(0x06A6, 0xFB6E, 0xFB71);
        rules.Forms(0x0684, 0xFB72, 0xFB75);
        rules.Forms(0x0683, 0xFB76, 0xFB79);
        rules.Forms(0x0686, 0xFB7A, 0xFB7D);
        rules.Forms(0x0687, 0xFB7E, 0xFB81);
        rules.Forms(0x068D, 0xFB82, 0xFB83);
        rules.Forms(0x068C, 0xFB84, 0xFB85);
        rules.Forms(0x068E, 0xFB86, 0xFB87);
        rules.Forms(0x0688, 0xFB88, 0xFB89);
        rules.Forms(0x0698, 0xFB8A, 0xFB8B);
        rules.Forms(0x0691, 0xFB8C, 0xFB8D);
        rules.Forms(0x06A9, 0xFB8E, 0xFB91);
        rules.Forms(0x06AF, 0xFB92, 0xFB95);
        rules.Forms(0x06B3, 0xFB96, 0xFB99);
        rules.Forms(0x06B1, 0xFB9A, 0xFB9D);
        rules.Forms(0x06BA, 0xFB9E, 0xFB9F);
        rules.Forms(0x06BB, 0xFBA0, 0xFBA3);
        rules.Forms(0x06C0, 0xFBA4, 0xFBA5);
        rules.Forms(0x06C1, 0xFBA6, 0xFBA9);
        rules.Forms(0x06BE, 0xFBAA, 0xFBAD);
        rules.Forms(0x06D2, 0xFBAE, 0xFBAF);
        rules.Forms(0x06D3, 0xFBB0, 0xFBB1);
        rules.Forms(0x06AD, 0xFBD3, 0xFBD6);
        rules.Forms(0x06C7, 0xFBD7, 0xFBD8);
        rules.Forms(0x06C6, 0xFBD9, 0xFBDA);
        rules.Forms(0x06C8, 0xFBDB, 0xFBDC);
        rules.Forms(0x0677, 0xFBDD, 0xFBDD);
        rules.Forms(0x06CB, 0xFBDE, 0xFBDF);
        rules.Forms(0x06C5, 0xFBE0, 0xFBE1);
        rules.Forms(0x06C9, 0xFBE2, 0xFBE3);
        rules.Forms(0x06D0, 0xFBE4, 0xFBE7);
        rules.Forms(0x0649, 0xFBE8, 0xFBE9);
        rules.Forms(0x06CC, 0xFBFC, 0xFBFF);

        return rules.Build();
    }

    private sealed class Builder
    {
        private readonly SortedSet<char> _output = [];
        private readonly Dictionary<char, char> _mappings = [];
        private readonly Dictionary<char, string> _expansions = [];
        private readonly SortedSet<char> _deletions = [];

        public void Output(int codePoint) => Output(codePoint, codePoint);

        public void Output(int first, int last)
        {
            for (var c = first; c <= last; c++)
            {
                _Claim(c);
                _output.Add((char)c);
            }
        }

        public void Map(int source, char target)
        {
            _Claim(source);
            _mappings.Add((char)source, target);
        }

        public void Map(char target, params ReadOnlySpan<int> sources)
        {
            foreach (var source in sources)
            {
                Map(source, target);
            }
        }

        public void MapRanges(char target, params ReadOnlySpan<(int First, int Last)> ranges)
        {
            foreach (var (first, last) in ranges)
            {
                for (var c = first; c <= last; c++)
                {
                    Map(c, target);
                }
            }
        }

        public void Expand(string result, params ReadOnlySpan<int> sources)
        {
            foreach (var source in sources)
            {
                _Claim(source);
                _expansions.Add((char)source, result);
            }
        }

        public void Delete(int first, int last)
        {
            for (var c = first; c <= last; c++)
            {
                _Claim(c);
                _deletions.Add((char)c);
            }
        }

        // A presentation form takes its base letter's result, which must already be declared.
        public void Forms(int baseLetter, int first, int last)
        {
            var letter = (char)baseLetter;
            var target =
                _output.Contains(letter) ? letter
                : _mappings.TryGetValue(letter, out var mapped) ? mapped
                : throw new InvalidOperationException($"Base letter U+{baseLetter:X4} has no V1 rule.");

            for (var c = first; c <= last; c++)
            {
                Map(c, target);
            }
        }

        public ArabicSearchRuleSet Build()
        {
            foreach (var (source, target) in _mappings)
            {
                _EnsureOutput(source, target);
            }

            foreach (var (source, result) in _expansions)
            {
                foreach (var c in result)
                {
                    _EnsureOutput(source, c);
                }
            }

            return new ArabicSearchRuleSet(ArabicSearchVersion.V1, _output, _mappings, _expansions, _deletions);
        }

        private void _EnsureOutput(char source, char target)
        {
            if (!_output.Contains(target))
            {
                throw new InvalidOperationException(
                    $"U+{(int)source:X4} maps to U+{(int)target:X4}, which is outside the V1 output alphabet."
                );
            }
        }

        private void _Claim(int codePoint)
        {
            if (codePoint is <= 0x0020 or > char.MaxValue or (>= 0xD800 and <= 0xDFFF))
            {
                throw new InvalidOperationException($"U+{codePoint:X4} cannot carry a V1 rule.");
            }

            var c = (char)codePoint;

            if (_output.Contains(c) || _mappings.ContainsKey(c) || _expansions.ContainsKey(c) || _deletions.Contains(c))
            {
                throw new InvalidOperationException($"U+{codePoint:X4} has more than one V1 rule.");
            }
        }
    }
}
