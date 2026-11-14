// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Headless.Context;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Stores;

namespace Tests.Endpoint;

public sealed class TusValidationTests : TestBase
{
    private static readonly MediaTypeHeaderValue _PlainText = new("text/plain");

    /// <summary>A host whose default endpoint has a 1 MiB limit and the rules <paramref name="configure"/> sets.</summary>
    private static TusEndpointTestHostSetup _Endpoint(
        Action<TusUploadEndpointOptions> configure,
        Action<HeadlessTusUploadEndpointBuilder>? endpoint = null,
        Action<IServiceCollection>? services = null
    )
    {
        return new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) =>
            {
                var builder = tus.UseStore(store)
                    .AddUploadEndpoint(options =>
                    {
                        options.MaxSize = TusEndpointTestHost.DefaultMaxSize;
                        configure(options);
                    });

                endpoint?.Invoke(builder);
            },
            ConfigureServices = services,
        };
    }

    /// <summary>Encodes an <c>Upload-Metadata</c> header; a <see langword="null"/> value sends the key alone.</summary>
    private static string _Metadata(params (string Key, string? Value)[] entries)
    {
        return string.Join(
            ',',
            entries.Select(static entry =>
                entry.Value is null
                    ? entry.Key
                    : $"{entry.Key} {Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Value))}"
            )
        );
    }

    private static async Task _ShouldBeRefusedAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string messagePart
    )
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType.Should().Be(_PlainText);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain(messagePart);
    }

    public static TheoryData<string?> MetadataWithoutAFileName =>
        [
            (string?)null,
            // another field only
            _Metadata(("filetype", "image/png")),
            // the key with no value
            _Metadata(("filename", null)),
            // a value that decodes to white space
            _Metadata(("filename", " ")),
        ];

    [Theory]
    [MemberData(nameof(MetadataWithoutAFileName))]
    public async Task should_answer_400_naming_the_field_when_required_metadata_is_missing_or_empty(string? metadata)
    {
        // given
        var setup = _Endpoint(static options => options.RequiredMetadata.Add("filename"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, metadata);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "\"filename\"");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_create_the_upload_when_every_required_metadata_field_has_a_value()
    {
        // given
        var setup = _Endpoint(static options =>
        {
            options.RequiredMetadata.Add("filename");
            options.RequiredMetadata.Add("album");
        });
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var complete = await host.CreateAsync(
            uploadLength: 10,
            _Metadata(("filename", "cat.png"), ("album", "pets"))
        );
        using var partial = await host.CreateAsync(uploadLength: 10, _Metadata(("filename", "cat.png")));

        // then
        complete.StatusCode.Should().Be(HttpStatusCode.Created);
        await _ShouldBeRefusedAsync(partial, HttpStatusCode.BadRequest, "\"album\"");
    }

    [Theory]
    [InlineData("filetype", "image/png")]
    [InlineData("type", "image/png")]
    [InlineData("filetype", "IMAGE/PNG")]
    [InlineData("filetype", "image/png; charset=binary")]
    public async Task should_create_the_upload_when_the_declared_content_type_is_allowed(string key, string value)
    {
        // given
        var setup = _Endpoint(static options => options.AllowedContentTypes.Add("image/png"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, _Metadata((key, value)));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("filetype")]
    [InlineData("type")]
    public async Task should_answer_415_when_the_declared_content_type_is_not_allowed(string key)
    {
        // given
        var setup = _Endpoint(static options => options.AllowedContentTypes.Add("image/png"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, _Metadata((key, "application/pdf")));

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType, "application/pdf");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    public static TheoryData<string?> MetadataWithoutAContentType =>
        [
            (string?)null,
            _Metadata(("filename", "cat.png")),
            _Metadata(("filetype", null)),
            _Metadata(("filetype", " ")),
        ];

    [Theory]
    [MemberData(nameof(MetadataWithoutAContentType))]
    public async Task should_answer_415_when_no_content_type_is_declared_and_the_endpoint_lists_some(string? metadata)
    {
        // given
        var setup = _Endpoint(static options => options.AllowedContentTypes.Add("image/png"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, metadata);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType, "content type");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData("filename", "cat.png", "png")]
    [InlineData("name", "cat.png", "png")]
    [InlineData("filename", "CAT.PNG", "png")]
    [InlineData("filename", "cat.png", ".PNG")]
    [InlineData("filename", "holiday.2026.png", "png")]
    public async Task should_create_the_upload_when_the_declared_extension_is_allowed(
        string key,
        string fileName,
        string allowed
    )
    {
        // given
        var setup = _Endpoint(options => options.AllowedFileExtensions.Add(allowed));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, _Metadata((key, fileName)));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("filename")]
    [InlineData("name")]
    public async Task should_answer_415_when_the_declared_extension_is_not_allowed(string key)
    {
        // given
        var setup = _Endpoint(static options => options.AllowedFileExtensions.Add("png"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when - the allowed extension is in the name, but is not the name's extension
        using var response = await host.CreateAsync(uploadLength: 10, _Metadata((key, "cat.png.exe")));

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType, "\"exe\"");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    public static TheoryData<string?> MetadataWithoutAnExtension =>
        [
            (string?)null,
            _Metadata(("filetype", "image/png")),
            _Metadata(("filename", null)),
            _Metadata(("filename", "README")),
            _Metadata(("filename", "archive.")),
        ];

    [Theory]
    [MemberData(nameof(MetadataWithoutAnExtension))]
    public async Task should_answer_415_when_no_extension_is_declared_and_the_endpoint_lists_some(string? metadata)
    {
        // given
        var setup = _Endpoint(static options => options.AllowedFileExtensions.Add("png"));
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, metadata);

        // then
        await _ShouldBeRefusedAsync(response, HttpStatusCode.UnsupportedMediaType, "extension");
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_prefer_filename_and_filetype_when_both_spellings_are_sent()
    {
        // given
        var setup = _Endpoint(static options =>
        {
            options.AllowedContentTypes.Add("image/png");
            options.AllowedFileExtensions.Add("png");
        });
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var allowedFirst = await host.CreateAsync(
            uploadLength: 10,
            _Metadata(
                ("filename", "cat.png"),
                ("name", "cat.exe"),
                ("filetype", "image/png"),
                ("type", "application/pdf")
            )
        );
        using var refusedFirst = await host.CreateAsync(
            uploadLength: 10,
            _Metadata(("filename", "cat.exe"), ("name", "cat.png"), ("filetype", "image/png"), ("type", "image/png"))
        );

        // then
        allowedFirst.StatusCode.Should().Be(HttpStatusCode.Created);
        refusedFirst.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    // a tab before the key: tusdotnet reads "\tfiletype" as a key of its own, beside the allowed "type"
    [InlineData("\tfiletype dGV4dC9odG1s")]
    [InlineData("\tfilename ZXZpbC5leGU=")]
    // a no-break space before the key
    [InlineData("\u00A0filetype dGV4dC9odG1s")]
    // a tab inside the key
    [InlineData("file\ttype aW1hZ2UvcG5n")]
    // a control character that is not white space
    [InlineData("file\u007Ftype aW1hZ2UvcG5n")]
    public async Task should_answer_400_when_a_metadata_key_carries_white_space_or_a_control_character(string oddEntry)
    {
        // given - the declared "type" and "name" are allowed, so only the odd key can refuse the upload
        var metadata = $"type aW1hZ2UvcG5n,name Y2F0LnBuZw==,{oddEntry}";
        var log = new ValidatorLog();
        var setup = _Endpoint(
            static options =>
            {
                options.AllowedContentTypes.Add("image/png");
                options.AllowedFileExtensions.Add("png");
            },
            static endpoint => endpoint.ValidateWith<QuotaValidator>(),
            services => services.AddSingleton(log)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10, metadata);

        // then - refused before any rule or validator reads a key the stored metadata would rename
        await _ShouldBeRefusedAsync(response, HttpStatusCode.BadRequest, "Upload-Metadata key");
        log.Calls.Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(host.Directory).Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_400_to_an_empty_upload_unless_the_endpoint_allows_it()
    {
        // given
        await using var refusing = await TusEndpointTestHost.StartAsync(_Endpoint(static _ => { }), AbortToken);
        await using var allowing = await TusEndpointTestHost.StartAsync(
            _Endpoint(static options => options.AllowEmptyUploads = true),
            AbortToken
        );

        // when
        using var refused = await refusing.CreateAsync(uploadLength: 0);
        var leftByTheRefusal = Directory.EnumerateFileSystemEntries(refusing.Directory).ToList();
        using var deferred = await refusing.CreateAsync(uploadLength: null);
        using var allowed = await allowing.CreateAsync(uploadLength: 0);

        // then - an upload whose length is deferred is not known to be empty
        await _ShouldBeRefusedAsync(refused, HttpStatusCode.BadRequest, "empty");
        leftByTheRefusal.Should().BeEmpty();
        deferred.StatusCode.Should().Be(HttpStatusCode.Created);
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task should_apply_the_rules_bound_from_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["MaxSize"] = "1000",
                    ["AllowedContentTypes:0"] = "image/png",
                    ["AllowedFileExtensions:0"] = "png",
                    ["RequiredMetadata:0"] = "album",
                    ["AllowEmptyUploads"] = "true",
                    ["VerifyFileSignature"] = "false",
                }
            )
            .Build();
        var setup = new TusEndpointTestHostSetup
        {
            ConfigureTus = (tus, store) => tus.UseStore(store).AddUploadEndpoint(configuration),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        var options = host.Services.GetRequiredService<IOptions<TusUploadEndpointOptions>>().Value;

        // when
        using var noAlbum = await host.CreateAsync(
            uploadLength: 10,
            _Metadata(("filename", "cat.png"), ("filetype", "image/png"))
        );
        using var wrongType = await host.CreateAsync(
            uploadLength: 10,
            _Metadata(("filename", "cat.png"), ("filetype", "image/gif"), ("album", "pets"))
        );
        using var wrongExtension = await host.CreateAsync(
            uploadLength: 10,
            _Metadata(("filename", "cat.gif"), ("filetype", "image/png"), ("album", "pets"))
        );
        using var empty = await host.CreateAsync(
            uploadLength: 0,
            _Metadata(("filename", "cat.png"), ("filetype", "image/png"), ("album", "pets"))
        );

        // then
        noAlbum.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wrongType.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        wrongExtension.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        empty.StatusCode.Should().Be(HttpStatusCode.Created);
        options.VerifyFileSignature.Should().BeFalse();
    }

    [Fact]
    public void should_allow_everything_and_check_content_by_default()
    {
        // when
        var options = new TusUploadEndpointOptions();

        // then
        options.AllowedContentTypes.Should().BeEmpty();
        options.AllowedFileExtensions.Should().BeEmpty();
        options.RequiredMetadata.Should().BeEmpty();
        options.AllowEmptyUploads.Should().BeFalse();
        options.VerifyFileSignature.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(TusUploadEndpointOptions.AllowedContentTypes))]
    [InlineData(nameof(TusUploadEndpointOptions.AllowedFileExtensions))]
    [InlineData(nameof(TusUploadEndpointOptions.RequiredMetadata))]
    public async Task should_fail_startup_when_a_list_has_a_blank_entry(string list)
    {
        // given
        var setup = _Endpoint(options =>
        {
            var entries = list switch
            {
                nameof(TusUploadEndpointOptions.AllowedContentTypes) => options.AllowedContentTypes,
                nameof(TusUploadEndpointOptions.AllowedFileExtensions) => options.AllowedFileExtensions,
                _ => options.RequiredMetadata,
            };

            entries.Add(" ");
        });

        // when
        var act = () => TusEndpointTestHost.StartAsync(setup, AbortToken);

        // then
        var failure = (await act.Should().ThrowAsync<OptionsValidationException>()).Which;
        failure.Failures.Should().ContainSingle().Which.Should().Contain(list);
    }

    [Fact]
    public async Task should_run_validators_in_declaration_order_and_answer_with_the_first_failure()
    {
        // given - the first validator refuses uploads above 100 bytes, the second every upload named "blocked"
        var log = new ValidatorLog();
        var setup = _Endpoint(
            static _ => { },
            static endpoint => endpoint.ValidateWith<QuotaValidator>().ValidateWith<BlockedNameValidator>(),
            services => services.AddSingleton(log)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var accepted = await host.CreateAsync(uploadLength: 10, _Metadata(("filename", "cat.png")));
        using var overQuota = await host.CreateAsync(uploadLength: 500, _Metadata(("filename", "blocked")));
        using var blocked = await host.CreateAsync(uploadLength: 10, _Metadata(("filename", "blocked")));

        // then - a failure stops the validators after it, and its status and message are the response
        accepted.StatusCode.Should().Be(HttpStatusCode.Created);
        await _ShouldBeRefusedAsync(overQuota, HttpStatusCode.Forbidden, "The upload is over the quota of 100 bytes.");
        await _ShouldBeRefusedAsync(blocked, HttpStatusCode.BadRequest, "This file name is blocked.");
        log.Calls.Should().Equal("quota:10", "name:cat.png", "quota:500", "quota:10", "name:blocked");
        Directory.EnumerateFiles(host.Directory, "*.metadata").Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_run_validators_when_an_endpoint_rule_refuses_the_upload()
    {
        // given
        var log = new ValidatorLog();
        var setup = _Endpoint(
            static options => options.RequiredMetadata.Add("filename"),
            static endpoint => endpoint.ValidateWith<QuotaValidator>(),
            services => services.AddSingleton(log)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        log.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task should_resolve_a_validator_from_the_requests_services()
    {
        // given - the validator takes the signed-in user, which the host registers per request
        var setup = _Endpoint(static _ => { }, static endpoint => endpoint.ValidateWith<NotBobValidator>());
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var alice = await host.CreateAsync(uploadLength: 10);
        host.User = "bob";
        using var bob = await host.CreateAsync(uploadLength: 10);

        // then
        alice.StatusCode.Should().Be(HttpStatusCode.Created);
        await _ShouldBeRefusedAsync(bob, HttpStatusCode.Forbidden, "bob may not upload.");
    }

    [Fact]
    public async Task should_validate_with_the_instance_the_application_registered()
    {
        // given - the application's own instance, built over a log the container does not hold, so a validator
        // the container built itself could not exist
        var log = new ValidatorLog();
        var registered = new QuotaValidator(log);
        var setup = _Endpoint(
            static _ => { },
            static endpoint => endpoint.ValidateWith<QuotaValidator>(),
            services => services.AddSingleton(registered)
        );
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);

        // when
        using var response = await host.CreateAsync(uploadLength: 10);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        log.Calls.Should().Equal("quota:10");
    }

    [Fact]
    public void should_register_a_validator_only_when_the_application_has_not()
    {
        // given - one validator the application registered before AddHeadlessTus, and one it did not
        var registered = new QuotaValidator(new ValidatorLog());
        var services = new ServiceCollection();
        services.AddSingleton(registered);

        // when
        services.AddHeadlessTus(static tus =>
            tus.UseStore(new TusDiskStore(Path.GetTempPath()))
                .AddUploadEndpoint(static options => options.MaxSize = 100)
                .ValidateWith<QuotaValidator>()
                .ValidateWith<BlockedNameValidator>()
        );

        // then
        services
            .Where(static descriptor => descriptor.ServiceType == typeof(QuotaValidator))
            .Should()
            .ContainSingle()
            .Which.ImplementationInstance.Should()
            .BeSameAs(registered);
        services
            .Where(static descriptor => descriptor.ServiceType == typeof(BlockedNameValidator))
            .Should()
            .ContainSingle()
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
    }

    [Theory]
    [InlineData(10L)]
    [InlineData(null)]
    public async Task should_describe_the_upload_to_a_validator(long? uploadLength)
    {
        // given
        var seen = new SeenRequests();
        var setup = new TusEndpointTestHostSetup
        {
            RegisterCurrentTenant = true,
            ConfigureTus = static (tus, store) =>
                tus.UseStore(store)
                    .AddUploadEndpoint(static options => options.MaxSize = TusEndpointTestHost.DefaultMaxSize)
                    .ValidateWith<RecordingValidator>(),
            ConfigureServices = services => services.AddSingleton(seen),
        };
        await using var host = await TusEndpointTestHost.StartAsync(setup, AbortToken);
        host.Tenant = "acme";

        // when
        using var response = await host.CreateAsync(uploadLength, _Metadata(("filename", "cat.png"), ("draft", null)));

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var request = seen.Requests.Should().ContainSingle().Which;
        request.Length.Should().Be(uploadLength);
        request
            .Metadata.Should()
            .BeEquivalentTo(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["filename"] = "cat.png", ["draft"] = "" }
            );
        request.TenantId.Should().Be("acme");
        request.OwnerKey.Should().Be(TusEndpointTestHost.DefaultUser);
        request.Path.Should().Be(TusEndpointTestHost.Route);
    }

    [Fact]
    public void should_refuse_with_400_when_a_failure_names_no_status()
    {
        // when
        var failure = TusUploadValidationResult.Failure("Not today.");

        // then
        failure.IsSuccess.Should().BeFalse();
        failure.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        failure.Message.Should().Be("Not today.");
        TusUploadValidationResult.Success.IsSuccess.Should().BeTrue();
        TusUploadValidationResult.Success.StatusCode.Should().BeNull();
        TusUploadValidationResult.Success.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(399)]
    [InlineData(600)]
    public void should_reject_a_failure_status_that_is_not_an_error(int statusCode)
    {
        // when
        var act = () => TusUploadValidationResult.Failure(statusCode, "Not today.");

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void should_reject_a_failure_without_a_message(string message)
    {
        // when
        var act = () => TusUploadValidationResult.Failure(StatusCodes.Status403Forbidden, message);

        // then
        act.Should().Throw<ArgumentException>();
    }

    private sealed class ValidatorLog
    {
        private readonly Lock _lock = new();
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_lock)
                {
                    return [.. _calls];
                }
            }
        }

        public void Add(string call)
        {
            lock (_lock)
            {
                _calls.Add(call);
            }
        }
    }

    private sealed class QuotaValidator(ValidatorLog log) : ITusUploadValidator
    {
        public ValueTask<TusUploadValidationResult> ValidateAsync(
            TusUploadRequest request,
            CancellationToken cancellationToken
        )
        {
            log.Add(string.Create(CultureInfo.InvariantCulture, $"quota:{request.Length}"));

            return ValueTask.FromResult(
                request.Length > 100
                    ? TusUploadValidationResult.Failure(
                        StatusCodes.Status403Forbidden,
                        "The upload is over the quota of 100 bytes."
                    )
                    : TusUploadValidationResult.Success
            );
        }
    }

    private sealed class BlockedNameValidator(ValidatorLog log) : ITusUploadValidator
    {
        public ValueTask<TusUploadValidationResult> ValidateAsync(
            TusUploadRequest request,
            CancellationToken cancellationToken
        )
        {
            var name = request.Metadata["filename"];
            log.Add($"name:{name}");

            return ValueTask.FromResult(
                string.Equals(name, "blocked", StringComparison.Ordinal)
                    ? TusUploadValidationResult.Failure("This file name is blocked.")
                    : TusUploadValidationResult.Success
            );
        }
    }

    private sealed class NotBobValidator(ICurrentUser user) : ITusUploadValidator
    {
        public ValueTask<TusUploadValidationResult> ValidateAsync(
            TusUploadRequest request,
            CancellationToken cancellationToken
        )
        {
            var userId = user.UserId?.GetUnderlyingPrimitiveValue();

            return ValueTask.FromResult(
                string.Equals(userId, "bob", StringComparison.Ordinal)
                    ? TusUploadValidationResult.Failure(StatusCodes.Status403Forbidden, $"{userId} may not upload.")
                    : TusUploadValidationResult.Success
            );
        }
    }

    private sealed record SeenRequest(
        long? Length,
        IReadOnlyDictionary<string, string> Metadata,
        string? TenantId,
        string OwnerKey,
        string Path
    );

    private sealed class SeenRequests
    {
        public List<SeenRequest> Requests { get; } = [];
    }

    private sealed class RecordingValidator(SeenRequests seen) : ITusUploadValidator
    {
        public ValueTask<TusUploadValidationResult> ValidateAsync(
            TusUploadRequest request,
            CancellationToken cancellationToken
        )
        {
            seen.Requests.Add(
                new SeenRequest(
                    request.Length,
                    request.Metadata,
                    request.TenantId,
                    request.OwnerKey,
                    request.HttpContext.Request.Path
                )
            );

            return ValueTask.FromResult(TusUploadValidationResult.Success);
        }
    }
}
