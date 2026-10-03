// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Generated assembly metadata naming one request a responder in the assembly answers and the response type it answers
/// with.
/// </summary>
/// <remarks>
/// The Messaging source generator emits one per <see cref="IRespond{TRequest, TResponse}"/> it registers. The generator
/// in a referencing project reads them to check each <c>IRequestClient.RequestAsync&lt;TRequest, TResponse&gt;</c> call
/// against the responders it can see, which the responder classes alone do not allow: the compiler does not read a
/// referenced assembly's classes cheaply enough to scan them on every edit. Do not apply it by hand.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ResponderMetadataAttribute(Type requestType, Type responseType) : Attribute
{
    /// <summary>The request type the responder answers.</summary>
    public Type RequestType { get; } = Argument.IsNotNull(requestType);

    /// <summary>The response type the responder answers it with.</summary>
    public Type ResponseType { get; } = Argument.IsNotNull(responseType);
}
