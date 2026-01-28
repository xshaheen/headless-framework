// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms.VictoryLink.Internals;

internal static class VictoryLinkResponseCodes
{
    private const string _Ok = "0";
    private const string _OkQueued = "-10";
    private const string _UserError = "-1";
    private const string _CreditError = "-5";
    private const string _LanguageError = "-11";
    private const string _SmsError = "-12";
    private const string _SenderError = "-13";
    private const string _SendingRateError = "-25";
    private const string _OtherError = "-100";

    public static string GetCodeMeaning(string code)
    {
        return code switch
        {
            _Ok => "Message Sent Successfully",
            _UserError => "User is not subscribed",
            _CreditError => "Out of credit.",
            _OkQueued => "Queued Message, no need to send it again.",
            _LanguageError => "Invalid language.",
            _SmsError => "SMS is empty.",
            _SenderError => "Invalid fake sender exceeded 12 chars or empty.",
            _SendingRateError => "Sending rate greater than receiving rate (only for send/receive accounts).",
            _OtherError => "Something wrong happened",
            _ => $"Unknown response code from VictoryLink API with code: {code}",
        };
    }

    public static bool IsSuccess(string code)
    {
        return code is _Ok or _OkQueued;
    }
}
