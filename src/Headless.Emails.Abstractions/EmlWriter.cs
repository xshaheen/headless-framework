// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>Writes a <see cref="CapturedEmail"/> as RFC 5322 MIME text without a MIME library.</summary>
internal static class EmlWriter
{
    private const string _Crlf = "\r\n";
    private const int _Base64LineLength = 76;

    // An RFC 2047 encoded-word may be at most 75 characters; 45 UTF-8 bytes encode to 60 base64 characters, which
    // leaves room for the "=?utf-8?B?" prefix and "?=" suffix.
    private const int _EncodedWordMaxBytes = 45;

    // A header line may not exceed 998 characters; encoding longer text as encoded-words folds it onto several lines.
    private const int _MaxPlainHeaderLength = 900;

    public static string Write(CapturedEmail email)
    {
        var sb = new StringBuilder();

        _Header(
            sb,
            "Date",
            email.CapturedAt.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                + " "
                + _FormatOffset(email.CapturedAt.Offset)
        );
        _AddressHeader(sb, "From", [email.From]);
        _AddressHeader(sb, "To", email.To);

        if (email.Cc.Count > 0)
        {
            _AddressHeader(sb, "Cc", email.Cc);
        }

        if (email.Bcc.Count > 0)
        {
            _AddressHeader(sb, "Bcc", email.Bcc);
        }

        _Header(sb, "Subject", _EncodeText(email.Subject));
        _Header(sb, "Message-ID", $"<{email.Id:N}@dev-inbox.headless>");
        _Header(sb, "MIME-Version", "1.0");

        if (email.SenderName.Length > 0)
        {
            _Header(sb, "X-Headless-Sender", _EncodeText(email.SenderName));
        }

        if (email.TraceId is not null)
        {
            _Header(sb, "X-Headless-Trace-Id", email.TraceId);
        }

        if (email.CorrelationId is not null)
        {
            _Header(sb, "X-Headless-Correlation-Id", _EncodeText(email.CorrelationId));
        }

        if (email.FailureError is not null)
        {
            _Header(sb, "X-Headless-Failure", _EncodeText(email.FailureError));
        }

        foreach (var omitted in email.Attachments.Where(static a => a.Content is null))
        {
            _Header(
                sb,
                "X-Headless-Attachment-Omitted",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_EncodeText(omitted.FileName)}; size={omitted.SizeBytes}; type={_SafeContentType(omitted.ContentType, "unknown")}"
                )
            );
        }

        var kept = email.Attachments.Where(static a => a.Content is not null).ToList();

        if (kept.Count == 0)
        {
            _WriteBody(sb, email);

            return sb.ToString();
        }

        var boundary = $"=_mixed_{email.Id:N}";
        _Header(sb, "Content-Type", $"multipart/mixed; boundary=\"{boundary}\"");
        sb.Append(_Crlf);

        sb.Append("--").Append(boundary).Append(_Crlf);
        _WriteBody(sb, email);

        foreach (var attachment in kept)
        {
            sb.Append("--").Append(boundary).Append(_Crlf);
            _Header(sb, "Content-Type", _SafeContentType(attachment.ContentType, "application/octet-stream"));
            _Header(sb, "Content-Disposition", "attachment; " + _FileNameParameter(attachment.FileName));
            _Header(sb, "Content-Transfer-Encoding", "base64");
            sb.Append(_Crlf);
            _AppendBase64(sb, attachment.Content!.Value.Span);
        }

        sb.Append("--").Append(boundary).Append("--").Append(_Crlf);

        return sb.ToString();
    }

    // Writes the body headers and content: one part for a single body, multipart/alternative for both.
    private static void _WriteBody(StringBuilder sb, CapturedEmail email)
    {
        var hasText = !string.IsNullOrEmpty(email.TextBody);
        var hasHtml = !string.IsNullOrEmpty(email.HtmlBody);

        if (hasText && hasHtml)
        {
            var boundary = $"=_alt_{email.Id:N}";
            _Header(sb, "Content-Type", $"multipart/alternative; boundary=\"{boundary}\"");
            sb.Append(_Crlf);
            sb.Append("--").Append(boundary).Append(_Crlf);
            _WriteTextPart(sb, "text/plain", email.TextBody!);
            sb.Append("--").Append(boundary).Append(_Crlf);
            _WriteTextPart(sb, "text/html", email.HtmlBody!);
            sb.Append("--").Append(boundary).Append("--").Append(_Crlf);

            return;
        }

        if (hasHtml)
        {
            _WriteTextPart(sb, "text/html", email.HtmlBody!);

            return;
        }

        // A failed capture can have no body at all; an empty text part keeps the file a valid message.
        _WriteTextPart(sb, "text/plain", email.TextBody ?? string.Empty);
    }

    private static void _WriteTextPart(StringBuilder sb, string mediaType, string content)
    {
        _Header(sb, "Content-Type", mediaType + "; charset=utf-8");
        _Header(sb, "Content-Transfer-Encoding", "base64");
        sb.Append(_Crlf);
        _AppendBase64(sb, Encoding.UTF8.GetBytes(content));
    }

    private static void _AppendBase64(StringBuilder sb, ReadOnlySpan<byte> bytes)
    {
        var encoded = Convert.ToBase64String(bytes);

        for (var i = 0; i < encoded.Length; i += _Base64LineLength)
        {
            sb.Append(encoded.AsSpan(i, Math.Min(_Base64LineLength, encoded.Length - i))).Append(_Crlf);
        }
    }

    private static void _Header(StringBuilder sb, string name, string value)
    {
        sb.Append(name).Append(": ").Append(value).Append(_Crlf);
    }

    // Folds an address list one address per line, so a long recipient list stays under the 998-character line limit.
    private static void _AddressHeader(StringBuilder sb, string name, IReadOnlyList<EmailRequestAddress> addresses)
    {
        sb.Append(name).Append(": ");

        for (var i = 0; i < addresses.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',').Append(_Crlf).Append(' ');
            }

            sb.Append(_FormatAddress(addresses[i]));
        }

        sb.Append(_Crlf);
    }

    private static string _FormatAddress(EmailRequestAddress address)
    {
        if (string.IsNullOrEmpty(address.DisplayName))
        {
            return _StripLineBreaks(address.EmailAddress);
        }

        var displayName = _IsPlainHeaderText(address.DisplayName)
            ? "\""
                + address
                    .DisplayName.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal)
                + "\""
            : _EncodeWords(address.DisplayName);

        return $"{displayName} <{_StripLineBreaks(address.EmailAddress)}>";
    }

    // Leaves short printable ASCII as is and RFC 2047-encodes anything else, so a control character never ends the
    // header and a long value folds instead of passing the line limit.
    private static string _EncodeText(string value)
    {
        return _IsPlainHeaderText(value) ? value : _EncodeWords(value);
    }

    private static bool _IsPlainHeaderText(string value)
    {
        return value.Length <= _MaxPlainHeaderLength && Ascii.IsValid(value) && !value.Any(char.IsControl);
    }

    // A caller-supplied address carries no validation; removing line breaks keeps it from injecting a header.
    private static string _StripLineBreaks(string value)
    {
        return value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
    }

    // A media type is a token without spaces or controls; anything else would break the header, so it falls back.
    private static string _SafeContentType(string? contentType, string fallback)
    {
        return string.IsNullOrWhiteSpace(contentType) || !Ascii.IsValid(contentType) || contentType.Any(char.IsControl)
            ? fallback
            : contentType;
    }

    // Splits on character boundaries, never inside a surrogate pair, so each encoded-word decodes on its own.
    private static string _EncodeWords(string value)
    {
        var words = new List<string>();
        var start = 0;

        while (start < value.Length)
        {
            var end = start;
            var bytes = 0;

            while (end < value.Length)
            {
                var length = char.IsHighSurrogate(value[end]) && end + 1 < value.Length ? 2 : 1;
                var charBytes = Encoding.UTF8.GetByteCount(value.AsSpan(end, length));

                if (bytes + charBytes > _EncodedWordMaxBytes && end > start)
                {
                    break;
                }

                bytes += charBytes;
                end += length;
            }

            words.Add("=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value[start..end])) + "?=");
            start = end;
        }

        return string.Join(_Crlf + " ", words);
    }

    // RFC 2231 extended parameter for a non-ASCII or control-bearing file name; a quoted string otherwise.
    private static string _FileNameParameter(string fileName)
    {
        if (_IsPlainHeaderText(fileName))
        {
            return "filename=\""
                + fileName
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal)
                + "\"";
        }

        var sb = new StringBuilder("filename*=utf-8''");

        foreach (var b in Encoding.UTF8.GetBytes(fileName))
        {
            if (char.IsAsciiLetterOrDigit((char)b) || b is (byte)'.' or (byte)'-' or (byte)'_')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    private static string _FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var absolute = offset.Duration();

        return string.Create(CultureInfo.InvariantCulture, $"{sign}{absolute.Hours:00}{absolute.Minutes:00}");
    }
}
