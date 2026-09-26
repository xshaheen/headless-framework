// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the configuration variable the imaging guide's examples assume.

// ImageSharp encoder namespaces the examples use; a consumer's IDE adds these usings.
global using static ImagingAmbient;
global using SixLabors.ImageSharp.Formats.Jpeg;
global using SixLabors.ImageSharp.Formats.Png;
global using SixLabors.ImageSharp.Formats.Webp;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class ImagingAmbient
{
    public static IConfiguration config => null!;
}
