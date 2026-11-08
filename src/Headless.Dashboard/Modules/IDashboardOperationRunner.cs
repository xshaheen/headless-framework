// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: runs a command over a filter instead of a list of ids, as an Operation the browser drives one
/// chunk at a time.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard stores no Operation. <see cref="PrepareAsync"/> freezes the scope: the shell signs the filter, the
/// returned high-water mark, and the count into a time-limited token. Each chunk call then brings the token and the
/// previous cursor back, possibly to another node, and the shell calls <see cref="RunChunkAsync"/> with them. Chunks
/// of one Operation can run on several nodes, an operator can stop it between any two chunks, and the same Operation
/// can be confirmed and run again later. The store must therefore make every chunk safe on its own.
/// </para>
/// <para>What the module's store must provide:</para>
/// <list type="bullet">
///   <item><description>
///   <b>A time each row entered its current state, stamped by the store.</b> Write it in the same statement that
///   changes the row's state, from the store's own clock, never a time an application node sampled; other writes to
///   the row, such as a retry count or progress, must leave it alone. Neither a creation time nor a last-update time
///   can stand in: the first ignores a row that left the state and came back, and the second moves on writes that do
///   not change the state.
///   </description></item>
///   <item><description>
///   <b>A high-water mark from that time.</b> <see cref="PrepareAsync"/> returns the latest entered-state time among
///   the rows in scope, read from the store, encoded in the module's own format. A chunk acts only on rows whose
///   entered-state time is at or before the mark, so a row that enters the state while the Operation runs, such as a
///   run that fails during a requeue-all-failed, stays out of it. An id mark cannot do this, because a row that fails
///   again keeps its id.
///   </description></item>
///   <item><description>
///   <b>A conditional update.</b> Each chunk applies the command with the scope's predicate inside the update itself:
///   the row still matches the filter, is still in the state the command acts on, and entered it at or before the
///   mark. A row an earlier chunk already changed has left the state and is not touched again, by this run, by a
///   concurrent chunk on another node, or by a later run of the same Operation. Report such a row as
///   <see cref="DashboardRowOutcome.Skipped"/>, or leave it out.
///   </description></item>
///   <item><description>
///   <b>An index for the walk.</b> Walk the scope in a stable order, such as the entered-state time and then the id,
///   with an index that serves both the filter's state predicate and that order, so each chunk is a range seek from
///   the cursor and no chunk rescans what earlier chunks passed.
///   </description></item>
/// </list>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>, beside <see cref="IDashboardCommandSource"/>
/// for the same commands. New members arrive as default interface members; existing members never gain a parameter,
/// and the request records grow instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardOperationRunner
{
    /// <summary>
    /// Freezes an Operation's scope: counts the rows the command would act on, returns a few as a sample, and reads
    /// the high-water mark from the store.
    /// </summary>
    /// <param name="request">The command, the filter, the count limit, and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends.</param>
    /// <returns>The exact count, up to one past the limit, a sample, and the mark.</returns>
    /// <exception cref="ArgumentException">
    /// The filter is not one the module accepts for the command. The shell answers <c>400</c> with the message.
    /// </exception>
    Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Runs the next chunk of an Operation: acts on up to <see cref="DashboardOperationChunkRequest.ChunkSize"/> rows
    /// after the cursor that are still in scope.
    /// </summary>
    /// <param name="request">The frozen scope, the cursor, and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends.</param>
    /// <returns>The per-row outcomes and the cursor to resume from; a <see langword="null"/> cursor ends the run.</returns>
    /// <exception cref="ArgumentException">
    /// The filter, the mark, or the cursor is not one the module returned, checked before any row changes. The shell
    /// answers <c>400</c> with the message.
    /// </exception>
    Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>A request to freeze an Operation's scope.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardOperationScopeRequest
{
    /// <summary>Gets the command's id.</summary>
    public required string CommandId { get; init; }

    /// <summary>Gets the filter in the module's own shape, as its list view sent it.</summary>
    public required JsonElement Filter { get; init; }

    /// <summary>
    /// Gets the largest count the shell accepts. Count no further than one past it: the shell refuses a larger scope
    /// and needs no exact figure for it.
    /// </summary>
    public required int CountLimit { get; init; }

    /// <summary>Gets how many sample rows to return.</summary>
    public required int SampleSize { get; init; }

    /// <summary>Gets the signed-in user who asks.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>An Operation's frozen scope, as the shell signs it into the Operation's token.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardOperationScope
{
    /// <summary>Gets the number of rows in scope, up to one past the request's limit.</summary>
    public required long Count { get; init; }

    /// <summary>Gets a few rows in scope, shown in the confirm dialog.</summary>
    public required IReadOnlyList<DashboardObject> Sample { get; init; }

    /// <summary>
    /// Gets the high-water mark in the module's own format: the latest time, by the store's clock, at which a row in
    /// scope entered the state the command acts on.
    /// </summary>
    public required string HighWaterMark { get; init; }
}

/// <summary>A request to run one chunk of an Operation.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardOperationChunkRequest
{
    /// <summary>Gets the command's id.</summary>
    public required string CommandId { get; init; }

    /// <summary>Gets the filter frozen at confirm time.</summary>
    public required JsonElement Filter { get; init; }

    /// <summary>Gets the high-water mark frozen at confirm time.</summary>
    public required string HighWaterMark { get; init; }

    /// <summary>Gets the cursor the previous chunk returned, or <see langword="null"/> for the first chunk.</summary>
    public string? Cursor { get; init; }

    /// <summary>Gets the most rows this chunk may act on.</summary>
    public required int ChunkSize { get; init; }

    /// <summary>Gets the command's validated arguments; empty when it takes none.</summary>
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>Gets the operator's reason, or <see langword="null"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the signed-in user who runs the Operation.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>The result of one Operation chunk.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardOperationChunk
{
    /// <summary>Gets one result per row the chunk acted on or skipped.</summary>
    public required IReadOnlyList<DashboardRowResult> Rows { get; init; }

    /// <summary>
    /// Gets the cursor to resume from, in the module's own format, or <see langword="null"/> when no row is left in
    /// scope.
    /// </summary>
    public string? NextCursor { get; init; }
}
