// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>The <c>apns-push-type</c> header values, shared by header computation, payload limits, and auth-mode checks.</summary>
internal static class ApnsPushTypes
{
    public const string Alert = "alert";
    public const string Background = "background";
    public const string Voip = "voip";
    public const string LiveActivity = "liveactivity";
    public const string Location = "location";
    public const string PushToTalk = "pushtotalk";
    public const string Widgets = "widgets";
    public const string Controls = "controls";
    public const string Complication = "complication";
    public const string FileProvider = "fileprovider";
}
