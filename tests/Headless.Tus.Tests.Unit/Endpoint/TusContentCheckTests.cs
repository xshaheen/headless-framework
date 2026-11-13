// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using FileSignatures;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenMcdf;
using tusdotnet.Interfaces;
using tusdotnet.Stores;

namespace Tests.Endpoint;

public sealed class TusContentCheckTests : TestBase
{
    private static readonly MediaTypeHeaderValue _PlainText = new("text/plain");

    private static readonly byte[] _PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] _JpegSignature = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    /// <summary>A host whose default endpoint has a 1 MiB limit and the rules <paramref name="configure"/> sets.</summary>
    private static TusEndpointTestHostSetup _Endpoint(
        Action<TusUploadEndpointOptions> configure,
        Action<IApplicationBuilder>? middleware = null
    )
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) =>
                tus.UseStore(store)
                    .AddUploadEndpoint(options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        configure(options);
                    }),
            ConfigureMiddleware = middleware,
        };
    }

    private static TusEndpointTestHostSetup _Images(Action<IApplicationBuilder>? middleware = null)
    {
        return _Endpoint(
            static options =>
            {
                options.AllowedContentTypes.Add("image/png");
                options.AllowedContentTypes.Add("image/jpeg");
            },
            middleware
        );
    }

    private static string _Metadata(string? fileName, string? contentType)
    {
        var entries = new List<string>();

        if (fileName is not null)
        {
            entries.Add($"filename {Convert.ToBase64String(Encoding.UTF8.GetBytes(fileName))}");
        }

        if (contentType is not null)
        {
            entries.Add($"filetype {Convert.ToBase64String(Encoding.UTF8.GetBytes(contentType))}");
        }

        return string.Join(',', entries);
    }

    /// <summary>A file of <paramref name="length"/> bytes that starts with <paramref name="signature"/>.</summary>
    private static byte[] _FileStartingWith(ReadOnlySpan<byte> signature, int length)
    {
        var file = new byte[length];
        signature[..Math.Min(signature.Length, length)].CopyTo(file);

        return file;
    }

    private static byte[] _Png(int length = 1024)
    {
        return _FileStartingWith(_PngSignature, length);
    }

    private static byte[] _Pdf(int length = 1024)
    {
        return _FileStartingWith("%PDF-1.7\n"u8, length);
    }

    /// <summary>A Windows executable: the <c>MZ</c> header every one starts with.</summary>
    private static byte[] _Executable(int length = 1024)
    {
        return _FileStartingWith("MZ"u8, length);
    }

    private static byte[] _Csv()
    {
        var builder = new StringBuilder("id,name,email\n");

        for (var row = 1; row <= 40; row++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{row},user{row},user{row}@example.com\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] _RandomBytes(int length = 1024)
    {
        var bytes = new byte[length];
        new Random(Seed: 20261113).NextBytes(bytes);

        return bytes;
    }

    /// <summary>A Word document: a ZIP archive with the parts that make it one, stored uncompressed.</summary>
    private static byte[] _Docx()
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            _AddPart(
                archive,
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Override PartName="/word/document.xml"
                    ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """
            );
            _AddPart(
                archive,
                "word/document.xml",
                $"<w:document><w:body><w:p><w:t>{new string('x', 2000)}</w:t></w:p></w:body></w:document>"
            );
        }

        return buffer.ToArray();
    }

    private static void _AddPart(ZipArchive archive, string name, string content)
    {
        using var part = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
        part.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>A legacy Word document: a compound file with a <c>WordDocument</c> stream.</summary>
    private static byte[] _LegacyDoc()
    {
        using var buffer = new MemoryStream();

        using (var root = RootStorage.Create(buffer, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            using var document = root.CreateStream("WordDocument");
            document.Write(_RandomBytes(16 * 1024));
        }

        return buffer.ToArray();
    }

    private static string? _DetectedExtension(byte[] file)
    {
        using var stream = new MemoryStream(file);

        return new FileFormatInspector().DetermineFileFormat(stream)?.Extension;
    }

    private static async Task _ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType.Should().Be(_PlainText);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().NotBeNullOrWhiteSpace();
    }

    private static async Task _ShouldBeGoneAsync(TusEndpointTestHost host, string location)
    {
        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    private static async Task _ShouldBeStoredAsync(TusEndpointTestHost host, string location, byte[] content)
    {
        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(content.Length.ToString(CultureInfo.InvariantCulture));

        // The check reads the first bytes before the store does: the store must still receive every one of them.
        var stored = await File.ReadAllBytesAsync(
            Path.Combine(host.Directory, TusEndpointTestHost.FileIdOf(location)),
            AbortToken
        );
        stored.Should().Equal(content);
    }

    [Fact]
    public async Task should_accept_a_png_declared_as_png()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var png = _Png();
        var location = await host.CreateUploadAsync(png.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, png, offset: 0);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, png);
    }

    [Fact]
    public async Task should_answer_415_and_delete_the_upload_when_a_pdf_is_declared_as_png()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var pdf = _Pdf();
        var location = await host.CreateUploadAsync(pdf.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, pdf, offset: 0);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType);
        await _ShouldBeGoneAsync(host, location);
    }

    [Fact]
    public async Task should_accept_a_docx_on_its_zip_container_when_only_docx_is_allowed()
    {
        // given - a real Word document, of which the first write shows only that it is a ZIP archive
        var setup = _Endpoint(static options => options.AllowedFileExtensions.Add("docx"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var docx = _Docx();
        _DetectedExtension(docx).Should().Be("docx");
        _DetectedExtension(docx[..512]).Should().Be("zip");
        var location = await host.CreateUploadAsync(docx.Length, _Metadata("report.docx", contentType: null));

        // when
        using var response = await host.PatchAsync(location, docx, offset: 0);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, docx);
    }

    [Fact]
    public async Task should_answer_415_and_delete_the_upload_when_an_executable_is_declared_as_docx()
    {
        // given
        var setup = _Endpoint(static options => options.AllowedFileExtensions.Add("docx"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var executable = _Executable();
        var location = await host.CreateUploadAsync(executable.Length, _Metadata("report.docx", contentType: null));

        // when
        using var response = await host.PatchAsync(location, executable, offset: 0);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType);
        await _ShouldBeGoneAsync(host, location);
    }

    [Fact]
    public async Task should_accept_a_legacy_doc_on_its_compound_file_container()
    {
        // given - a compound file far longer than a first write's leading bytes
        var setup = _Endpoint(static options => options.AllowedFileExtensions.Add("doc"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var doc = _LegacyDoc();
        _DetectedExtension(doc).Should().Be("doc");
        doc.Length.Should().BeGreaterThan(16 * 1024);
        var location = await host.CreateUploadAsync(doc.Length, _Metadata("letter.doc", contentType: null));

        // when
        using var response = await host.PatchAsync(location, doc, offset: 0);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, doc);
    }

    [Fact]
    public async Task should_answer_415_when_an_executable_is_declared_as_legacy_doc()
    {
        // given
        var setup = _Endpoint(static options => options.AllowedFileExtensions.Add("doc"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var executable = _Executable();
        var location = await host.CreateUploadAsync(executable.Length, _Metadata("letter.doc", contentType: null));

        // when
        using var response = await host.PatchAsync(location, executable, offset: 0);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType);
        await _ShouldBeGoneAsync(host, location);
    }

    [Fact]
    public async Task should_accept_a_csv_whose_type_has_no_signature()
    {
        // given
        var setup = _Endpoint(static options => options.AllowedContentTypes.Add("text/csv"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var csv = _Csv();
        var location = await host.CreateUploadAsync(csv.Length, _Metadata("users.csv", "text/csv"));

        // when
        using var response = await host.PatchAsync(location, csv, offset: 0);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, csv);
    }

    [Fact]
    public async Task should_answer_415_when_an_executable_is_declared_as_csv()
    {
        // given
        var setup = _Endpoint(static options => options.AllowedContentTypes.Add("text/csv"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var executable = _Executable();
        var location = await host.CreateUploadAsync(executable.Length, _Metadata("users.csv", "text/csv"));

        // when
        using var response = await host.PatchAsync(location, executable, offset: 0);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType);
        await _ShouldBeGoneAsync(host, location);
    }

    [Theory]
    [InlineData("image/png", null)]
    [InlineData(null, "png")]
    public async Task should_answer_415_when_unrecognized_bytes_are_declared_as_png(
        string? allowedContentType,
        string? allowedExtension
    )
    {
        // given - an endpoint that allows PNG by content type, or by extension
        var setup = _Endpoint(options =>
        {
            if (allowedContentType is not null)
            {
                options.AllowedContentTypes.Add(allowedContentType);
            }

            if (allowedExtension is not null)
            {
                options.AllowedFileExtensions.Add(allowedExtension);
            }
        });
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var random = _RandomBytes();
        _DetectedExtension(random).Should().BeNull();
        var location = await host.CreateUploadAsync(random.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, random, offset: 0);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType);
        await _ShouldBeGoneAsync(host, location);
    }

    [Fact]
    public async Task should_accept_a_format_whose_allowed_extension_is_another_spelling_of_its_own()
    {
        // given - the endpoint allows "jpeg", and the format is recognized under the extension "jpg"
        var setup = _Endpoint(static options => options.AllowedFileExtensions.Add("jpeg"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var jpeg = _FileStartingWith(_JpegSignature, 1024);
        _DetectedExtension(jpeg).Should().Be("jpg");
        var location = await host.CreateUploadAsync(jpeg.Length, _Metadata("cat.jpeg", contentType: null));

        // when
        using var response = await host.PatchAsync(location, jpeg, offset: 0);
        var randomLocation = await host.CreateUploadAsync(1024, _Metadata("cat.jpeg", contentType: null));
        using var unrecognized = await host.PatchAsync(randomLocation, _RandomBytes(), offset: 0);

        // then - and bytes that are no JPEG are still refused under that spelling
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        unrecognized.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task should_answer_400_when_the_first_write_is_shorter_than_the_check_needs()
    {
        // given - a 1000-byte PNG whose first write carries 100 bytes
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var png = _Png(1000);
        var location = await host.CreateUploadAsync(png.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, png[..100], offset: 0);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest);
        await _ShouldBeGoneAsync(host, location);
    }

    [Fact]
    public async Task should_check_an_upload_shorter_than_the_check_needs_on_what_it_has()
    {
        // given - two 50-byte uploads declared as PNG: one is a PNG, the other a PDF
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var png = _Png(50);
        var pdf = _Pdf(50);
        var pngLocation = await host.CreateUploadAsync(png.Length, _Metadata("cat.png", "image/png"));
        var pdfLocation = await host.CreateUploadAsync(pdf.Length, _Metadata("cat.png", "image/png"));

        // when
        using var accepted = await host.PatchAsync(pngLocation, png, offset: 0);
        using var refused = await host.PatchAsync(pdfLocation, pdf, offset: 0);

        // then
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, pngLocation, png);
        await _ShouldBeRefusedAsync(refused, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task should_answer_400_when_the_first_write_of_a_deferred_length_upload_is_short()
    {
        // given - a type with no signature, so only the length of the first write can refuse the upload
        var setup = _Endpoint(static options => options.AllowedContentTypes.Add("text/csv"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var location = await host.CreateUploadAsync(uploadLength: null, _Metadata("users.csv", "text/csv"));

        // when - 100 bytes, with the length still deferred
        using var response = await host.PatchAsync(location, _Csv()[..100], offset: 0);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest);
        await _ShouldBeGoneAsync(host, location);
    }

    [Fact]
    public async Task should_check_a_short_upload_whose_first_write_also_declares_its_deferred_length()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var png = _Png(50);
        var location = await host.CreateUploadAsync(uploadLength: null, _Metadata("cat.png", "image/png"));

        // when - the write that carries the whole upload also says how long it is
        using var response = await host.PatchAsync(location, png, offset: 0, uploadLength: png.Length);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, png);
    }

    [Fact]
    public async Task should_leave_the_upload_when_the_first_write_carries_no_bytes()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var pdf = _Pdf();
        var location = await host.CreateUploadAsync(pdf.Length, _Metadata("cat.png", "image/png"));

        // when - an empty write, then the real first bytes
        using var empty = await host.PatchAsync(location, [], offset: 0);
        using var head = await host.HeadAsync(location);
        using var first = await host.PatchAsync(location, pdf, offset: 0);

        // then - nothing was written, so the write that brings the first bytes is the one that is checked
        empty.StatusCode.Should().Be(HttpStatusCode.NoContent);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        first.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task should_check_only_the_first_write()
    {
        // given - a PNG whose second half is sent as a second write
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var file = _Png(1000);
        _Executable(500).CopyTo(file, 500);
        var location = await host.CreateUploadAsync(file.Length, _Metadata("cat.png", "image/png"));

        // when
        using var first = await host.PatchAsync(location, file[..500], offset: 0);
        using var second = await host.PatchAsync(location, file[500..], offset: 500);

        // then
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, file);
    }

    [Fact]
    public async Task should_not_check_the_content_when_the_endpoint_turns_the_check_off()
    {
        // given
        var setup = _Endpoint(static options =>
        {
            options.AllowedContentTypes.Add("image/png");
            options.VerifyFileSignature = false;
        });
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var pdf = _Pdf(100);
        var location = await host.CreateUploadAsync(pdf.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, pdf, offset: 0);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, pdf);
    }

    [Fact]
    public async Task should_not_check_the_content_when_the_endpoint_lists_no_type()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Endpoint(static _ => { }), AbortToken);
        var executable = _Executable(100);
        var location = await host.CreateUploadAsync(executable.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, executable, offset: 0);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task should_hand_a_checked_upload_unchanged_to_a_store_that_reads_from_a_stream()
    {
        // given - a store with no pipeline support, which gets the request body as a stream after the check read it
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(new CreationOnlyTusStore(store))
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        options.AllowedContentTypes.Add("image/png");
                    }),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var png = _Png(200_000);
        _RandomBytes(100_000).CopyTo(png, 1000);
        var location = await host.CreateUploadAsync(png.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, png, offset: 0);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, location, png);
    }

    [Fact]
    public async Task should_refuse_and_keep_the_upload_when_the_store_cannot_delete()
    {
        // given - a store without the termination extension
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(new CreationOnlyTusStore(store))
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        options.AllowedContentTypes.Add("image/png");
                    }),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var pdf = _Pdf();
        var location = await host.CreateUploadAsync(pdf.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, pdf, offset: 0);

        // then - refused with nothing written; the upload stays until it expires
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType);
        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public async Task should_still_answer_415_when_the_store_fails_to_delete_the_refused_upload()
    {
        // given
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(new FailingDeleteStore(store))
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        options.AllowedContentTypes.Add("image/png");
                    }),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var pdf = _Pdf();
        var location = await host.CreateUploadAsync(pdf.Length, _Metadata("cat.png", "image/png"));

        // when
        using var response = await host.PatchAsync(location, pdf, offset: 0);

        // then - the refusal is the answer, not the store's failure, and nothing was written
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType);
        using var head = await host.HeadAsync(location);
        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
    }

    [Fact]
    public async Task should_check_the_first_bytes_on_a_real_server_and_store_the_upload_unchanged()
    {
        // given - Kestrel, whose request body is a pipe of its own and arrives in several reads
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = static (tus, store) =>
                tus.UseStore(store)
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        options.AllowedContentTypes.Add("image/png");
                    }),
            UseKestrel = true,
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var png = _Png(500_000);
        _RandomBytes(400_000).CopyTo(png, 1000);
        var pdf = _Pdf();
        var pngLocation = await host.CreateUploadAsync(png.Length, _Metadata("cat.png", "image/png"));
        var pdfLocation = await host.CreateUploadAsync(pdf.Length, _Metadata("cat.png", "image/png"));

        // when
        using var accepted = await host.PatchAsync(pngLocation, png, offset: 0);
        using var refused = await host.PatchAsync(pdfLocation, pdf, offset: 0);

        // then
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ShouldBeStoredAsync(host, pngLocation, png);
        await _ShouldBeRefusedAsync(refused, HttpStatusCode.UnsupportedMediaType);
        using var head = await host.HeadAsync(pdfLocation);
        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_accept_content_sent_with_the_creating_request()
    {
        // given
        await using var host = await TusEndpointTestHost.StartAsync(_Images(), AbortToken);
        var png = _Png();

        // when
        using var response = await host.CreateAsync(
            png.Length,
            _Metadata("cat.png", "image/png"),
            configure: request => request.Content = _Body(png)
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response
            .Headers.GetValues("Upload-Offset")
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(png.Length.ToString(CultureInfo.InvariantCulture));
        await _ShouldBeStoredAsync(host, response.Headers.Location!.ToString(), png);
    }

    [Fact]
    public async Task should_answer_201_with_offset_zero_and_then_404_when_content_sent_with_the_creating_request_is_refused()
    {
        // given
        using var responseCompleted = new SemaphoreSlim(0);
        var setup = _Images(app => _SignalWhenTheResponseCompletes(app, responseCompleted));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var executable = _Executable();

        // when
        using var response = await host.CreateAsync(
            executable.Length,
            _Metadata("cat.png", "image/png"),
            configure: request => request.Content = _Body(executable)
        );
        await responseCompleted.WaitAsync(AbortToken);

        // then - tusdotnet answers the creating request as created; the upload is removed once that answer is sent
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.GetValues("Upload-Offset").Should().ContainSingle().Which.Should().Be("0");
        await _ShouldBeGoneAsync(host, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task should_delete_a_refused_upload_in_the_tenant_it_was_created_in_after_the_creating_request()
    {
        // given - a store that records the tenant each delete runs in, as a store that keeps uploads per tenant needs
        using var responseCompleted = new SemaphoreSlim(0);
        var deletes = new List<string?>();
        var setup = new TusEndpointTestHostSetup
        {
            RegisterCurrentTenant = true,
            ConfigureTus = (tus, store) =>
                tus.UseStore(new TenantRecordingStore(store, deletes))
                    .AddUploadEndpoint(static options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        options.AllowedContentTypes.Add("image/png");
                    }),
            ConfigureMiddleware = app => _SignalWhenTheResponseCompletes(app, responseCompleted),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        host.Tenant = "acme";
        var executable = _Executable();

        // when
        using var response = await host.CreateAsync(
            executable.Length,
            _Metadata("cat.png", "image/png"),
            configure: request => request.Content = _Body(executable)
        );
        await responseCompleted.WaitAsync(AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        deletes.Should().Equal("acme");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    private static ByteArrayContent _Body(byte[] content)
    {
        return new ByteArrayContent(content)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/offset+octet-stream") },
        };
    }

    /// <summary>
    /// Releases <paramref name="completed"/> after everything the endpoint does once a response is sent. The
    /// server runs those callbacks last-registered first, and this one is registered before the endpoint runs.
    /// </summary>
    private static void _SignalWhenTheResponseCompletes(IApplicationBuilder app, SemaphoreSlim completed)
    {
        app.Use(
            (context, next) =>
            {
                context.Response.OnCompleted(() =>
                {
                    completed.Release();

                    return Task.CompletedTask;
                });

                return next(context);
            }
        );
    }

    /// <summary>A store whose every delete fails.</summary>
    private sealed class FailingDeleteStore(TusDiskStore inner) : CreationOnlyTusStore(inner), ITusTerminationStore
    {
        public Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
        {
            throw new IOException("The store is not reachable.");
        }
    }

    /// <summary>A store that can delete, and records the ambient tenant of every delete.</summary>
    private sealed class TenantRecordingStore(TusDiskStore inner, List<string?> deletes)
        : CreationOnlyTusStore(inner),
            ITusTerminationStore
    {
        private readonly TusDiskStore _inner = inner;

        public Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
        {
            deletes.Add(AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId);

            return _inner.DeleteFileAsync(fileId, cancellationToken);
        }
    }
}
