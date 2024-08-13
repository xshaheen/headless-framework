namespace Framework.BuildingBlocks.Primitives;

[PublicAPI]
public sealed class PageMetadata
{
    /// <summary>Lowercase, hyphenated identifier typically used in URLs.</summary>
    public string? Slug { get; init; }

    /// <summary>Page title used for search engine optimization purposes.</summary>
    public string? MetaTitle { get; init; }

    /// <summary>Page description used for search engine optimization purposes.</summary>
    public string? MetaDescription { get; init; }

    /// <summary>Page keywords used for search engine optimization purposes.</summary>
    public HashSet<string>? MetaKeywords { get; init; }

    /// <summary>List of arbitrary tags typically used as metadata to improve search results or associate a custom behavior.</summary>
    public HashSet<string>? Tags { get; init; }
}

public static class PageMetadataConstants
{
    public static class Slugs
    {
        public const int MaxLength = 100;
    }

    public static class MetaTitles
    {
        public const int MaxLength = 250;
    }

    public static class MetaDescriptions
    {
        public const int MaxLength = 1000;
    }

    public static class MetaKeywords
    {
        public const int MaxElements = 25;
    }

    public static class Tags
    {
        public const int MaxElements = 25;
    }
}
