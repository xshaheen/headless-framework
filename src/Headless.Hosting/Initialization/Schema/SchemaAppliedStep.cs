// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Initialization.Schema;

/// <summary>One step a run applied.</summary>
/// <param name="Schema">The schema the step was applied in.</param>
/// <param name="Feature">The contributing feature.</param>
/// <param name="Version">The step's version.</param>
[PublicAPI]
public sealed record SchemaAppliedStep(string Schema, string Feature, string Version);
