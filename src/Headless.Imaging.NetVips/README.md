# Headless.Imaging.NetVips

libvips-backed contributors for image resizing, compression, format conversion, and inspection, through the NetVips binding.

## Why use this package

Provides the image-processing implementation wired into the contributor pipeline. libvips streams pixels through a demand-driven pipeline and shrinks JPEG and WebP while decoding, so resizing large images takes a fraction of the memory a full decode needs. Resizes and converts between JPEG, PNG, WebP, GIF (animated included), TIFF, and AVIF; compresses into JPEG, PNG, WebP, and AVIF; and inspects an upload's format and size from its header without reading the whole file. It strips metadata, rejects oversized images, and refuses every loader outside that list.

This package references only the MIT-licensed `NetVips` wrapper. The application supplies libvips itself: add `NetVips.Native` (bundled binaries, LGPL-3.0-or-later) or install libvips 8.15 or later on the host.

## Install

```bash
dotnet add package Headless.Imaging.NetVips
dotnet add package NetVips.Native
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Imaging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/imaging.md#headlessimagingnetvips)
