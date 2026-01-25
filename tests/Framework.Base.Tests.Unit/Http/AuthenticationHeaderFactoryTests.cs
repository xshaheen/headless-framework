// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Framework.Http;

namespace Tests.Http;

public sealed class AuthenticationHeaderFactoryTests
{
    [Fact]
    public void CreateBasic_should_return_basic_header_with_encoded_credentials()
    {
        // given
        const string userName = "user";
        const string password = "pass";
        var expectedParameter = Convert.ToBase64String("user:pass"u8);

        // when
        var result = AuthenticationHeaderFactory.CreateBasic(userName, password);

        // then
        result.Should().NotBeNull();
        result.Scheme.Should().Be("Basic");
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void CreateBasic_should_encode_empty_password()
    {
        // given
        const string userName = "user";
        const string password = "";
        var expectedParameter = Convert.ToBase64String("user:"u8);

        // when
        var result = AuthenticationHeaderFactory.CreateBasic(userName, password);

        // then
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void CreateBasic_should_encode_password_with_colon()
    {
        // given
        const string userName = "user";
        const string password = "pass:word";
        var expectedParameter = Convert.ToBase64String("user:pass:word"u8);

        // when
        var result = AuthenticationHeaderFactory.CreateBasic(userName, password);

        // then
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void CreateBasic_should_throw_for_null_username()
    {
        // when
        var act = () => AuthenticationHeaderFactory.CreateBasic(null!, "pass");

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("userName");
    }

    [Fact]
    public void CreateBasic_should_throw_for_empty_username()
    {
        // when
        var act = () => AuthenticationHeaderFactory.CreateBasic("", "pass");

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("userName");
    }

    [Fact]
    public void CreateBasic_should_throw_for_whitespace_username()
    {
        // when
        var act = () => AuthenticationHeaderFactory.CreateBasic("   ", "pass");

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("userName");
    }

    [Fact]
    public void CreateBasic_should_throw_for_null_password()
    {
        // when
        var act = () => AuthenticationHeaderFactory.CreateBasic("user", null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("password");
    }

    [Fact]
    public void CreateBasic_string_overload_should_return_basic_header()
    {
        // given
        const string value = "user:pass";
        var expectedParameter = Convert.ToBase64String("user:pass"u8);

        // when
        var result = AuthenticationHeaderFactory.CreateBasic(value);

        // then
        result.Scheme.Should().Be("Basic");
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void CreateBasic_string_overload_should_throw_for_null()
    {
        // when
        var act = () => AuthenticationHeaderFactory.CreateBasic((string)null!);

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [Fact]
    public void CreateBasic_string_overload_should_throw_for_empty()
    {
        // when
        var act = () => AuthenticationHeaderFactory.CreateBasic("");

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void CreateBasic_bytes_overload_should_return_basic_header()
    {
        // given
        ReadOnlySpan<byte> value = "user:pass"u8;
        var expectedParameter = Convert.ToBase64String("user:pass"u8);

        // when
        var result = AuthenticationHeaderFactory.CreateBasic(value);

        // then
        result.Scheme.Should().Be("Basic");
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void CreateBasic_bytes_overload_should_handle_empty_span()
    {
        // given
        ReadOnlySpan<byte> value = [];

        // when
        var result = AuthenticationHeaderFactory.CreateBasic(value);

        // then
        result.Scheme.Should().Be("Basic");
        result.Parameter.Should().Be("");
    }

    [Fact]
    public void CreateBasic_should_handle_unicode_characters()
    {
        // given
        const string userName = "用户";
        const string password = "密码";
        var expectedParameter = Convert.ToBase64String(Encoding.UTF8.GetBytes("用户:密码"));

        // when
        var result = AuthenticationHeaderFactory.CreateBasic(userName, password);

        // then
        result.Parameter.Should().Be(expectedParameter);
    }
}
