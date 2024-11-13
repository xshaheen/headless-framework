// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;

namespace Framework.Kernel.BuildingBlocks.Helpers.Text;

internal static class FormatStringTokenizer
{
    public static List<FormatStringToken> Tokenize(string format, bool includeBracketsForDynamicValues = false)
    {
        var tokens = new List<FormatStringToken>();

        var currentText = new StringBuilder();
        var inDynamicValue = false;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            switch (c)
            {
                case '{':
                    if (inDynamicValue)
                    {
                        FormattableString message =
                            $"Incorrect syntax at char {i}! format string can not contain nested dynamic value expression!";
                        throw new FormatException(message.ToString(CultureInfo.InvariantCulture));
                    }

                    inDynamicValue = true;

                    if (currentText.Length > 0)
                    {
                        tokens.Add(new FormatStringToken(currentText.ToString(), FormatStringTokenType.ConstantText));
                        currentText.Clear();
                    }

                    break;
                case '}':
                    if (!inDynamicValue)
                    {
                        FormattableString message =
                            $"Incorrect syntax at char {i}! These is no opening brackets for the closing bracket }}.";

                        throw new FormatException(message.ToString(CultureInfo.InvariantCulture));
                    }

                    inDynamicValue = false;

                    if (currentText.Length <= 0)
                    {
                        FormattableString message =
                            $"Incorrect syntax at char {i}! Brackets does not contain any chars.";

                        throw new FormatException(message.ToString(CultureInfo.InvariantCulture));
                    }

                    var dynamicValue = currentText.ToString();
                    if (includeBracketsForDynamicValues)
                    {
                        dynamicValue = "{" + dynamicValue + "}";
                    }

                    tokens.Add(new FormatStringToken(dynamicValue, FormatStringTokenType.DynamicValue));
                    currentText.Clear();

                    break;
                default:
                    currentText.Append(c);
                    break;
            }
        }

        if (inDynamicValue)
        {
            throw new FormatException("There is no closing } char for an opened { char.");
        }

        if (currentText.Length > 0)
        {
            tokens.Add(new FormatStringToken(currentText.ToString(), FormatStringTokenType.ConstantText));
        }

        return tokens;
    }
}

internal sealed record FormatStringToken(string Text, FormatStringTokenType Type);

internal enum FormatStringTokenType
{
    ConstantText,
    DynamicValue,
}
