// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Options;
using Headless.IO;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Primitives;

namespace Tests.Binding;

public sealed class UploadedFileModelBinderTests : TestBase
{
    [Fact]
    public async Task should_bind_the_form_file_named_after_the_model()
    {
        var context = _Context(_File("avatar", "a.png", [1, 2, 3]), modelName: "avatar");

        await new UploadedFileModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        var file = context.Result.Model.Should().BeAssignableTo<IUploadedFile>().Subject;
        file.FileName.Should().Be("a.png");
        file.Length.Should().Be(3);
        await using var stream = file.OpenReadStream();
        stream.GetAllBytes().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_bind_the_part_named_after_the_property_when_the_model_name_carries_a_prefix()
    {
        var context = _Context(_File("avatar", "a.png", [1]), modelName: "form.avatar", fieldName: "avatar");

        await new UploadedFileModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
    }

    [Fact]
    public async Task should_leave_the_model_unbound_when_no_part_has_its_name()
    {
        var context = _Context(_File("other", "a.png", [1]), modelName: "avatar");

        await new UploadedFileModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
    }

    [Fact]
    public async Task should_leave_the_model_unbound_when_the_part_is_an_empty_unnamed_file()
    {
        var context = _Context(_File("avatar", "", []), modelName: "avatar");

        await new UploadedFileModelBinder().BindModelAsync(context);

        context.Result.IsModelSet.Should().BeFalse();
    }

    [Fact]
    public void should_register_the_binder_and_mark_uploaded_files_as_form_files_once()
    {
        var options = new MvcOptions();
        var configure = new ConfigureMvcApiOptions();

        configure.Configure(options);
        configure.Configure(options);

        options.ModelBinderProviders.OfType<UploadedFileModelBinderProvider>().Should().ContainSingle();
        options
            .ModelMetadataDetailsProviders.OfType<BindingSourceMetadataProvider>()
            .Should()
            .ContainSingle(x => x.Type == typeof(IUploadedFile) && x.BindingSource == BindingSource.FormFile);
    }

    private static FormFile _File(string name, string fileName, byte[] content) =>
        new(new MemoryStream(content), 0, content.Length, name, fileName) { Headers = new HeaderDictionary() };

    private static DefaultModelBindingContext _Context(FormFile file, string modelName, string? fieldName = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "multipart/form-data; boundary=x";
        httpContext.Request.Form = new FormCollection([], new FormFileCollection { file });

        return new DefaultModelBindingContext
        {
            ActionContext = new ActionContext { HttpContext = httpContext },
            ModelName = modelName,
            FieldName = fieldName ?? modelName,
        };
    }
}
