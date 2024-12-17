// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Api.ApiExplorer;

public static class SwaggerInformation
{
    [StringSyntax("markdown")]
    public const string ResponsesDescription = """
        ## Success Response `2xx`

        <details>
        <summary>200 Ok</summary>

        - The request has succeeded.
        - [rfc2068#section-10.2.1](https://tools.ietf.org/html/rfc2068#section-10.2.1)

        </details>

        <details>
        <summary>202 Accepted</summary>

        - The request has been accepted for processing, but the processing has not been completed. The response may contain the current status and a reference id.
        - [rfc2068#section-10.2.3](https://tools.ietf.org/html/rfc2068#section-10.2.3)

        </details>

        <details>
        <summary>204 NoContent</summary>

        - The request has succeeded. But there is no new information to send (Response not have message-body).
        - [rfc2068#section-10.2.5](https://tools.ietf.org/html/rfc2068#section-10.2.5)

        </details>

        ## Client Error `4xx`

        Requests made to our APIs can result in several different error responses.
        The following document describes the common errors values.
        API errors responses is based on [RFC7807](https://tools.ietf.org/html/rfc7807).

        <details>
        <summary>404 Not Found</summary>

        - The requested resource was not found. This response will be returned if the URL is entirely invalid (i.e. /request), or if it is a URL that could be valid but is referencing something that does not exist (i.e. /items/12344).
        - [rfc2068#section-10.4.5](https://tools.ietf.org/html/rfc2068#section-10.4.5)
        - Endpoint not found response body example:

        ```json
        {
          "type": "/errors/endpoint-not-found",
          "title": "endpoint-not-found",
          "status": 404,
          "detail": "The requested endpoint '/api/example' was not found.",
          "instance": "/api/example",
          "version": "1.0.0",
          "timestamp": "2020-12-05T04:04:24.5016592Z",
          "traceId": "00-5ccef78897b44c4e83cf650d878c4873-905457454dd84943-00"
        }
        ```

        - Entity not found response body example:

        ```json
        {
          "type": "/errors/entity-not-found",
          "title": "entity-not-found",
          "status": 404,
          "detail": "The requested entity does not exist. There is no entity matches 'Id:1234'.",
          "instance": "/api/items/1234",
          "version": "1.0.0",
          "traceId": "00-b4ed7c81dfa056409712080bf8326114-dc891f34aca58540-00",
          "timestamp": "2020-12-05T04:30:30.4789974Z",
          "params": {
             "entity": "User",
             "key": "example@example.com"
          }
        }
        ```

        </details>

        <details>
        <summary>422 Unprocessable Entity</summary>

        - Means the server understands the content type of the request entity (parseable) but have a semantic errors (some parameters were missing or otherwise invalid).
        - [rfc2518#section-10.3](https://tools.ietf.org/html/rfc2518#section-10.3)
        - Response body example:

        ```json
        {
          "type": "/errors/validation-problem",
          "title": "validation-problem",
          "status": 422,
          "detail": "",
          "instance": "/register",
          "version": "1.0.0",
          "timestamp": "2020-12-05T04:04:24.5016592Z",
          "traceId": "00-5ccef78897b44c4e83cf650d878c4873-905457454dd84943-00",
          "errors": {
            "password": [
              {
                "code": "g:minimum_length",
                "description": "The length of 'Password' must be at least 8 characters. You entered 1 characters.",
                "params": {
                  "minLength": 8,
                  "maxLength": -1,
                  "totalLength": 1,
                  "propertyName": "Password",
                  "propertyValue": "1",
                  "propertyPath": "Password"
                }
              }
            ]
          }
        }
        ```

        </details>

        <details>
        <summary>409 Conflict</summary>

        - Response body example:

        ```json
        {
          "type": "/errors/conflict-request",
          "title": "conflict-request",
          "status": 409,
          "detail": "Conflict request.",
          "instance": "/items",
          "version": "1.0.0",
          "timestamp": "2020-12-05T04:04:24.5016592Z",
          "traceId": "00-5ccef78897b44c4e83cf650d878c4873-905457454dd84943-00",
          "errors": [
            {
              "code": "auth:sign_in_requires_two_factor",
              "description": "Require two factor authentication."
            }
          ]
        }
        ```

        </details>

        <details>
        <summary>400 Bad Request</summary>

        - The request could not be understood by the server (not parsable) due to malformed syntax.
        - [rfc2068#section-10.4.1](https://tools.ietf.org/html/rfc2068#section-10.4.1)
        - Response body example:

        ```json
        {
          "type": "/errors/bad-request",
          "title": "bad-request",
          "status": 400,
          "detail": "The request body is empty or could not be understood by the server due to malformed syntax.",
          "instance": "/api/items",
          "version": "1.0.0",
          "timestamp": "2020-12-05T04:34:18.4905013Z",
          "traceId": "00-cab22f03e1c1ee48b7282e9d89472179-3657eeaaf6f37142-00"
        }
        ```

        </details>

        <details>
        <summary>401 Unauthorized</summary>

        - Access token is messing or invalid.
        - [rfc2068#section-10.4.2](https://tools.ietf.org/html/rfc2068#section-10.4.2)
        - No body content

        </details>

        <details>
        <summary>403 Forbidden</summary>

        - The server understood the request, but is refusing to fulfill it.
        - [rfc2068#section-10.4.4](https://tools.ietf.org/html/rfc2068#section-10.4.4)

        </details>

        <details>
        <summary>405 Method Not Allowed</summary>

        - A request was made of a resource using a request method not supported by that resource; for example, using GET on a form which requires data to be presented via POST, or using PUT on a read-only resource.
        - [rfc2068#section-10.4.6](https://tools.ietf.org/html/rfc2068#section-10.4.6)
        - No body content

        </details>

        ## Server Errors `5xx`

        <details>
        <summary>500 Internal Error</summary>

        - The request could not be understood by the server (not parsable) due to malformed syntax.
        - [rfc2068#section-10.4.1](https://tools.ietf.org/html/rfc2068#section-10.4.1)
        - Response body example:

        ```json
        {
          "type": "/errors/unhandled-exception",
          "title": "unhandled-exception",
          "status": 500,
          "detail": "An error occurred while processing your request.",
          "instance": "/api/exception",
          "version": "1.0.0",
          "timestamp": "2020-12-05T01:02:47.7997190Z",
          "traceId": "00-49ac3629883b0a4fa1b7b29dd9d9cb16-5f89fb22bb006b45-00"
        }
        ```

        </details>
        """;
}
