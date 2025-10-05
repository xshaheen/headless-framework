// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Permissions.Models;

public enum PermissionGrantStatus
{
    Undefined = 0,
    Granted,
    Prohibited,
}

public static class PermissionGrantStatusExtensions
{
    extension(PermissionGrantStatus)
    {
        internal static PermissionGrantStatus From(bool? isGranted)
        {
            return isGranted switch
            {
                true => PermissionGrantStatus.Granted,
                false => PermissionGrantStatus.Prohibited,
                null => PermissionGrantStatus.Undefined,
            };
        }

        internal static PermissionGrantStatus From(bool isGranted)
        {
            return isGranted switch
            {
                true => PermissionGrantStatus.Granted,
                false => PermissionGrantStatus.Prohibited,
            };
        }
    }
}
