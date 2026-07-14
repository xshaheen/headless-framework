// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Length and element-count limits for the fields of <see cref="PageMetadata"/>.</summary>
public static class PageMetadataConstants
{
    /// <summary>Limits for <see cref="PageMetadata.Slug"/>.</summary>
    public static class Slugs
    {
        /// <summary>The maximum allowed length, in characters, of a slug.</summary>
        public const int MaxLength = 100;
    }

    /// <summary>Limits for <see cref="PageMetadata.MetaTitle"/>.</summary>
    public static class MetaTitles
    {
        /// <summary>The maximum allowed length, in characters, of a meta title.</summary>
        public const int MaxLength = 250;
    }

    /// <summary>Limits for <see cref="PageMetadata.MetaDescription"/>.</summary>
    public static class MetaDescriptions
    {
        /// <summary>The maximum allowed length, in characters, of a meta description.</summary>
        public const int MaxLength = 1000;
    }

    /// <summary>Limits for <see cref="PageMetadata.MetaKeywords"/>.</summary>
    public static class MetaKeywords
    {
        /// <summary>The maximum allowed number of meta keywords.</summary>
        public const int MaxElements = 25;
    }

    /// <summary>Limits for <see cref="PageMetadata.Tags"/>.</summary>
    public static class Tags
    {
        /// <summary>The maximum allowed number of tags.</summary>
        public const int MaxElements = 25;
    }
}
