// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Sms;

/// <summary>Describes how a carrier encodes an SMS text and how many parts it is sent as.</summary>
/// <param name="Encoding">The encoding the text needs.</param>
/// <param name="Length">
/// The length in encoding units: GSM 7-bit septets, where an extension character such as <c>€</c> or <c>[</c> counts
/// as two, or UTF-16 code units for UCS-2.
/// </param>
/// <param name="SegmentCount">The number of SMS parts the text is sent as; 0 for an empty text.</param>
/// <remarks>
/// <see cref="Measure"/> reproduces the standard concatenation rules. It does not model a carrier that refuses to
/// split an extension character or a surrogate pair across parts, which can add one part at a boundary.
/// </remarks>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SmsSegmentation(SmsEncoding Encoding, int Length, int SegmentCount)
{
    private const string _Gsm7Basic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?"
        + "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    private const string _Gsm7Extension = "\f^{}\\[~]|€";

    private static readonly SearchValues<char> _Gsm7BasicValues = SearchValues.Create(_Gsm7Basic);
    private static readonly SearchValues<char> _Gsm7ExtensionValues = SearchValues.Create(_Gsm7Extension);

    /// <summary>Measures <paramref name="text"/> by the SMS concatenation rules.</summary>
    /// <param name="text">The SMS text.</param>
    /// <returns>The encoding, length, and part count of the text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SmsSegmentation Measure(string text)
    {
        Argument.IsNotNull(text);

        var septets = 0;

        foreach (var c in text)
        {
            if (_Gsm7BasicValues.Contains(c))
            {
                septets++;
            }
            else if (_Gsm7ExtensionValues.Contains(c))
            {
                // An extension character is sent as an escape septet followed by the character.
                septets += 2;
            }
            else
            {
                return new(SmsEncoding.Ucs2, text.Length, _Segments(text.Length, single: 70, multi: 67));
            }
        }

        return new(SmsEncoding.Gsm7, septets, _Segments(septets, single: 160, multi: 153));
    }

    // A split message spends part of each part on the concatenation header, so multi-part capacity is lower.
    private static int _Segments(int length, int single, int multi)
    {
        if (length == 0)
        {
            return 0;
        }

        return length <= single ? 1 : (length + multi - 1) / multi;
    }
}
