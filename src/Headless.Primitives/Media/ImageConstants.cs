// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Maximum lengths for the corresponding <see cref="Image"/> properties. Inherited file lengths mirror
/// <see cref="FileConstants"/>; <see cref="CaptionMaxLength"/> is image-specific.
/// </summary>
[PublicAPI]
public static class ImageConstants
{
    /// <summary>Maximum length of <see cref="File.Id"/>. Mirrors <see cref="FileConstants.IdNameMaxLength"/>.</summary>
    public const int IdNameMaxLength = FileConstants.IdNameMaxLength;

    /// <summary>Maximum length of <see cref="File.DisplayName"/>. Mirrors <see cref="FileConstants.DisplayNameMaxLength"/>.</summary>
    public const int DisplayNameMaxLength = FileConstants.DisplayNameMaxLength;

    /// <summary>Maximum length of <see cref="File.SavedName"/>. Mirrors <see cref="FileConstants.SavedNameMaxLength"/>.</summary>
    public const int SavedNameMaxLength = FileConstants.SavedNameMaxLength;

    /// <summary>Maximum length of <see cref="File.Url"/>. Mirrors <see cref="FileConstants.UrlMaxLength"/>.</summary>
    public const int UrlMaxLength = FileConstants.UrlMaxLength;

    /// <summary>Maximum length of <see cref="File.ContentType"/>. Mirrors <see cref="FileConstants.ContentTypeMaxLength"/>.</summary>
    public const int ContentTypeMaxLength = FileConstants.ContentTypeMaxLength;

    /// <summary>Maximum length of <see cref="File.Md5"/>. Mirrors <see cref="FileConstants.Md5MaxLength"/>.</summary>
    public const int Md5MaxLength = FileConstants.Md5MaxLength;

    /// <summary>Maximum length of <see cref="Image.Caption"/>.</summary>
    public const int CaptionMaxLength = 1000;
}
