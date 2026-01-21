// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Testing.Tests;

namespace Tests;

public sealed class RabbitMQOptionsValidatorTests : TestBase
{
    private readonly RabbitMQOptionsValidator _validator = new();

    [Fact]
    public void should_pass_validation_with_valid_options()
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_pass_validation_with_default_port()
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = -1,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_hostname_is_null_or_whitespace(string? hostName)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = hostName!,
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("HostName is required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void should_fail_validation_when_port_is_out_of_range(int port)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = port,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Port must be -1");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5672)]
    [InlineData(65535)]
    public void should_pass_validation_when_port_is_in_valid_range(int port)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = port,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_virtualhost_is_null_or_whitespace(string? virtualHost)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = virtualHost!,
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("VirtualHost is required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_exchangename_is_null_or_whitespace(string? exchangeName)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = exchangeName!,
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ExchangeName is required");
    }

    [Fact]
    public void should_fail_validation_when_exchangename_has_invalid_format()
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "invalid exchange name",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Invalid ExchangeName");
    }

    [Fact]
    public void should_fail_validation_when_exchangename_exceeds_max_length()
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = new string('a', 256),
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Invalid ExchangeName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_username_is_null_or_whitespace(string? userName)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = userName!,
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("UserName is required");
    }

    [Theory]
    [InlineData("guest")]
    [InlineData("GUEST")]
    [InlineData("Guest")]
    public void should_fail_validation_when_username_is_guest(string userName)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = userName,
            Password = "secure_password",
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("UserName cannot be 'guest'");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_validation_when_password_is_null_or_whitespace(string? password)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = password!,
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Password is required");
    }

    [Theory]
    [InlineData("guest")]
    [InlineData("GUEST")]
    [InlineData("Guest")]
    public void should_fail_validation_when_password_is_guest(string password)
    {
        // Given
        var options = new RabbitMQOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "myapp_user",
            Password = password,
            VirtualHost = "/",
            ExchangeName = "test-exchange",
        };

        // When
        var result = _validator.Validate(null, options);

        // Then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Password cannot be 'guest'");
    }
}
