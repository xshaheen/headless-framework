// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork;

/// <summary>
/// The participant configurations already proven to reach a unit's database, kept on the unit so every enlisted
/// call after the first skips the connection build and data-source comparison.
/// </summary>
/// <remarks>
/// Only matches are kept: a mismatch refuses the call, so there is no second call to save. The candidate connection
/// is part of the key because a nested view shares the unit's state, and a verdict holds only for the connection
/// it was proven against.
/// </remarks>
internal sealed class RelationalDatabaseVerdicts
{
    private readonly Lock _gate = new();
    private readonly List<(object Configuration, DbConnection Candidate)> _matches = [];

    public bool Contains(object configuration, DbConnection candidate)
    {
        lock (_gate)
        {
            foreach (var (knownConfiguration, knownCandidate) in _matches)
            {
                if (ReferenceEquals(knownConfiguration, configuration) && ReferenceEquals(knownCandidate, candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Add(object configuration, DbConnection candidate)
    {
        lock (_gate)
        {
            _matches.Add((configuration, candidate));
        }
    }
}
