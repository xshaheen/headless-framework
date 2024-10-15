// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Settings.Models;

public static class SettingsConstants
{
    public const string CommonUpdateLockKey = "Common_SettingUpdateLock";
    public const string SettingUpdatedStampCacheKey = "SettingUpdatedStamp";

    public static string GetApplicationLockKey(string applicationName)
    {
        return $"{applicationName}_SettingUpdateLock";
    }
}
