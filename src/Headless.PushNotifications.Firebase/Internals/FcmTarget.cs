// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>What an FCM send addresses.</summary>
internal enum FcmTargetKind
{
    Token = 0,
    Topic = 1,
    Condition = 2,
}

/// <summary>One FCM send target: a device FID or token, a topic name, or a condition.</summary>
internal readonly record struct FcmTarget(FcmTargetKind Kind, string Value)
{
    public static FcmTarget Token(string fid) => new(FcmTargetKind.Token, fid);

    public static FcmTarget Topic(string topic) => new(FcmTargetKind.Topic, topic);

    public static FcmTarget Condition(string condition) => new(FcmTargetKind.Condition, condition);

    /// <summary>The <see cref="FcmTags.TargetKind"/> value.</summary>
    public static string ToTagValue(FcmTargetKind kind)
    {
        return kind switch
        {
            FcmTargetKind.Topic => "topic",
            FcmTargetKind.Condition => "condition",
            _ => "token",
        };
    }
}
