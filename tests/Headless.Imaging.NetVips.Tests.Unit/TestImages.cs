// Copyright (c) Mahmoud Shaheen. All rights reserved.

using NetVips;

namespace Tests;

/// <summary>Builds encoded test images with libvips itself, so each test states the exact input it depends on.</summary>
internal static class TestImages
{
    /// <summary>A hand-written 2 × 2, 24-bit BMP: a format the bundled libvips cannot load.</summary>
    public static readonly byte[] Bmp =
    [
        0x42,
        0x4D,
        70,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        54,
        0,
        0,
        0,
        40,
        0,
        0,
        0,
        2,
        0,
        0,
        0,
        2,
        0,
        0,
        0,
        1,
        0,
        24,
        0,
        0,
        0,
        0,
        0,
        16,
        0,
        0,
        0,
        0x13,
        0x0B,
        0,
        0,
        0x13,
        0x0B,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        255,
        255,
        255,
        255,
        0,
        0,
        255,
        0,
        0,
        0,
        255,
        0,
        0,
        0,
    ];

    /// <summary>An SVG document: libvips can rasterize it, which is exactly why the allowlist refuses it.</summary>
    public static readonly byte[] Svg = System.Text.Encoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10'/></svg>"
    );

    /// <summary>Creates a noisy RGB image; noise keeps every encoder honest about size, unlike a flat colour.</summary>
    public static Image Noise(int width, int height, bool alpha = false)
    {
        using var red = Image.Gaussnoise(width, height, sigma: 40, mean: 120);
        using var green = Image.Gaussnoise(width, height, sigma: 40, mean: 140);
        using var blue = Image.Gaussnoise(width, height, sigma: 40, mean: 160);
        using var rgb = red.Bandjoin(green, blue);
        using var withAlpha = alpha ? rgb.Bandjoin(255) : rgb.Copy();
        using var bytes = withAlpha.Cast(Enums.BandFormat.Uchar);

        return bytes.Copy(interpretation: Enums.Interpretation.Srgb);
    }

    /// <summary>Encodes a noisy <paramref name="width" /> × <paramref name="height" /> image in <paramref name="suffix" /> (".jpg", ".png", …).</summary>
    public static byte[] Encode(string suffix, int width, int height, bool alpha = false, string options = "")
    {
        using var image = Noise(width, height, alpha);

        return image.WriteToBuffer(suffix + (options.Length == 0 ? "" : $"[{options}]"));
    }

    /// <summary>Creates a three-frame GIF; each frame is a solid colour so frame order is checkable.</summary>
    public static byte[] AnimatedGif(int width, int height)
    {
        using var animated = _AnimatedStrip(width, height);

        return animated.GifsaveBuffer();
    }

    /// <summary>Creates a three-frame WebP at quality 100, so the default quality 75 can shrink it.</summary>
    public static byte[] AnimatedWebp(int width, int height)
    {
        using var animated = _AnimatedStrip(width, height);

        return animated.WebpsaveBuffer(q: 100);
    }

    private static Image _AnimatedStrip(int width, int height)
    {
        using var red = Image.Black(width, height, bands: 3) + new double[] { 255, 0, 0 };
        using var green = Image.Black(width, height, bands: 3) + new double[] { 0, 255, 0 };
        using var blue = Image.Black(width, height, bands: 3) + new double[] { 0, 0, 255 };
        using var strip = Image.Arrayjoin([red, green, blue], across: 1);
        using var bytes = strip.Cast(Enums.BandFormat.Uchar);
        using var srgb = bytes.Copy(interpretation: Enums.Interpretation.Srgb);

        return srgb.Mutate(image =>
        {
            image.Set(GValue.GIntType, "page-height", height);
            image.Set(GValue.ArrayIntType, "delay", new[] { 100, 200, 300 });
        });
    }

    /// <summary>Encodes a JPEG whose pixels are stored on their side, with EXIF orientation 6 to turn them upright.</summary>
    public static byte[] SidewaysJpeg(int storedWidth, int storedHeight)
    {
        using var image = Noise(storedWidth, storedHeight);
        using var oriented = image.Mutate(m => m.Set(GValue.GIntType, "orientation", 6));

        return oriented.JpegsaveBuffer(keep: Enums.ForeignKeep.All);
    }

    /// <summary>Decodes an encoded result; <paramref name="allFrames" /> stacks every frame of a GIF or WebP.</summary>
    public static Image Decode(Stream stream, bool allFrames = false)
    {
        var bytes = stream.GetAllBytes();
        stream.Position = 0;

        return Image.NewFromBuffer(bytes, allFrames ? "n=-1" : "");
    }

    public static string? LoaderOf(Stream stream)
    {
        var loader = Image.FindLoadBuffer(stream.GetAllBytes());
        stream.Position = 0;

        return loader;
    }

    public static string AssetPath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", name);
    }
}
