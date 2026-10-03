// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

/// <summary>One field (or subject) on which the desired stream configuration and the live stream disagree.</summary>
/// <param name="Field">The <c>StreamConfig</c> property name, or <c>Subjects</c> for uncovered subjects.</param>
/// <param name="Desired">The value this application asks for, rendered for display.</param>
/// <param name="Actual">The value the live stream carries, rendered for display.</param>
/// <param name="IsImmutable">
/// <see langword="true"/> when JetStream refuses to change this field on an existing stream, so no
/// provisioning mode can converge it and the stream must be recreated or migrated out of band.
/// </param>
internal sealed record StreamDivergence(string Field, string? Desired, string? Actual, bool IsImmutable);
