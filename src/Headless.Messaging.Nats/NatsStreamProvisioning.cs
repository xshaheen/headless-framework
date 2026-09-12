// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Nats;

/// <summary>
/// How consumer clients provision the JetStream streams their subjects live on. A dedicated enum
/// (rather than a <see langword="bool"/> flag) so the three states stay distinguishable: a stream that is
/// missing, a stream that exists and agrees, and a stream that exists and disagrees are three different
/// situations, and the middle one is the common case a boolean cannot express.
/// </summary>
/// <remarks>
/// Every mode except <see cref="Disabled"/> creates a stream that does not exist yet, so first-run and
/// ephemeral-environment startup behave identically under <see cref="Verify"/> and <see cref="Reconcile"/>.
/// The modes differ only in what happens to a stream that is <em>already there</em>.
/// </remarks>
[PublicAPI]
public enum NatsStreamProvisioning
{
    /// <summary>
    /// Default. Creates the stream when it is absent. When the stream already exists, compares it against the
    /// configuration this application asks for and throws with the divergent fields instead of writing to it.
    /// Safe against a stream provisioned out-of-band (NATS CLI, Terraform, a Kubernetes operator): the
    /// application never silently rewrites an operator's storage class, replica count, or limits.
    /// </summary>
    Verify = 0,

    /// <summary>
    /// Creates the stream when it is absent, and updates it to match this application's configuration when it
    /// already exists. This is the pre-existing behavior. Choose it when the application owns stream topology,
    /// or when several consumer groups share one stream and each contributes its own subjects — under
    /// <see cref="Verify"/> a group whose subject the stream does not yet carry fails startup instead.
    /// </summary>
    /// <remarks>
    /// JetStream refuses some configuration changes on a live stream — storage type is the clearest case.
    /// This mode reports such a divergence with a migration remedy rather than attempting an update the
    /// server would reject.
    /// </remarks>
    Reconcile = 1,

    /// <summary>
    /// Performs no stream creation and no modification. Streams and subjects are managed entirely outside the
    /// application, and a missing or mismatched stream is not this application's concern at startup.
    /// </summary>
    Disabled = 2,
}
