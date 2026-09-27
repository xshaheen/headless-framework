// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>The PHC id of the algorithm the setup builder selected for new hashes.</summary>
/// <param name="AlgorithmId">The selected algorithm's PHC identifier.</param>
internal sealed record SecretHasherAlgorithmSelection(string AlgorithmId);
