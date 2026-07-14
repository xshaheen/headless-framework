// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// One semaphore in a composite acquisition: the resource it guards and the capacity it is created with.
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="MaxCount"/> is a property of the <em>semaphore</em>, not of the acquisition — it is the capacity
/// <see cref="IDistributedSemaphoreProvider.CreateSemaphore"/> binds at construction, and every caller naming the same
/// resource must agree on it. Two requests naming one resource with different capacities describe two different
/// semaphores that cannot both exist, so composite acquisition rejects that pair as a caller error before touching the
/// provider. Identical duplicates collapse to a single child.
/// </para>
/// <para>
/// There is deliberately no permit count here. A composite takes exactly <em>one</em> slot of each named semaphore.
/// Taking N permits of a single semaphore all-or-nothing is a real problem, but a composite structurally cannot solve
/// it: composites avoid deadlock by imposing a global order across <em>distinct</em> resources, and contention for N
/// permits of one semaphore has no ordering to impose — two callers hash to the same key, each take one permit, and
/// stall holding partial ownership. That needs atomic multi-permit acquisition inside the storage backend; a permit
/// count on this descriptor would advertise atomicity it cannot deliver.
/// </para>
/// </remarks>
/// <param name="Resource">The resource the semaphore guards. Must be non-null and non-whitespace.</param>
/// <param name="MaxCount">The maximum number of concurrent holders of <paramref name="Resource"/>. Must be at least 1.</param>
[PublicAPI]
public record DistributedSemaphoreRequest(string Resource, int MaxCount);
