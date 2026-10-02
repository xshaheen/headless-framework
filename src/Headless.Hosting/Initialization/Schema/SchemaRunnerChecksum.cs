// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// The checksum contract between the schema runner's history rows and any deploy-time tooling that writes them:
/// SHA-256 over the step's SQL with line endings normalized to Unix, so a file checked out with CRLF keeps its
/// checksum. A description change alone does not invalidate it.
/// </summary>
[PublicAPI]
public static class SchemaRunnerChecksum
{
    /// <summary>Returns the checksum the history table records for <paramref name="step"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is <see langword="null"/>.</exception>
    public static string ForStep(SchemaStep step)
    {
        Argument.IsNotNull(step);

        var normalized = step.Sql.Replace("\r\n", "\n", StringComparison.Ordinal);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
