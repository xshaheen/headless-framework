// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.RabbitMq;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RabbitMqOptionsValidatorTests : TestBase
{
    private readonly RabbitMqOptionsValidator _validator = new();

    [Fact]
    public void should_pass_validation_with_valid_options()
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_pass_validation_with_default_port()
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = -1,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_hostname_is_null_or_whitespace(string? hostName)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = hostName!,
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("HostName is required"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void should_fail_validation_when_port_is_out_of_range(int port)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = port,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Port must be -1"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5672)]
    [InlineData(65535)]
    public void should_pass_validation_when_port_is_in_valid_range(int port)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = port,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_virtualhost_is_null_or_whitespace(string? virtualHost)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = virtualHost!,
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("VirtualHost is required"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_exchangename_is_null_or_whitespace(string? exchangeName)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = exchangeName!,
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("ExchangeName is required"));
    }

    [Fact]
    public void should_fail_validation_when_exchangename_has_invalid_format()
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "invalid exchange name",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Invalid ExchangeName"));
    }

    [Fact]
    public void should_fail_validation_when_exchangename_exceeds_max_length()
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = new string('a', 256),
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Invalid ExchangeName"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_username_is_null_or_whitespace(string? userName)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = userName!,
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("UserName is required"));
    }

    [Theory]
    [InlineData("guest")]
    [InlineData("GUEST")]
    [InlineData("Guest")]
    public void should_fail_validation_when_username_is_guest(string userName)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = userName,
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("UserName cannot be 'guest'"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_password_is_null_or_whitespace(string? password)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = password!,
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Password is required"));
    }

    [Theory]
    [InlineData("guest")]
    [InlineData("GUEST")]
    [InlineData("Guest")]
    public void should_fail_validation_when_password_is_guest(string password)
    {
        // given
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = password,
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Password cannot be 'guest'"));
    }
}
