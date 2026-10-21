// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.IO;

/// <summary>
/// A file a caller supplied, described by the facts a rule or a store needs, independent of the transport that carried
/// it, so a command or request handled in process (for example through the mediator) accepts a file without depending
/// on ASP.NET Core.
/// </summary>
/// <remarks>
/// <para>
/// The members mirror ASP.NET Core's <c>IFormFile</c>, which adapts to this interface through
/// <c>IFormFile.ToUploadedFile()</c> in <c>Headless.Api</c>. <see cref="FileName"/> and <see cref="ContentType"/> come
/// from the sender and are untrusted: check the content's binary signature before relying on either.
/// </para>
/// <para>
/// An instance wraps live content, so it is not a message payload: a message that crosses a broker carries a
/// reference to stored content (a blob location, for example) instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUploadedFile
{
    /// <summary>The file name the sender supplied, untrusted and possibly empty.</summary>
    string FileName { get; }

    /// <summary>The media type the sender declared, untrusted and possibly empty.</summary>
    string ContentType { get; }

    /// <summary>The length of the content in bytes.</summary>
    long Length { get; }

    /// <summary>
    /// Opens a new read stream positioned at the start of the content. Each call returns an independent stream that
    /// the caller disposes, so a validation rule and the code that stores the file can each read the whole content.
    /// </summary>
    /// <returns>
    /// A readable, seekable stream over the content. Seeking is part of the contract because signature inspection
    /// reads a format's header and, for container formats, its directory at the end; buffer forward-only content
    /// (a network download, for example) before exposing it through this interface.
    /// </returns>
    Stream OpenReadStream();
}
