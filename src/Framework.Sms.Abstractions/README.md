# Framework.Sms.Abstraction

This package provides the core abstractions and contracts for SMS functionality within the framework. It defines the common interface and data models that specific SMS provider implementations must adhere to.

## Key Components

-   **ISmsSender.cs**: Defines the `ISmsSender` interface, which is the primary contract for sending SMS messages.
-   **Contracts/**: Contains the data transfer objects (DTOs) used for SMS operations.
    -   `SendSingleSmsRequest.cs`: Represents the request payload for sending a single SMS.
    -   `SendSingleSmsResponse.cs`: Represents the response received after sending a single SMS.

## Usage

This package is intended to be referenced by specific SMS provider implementations (e.g., Twilio, Vodafone, AWS) and by the application layer that consumes SMS services. By programming against `ISmsSender`, the application can remain agnostic of the underlying SMS provider.
