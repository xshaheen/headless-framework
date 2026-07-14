// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Maximum lengths for the corresponding <see cref="File"/> properties, intended for storage/validation constraints.</summary>
public static class FileConstants
{
    /// <summary>Maximum length of <see cref="File.Id"/>.</summary>
    public const int IdNameMaxLength = 100;

    /// <summary>Maximum length of <see cref="File.DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 250;

    /// <summary>Maximum length of <see cref="File.SavedName"/>.</summary>
    public const int SavedNameMaxLength = 250;

    /// <summary>Maximum length of <see cref="File.Url"/>.</summary>
    public const int UrlMaxLength = 2000;

    /// <summary>Maximum length of <see cref="File.ContentType"/>.</summary>
    public const int ContentTypeMaxLength = 150;

    /// <summary>Maximum length of <see cref="File.Md5"/> (a 32-character hexadecimal MD5 hash).</summary>
    public const int Md5MaxLength = 32;
}
