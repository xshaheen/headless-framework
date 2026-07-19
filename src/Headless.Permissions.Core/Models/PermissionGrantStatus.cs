// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions.Models;

/// <summary>
/// The three-valued permission decision produced by the grant evaluation pipeline.
/// <para>
/// <see cref="Undefined"/> (the default) means no provider returned a conclusive answer; the framework
/// typically treats this as not-granted but distinct from an explicit denial.
/// <see cref="Granted"/> means at least one provider explicitly granted the permission and none prohibited it.
/// <see cref="Prohibited"/> means at least one provider explicitly denied the permission (AWS IAM-style explicit deny).
/// </para>
/// </summary>
public enum PermissionGrantStatus
{
    /// <summary>No provider returned a conclusive grant or denial for this permission.</summary>
    Undefined = 0,

    /// <summary>The permission has been explicitly granted.</summary>
    Granted = 1,

    /// <summary>The permission has been explicitly denied; takes precedence over grants.</summary>
    Prohibited = 2,
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
