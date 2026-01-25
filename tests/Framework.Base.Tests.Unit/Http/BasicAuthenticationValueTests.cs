// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Http;

namespace Tests.Http;

public sealed class BasicAuthenticationValueTests
{
    [Fact]
    public void constructor_should_create_basic_header_with_scheme_only()
    {
        // when
        var result = new BasicAuthenticationValue();

        // then
        result.Scheme.Should().Be("Basic");
        result.Parameter.Should().BeNull();
    }

    [Fact]
    public void constructor_should_encode_username_and_password()
    {
        // given
        const string userName = "user";
        const string password = "pass";
        var expectedParameter = Convert.ToBase64String("user:pass"u8);

        // when
        var result = new BasicAuthenticationValue(userName, password);

        // then
        result.Scheme.Should().Be("Basic");
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void constructor_should_handle_empty_password()
    {
        // given
        const string userName = "user";
        const string password = "";
        var expectedParameter = Convert.ToBase64String("user:"u8);

        // when
        var result = new BasicAuthenticationValue(userName, password);

        // then
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void constructor_should_handle_colon_in_password()
    {
        // given
        const string userName = "user";
        const string password = "pass:word:extra";
        var expectedParameter = Convert.ToBase64String("user:pass:word:extra"u8);

        // when
        var result = new BasicAuthenticationValue(userName, password);

        // then
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void constructor_should_handle_special_characters()
    {
        // given
        const string userName = "user@domain.com";
        const string password = "p@$$w0rd!";
        var expectedParameter = Convert.ToBase64String(Encoding.UTF8.GetBytes("user@domain.com:p@$$w0rd!"));

        // when
        var result = new BasicAuthenticationValue(userName, password);

        // then
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void constructor_string_overload_should_encode_value()
    {
        // given
        const string value = "user:pass";
        var expectedParameter = Convert.ToBase64String("user:pass"u8);

        // when
        var result = new BasicAuthenticationValue(value);

        // then
        result.Scheme.Should().Be("Basic");
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void constructor_should_handle_unicode_characters()
    {
        // given
        const string userName = "用户";
        const string password = "密码";
        var expectedParameter = Convert.ToBase64String(Encoding.UTF8.GetBytes("用户:密码"));

        // when
        var result = new BasicAuthenticationValue(userName, password);

        // then
        result.Parameter.Should().Be(expectedParameter);
    }

    [Fact]
    public void BasicScheme_constant_should_be_Basic()
    {
        BasicAuthenticationValue.BasicScheme.Should().Be("Basic");
    }

    [Fact]
    public void ToString_should_return_formatted_header()
    {
        // given
        var auth = new BasicAuthenticationValue("user", "pass");
        var expectedParameter = Convert.ToBase64String("user:pass"u8);

        // when
        var result = auth.ToString();

        // then
        result.Should().Be($"Basic {expectedParameter}");
    }
}
