# Sandboxes

Local hosts that run framework UI so a person or an agent can drive it. They are test fixtures, not samples: the
wiring favors reaching every state quickly over showing production setup. For consumer examples, see `demo/`.

## Dashboard sandbox

`Headless.Dashboards.Sandbox` serves the Headless dashboard, with its Jobs, Messaging, Rate limits, Idempotency, Cluster, and Leases modules, on loopback. The project CLI owns it:

```sh
make up                                   # build and start; STORE=postgres adds sandboxes/compose.yaml
make ready                                # wait until every dashboard answers
make seed SCENARIO=progress-running       # create a dashboard state; SCENARIO=all creates every one
make db-q Q="select status, progress_percent from headless.time_jobs"   # STORE=postgres only; read-only
make logs                                 # FOLLOW=1 to keep following
make down                                 # stop what `up` started
```

Another worktree may hold port 5300: `make up SANDBOX_PORT=5310` moves the sandbox, and `ready`, `seed`, `status`,
and `down` take the same override.

| Dashboard | URL | Auth |
| --- | --- | --- |
| Headless, with the Jobs, Messaging, Rate limits, Idempotency, Cluster, and Leases modules | `http://127.0.0.1:5300/headless/` (Jobs at `/headless/jobs`, Messaging at `/headless/messaging`, Rate limits at `/headless/rate-limiting`, Idempotency at `/headless/idempotency`, Cluster at `/headless/cluster`, Leases at `/headless/leases`) | `admin` / `admin` on the dashboard's sign-in page; `make up AUTH=host` for the host sign-in (below) |

Every module sits behind the same sign-in, so inbox and scheduled operations record the signed-in operator's name.
`GET /sandbox/usable` answers once the Messaging store does, with its published and received counts.

By default the dashboard signs in with its own Basic mode and the fixed credential `admin` / `admin`, as one operator who
may run every action. The fixed credential is safe only because the sandbox binds to loopback and holds no real data.

`make up AUTH=host` instead runs the dashboard in host mode, through a fake host scheme that stands in for an application that
signs its users in with a bearer token and has no sign-in redirect. Open
`http://127.0.0.1:5300/sandbox/headless-sign-in` and enter the secret `Sandbox__MessagingDashboardPassword` from `sandbox.env` under the checkout's `.local/cli/` folder
(generated on the first `make up`) to sign in as
`sandbox-operator`, or `viewer-<password>` to sign in as `sandbox-viewer`. The page posts the token as
`Authorization: Bearer ...` with `X-Requested-With: XMLHttpRequest` to `/headless/api/auth/session`, which sets the
`__Host-headless-dashboard-headless-dashboards-sandbox` session cookie, then opens `/headless/`. The viewer passes the
`SandboxViewers` view policy only, so every module shows its actions disabled and the server refuses them with `403`;
the operator also passes the `SandboxOperators` operate policy. `Sandbox__HostSecret` replaces that secret as the
token secret. `GET /` reports the dashboard's path and auth mode.

```sh
# Without a browser: exchange the token, then call the Jobs module's API with the session cookie alone.
curl -si -c jar -X POST -H "Authorization: Bearer $SECRET" -H 'X-Requested-With: XMLHttpRequest' \
  http://127.0.0.1:5300/headless/api/auth/session
curl -s -b jar 'http://127.0.0.1:5300/headless/api/jobs/runs?status=Failed'
```

### Scenarios

`GET /sandbox/scenarios` lists the scenarios and what each creates; `POST /sandbox/scenarios/{name}` or
`make seed SCENARIO=<name>` runs one. Each call creates new rows, so running a scenario again is safe. Jobs run with
short cadences (progress writes every 500 ms, a 30 s lease, a 5 s fallback poll) so states appear within seconds.

| Scenario | Creates | Journey it opens |
| --- | --- | --- |
| `progress-running`, `progress-quick`, `progress-failing`, `quiet` | Time jobs that report progress and finish or fail, and one that reports none | Live progress on the Runs view and the run page |
| `failing` | A time job that fails at once, with no retries | Requeue of one failed run |
| `failures-by-id` | Five `sandbox.order-sync` runs that fail with one cause and a different order id in each message | One Needs you item for the five failures, and Requeue over the group as an Operation |
| `chain-failed-middle` | A three-step chain whose second step fails: the first succeeds and the third never runs | Chain steps on the run page |
| `cron-progress` | A cron job every 30 s whose occurrences report progress | Occurrences on the cron job page |
| `cron-no-overlap-failing` | A cron job every 10 s under the skip overlap policy whose occurrences run 15 s, then fail | Skipped and failed occurrences, and Run now refused while one runs |
| `cron-paused` | A cron job every 5 minutes, paused once created | Resume, and Run now refused while paused |
| `cancel-stubborn` | A time job that reports progress for 5 minutes and ignores its cancellation token | Cancel on a running job that never stops: the run reads `Cancelling…` until it ends |
| `cancelled-before-start` | A time job scheduled an hour ahead and cancelled at once | A Cancelled run that never started |
| `failing-timeout` | A `sandbox.times-out` job that fails at once with a `TimeoutException`, with no retries | A second exception type on the Failed list, beside `InvalidOperationException` |
| `messages`, `scheduled-message` | Ten published messages, two failing, and one delayed 10 minutes | The Messaging module's Published, Received, and Scheduled views |
| `message-failures-by-cause` | Seven received messages that fail with two causes and a different order id in each message | Two Needs you items, one per cause, and an Operation over one group |
| `message-contracts` | One consumed message of each declared contract: two on the Bus lane, two on the Queue lane | The Contracts and Consumers views, and the lane filter on the lists |
| `message-retrying` | Three Queue-lane messages that fail in `sandbox.shipments` and wait for a retry: the first after a minute, then at doubling delays for over an hour | The Retrying list and count, and a retry count that moves |
| `message-held` | One consumed message that the operator `sandbox-seed` holds | The Held list and count, and Release |
| `message-operator-log` | One failed message re-executed, and one consumed message held then released, all by `sandbox-seed` | The Operator log with three operation types, and a received message with an original and a replay generation |
| `message-circuit-open` | Two messages that time out in `sandbox.payments`, which opens its circuit | An open circuit on the Circuits view, and Reset |
| `message-backlog` | Five Queue-lane messages that `sandbox.stock-counts` takes 45 s to 165 s to handle | A received backlog that drains one row every 30 s, for under three minutes |
| `message-rate-limited` | Six Queue-lane messages for `sandbox.carrier-quotes`, which handles one every 30 s | Received rows that wait for a rate-limit permit and drain one every 30 s. Run it with `message-retrying`: the shipments' retries still run when they are due |
| `message-publish-failed` | One message published with a delivery deadline that has already passed when it is sent | A published row that failed for good, with Re-send and Delete |
| `attempts` | Two new `sign-in.password` subjects: one locked out at 5 of 5, one at 2 of 5; the call returns each subject and its window end | Look up a locked-out subject on Rate limits, and Reset its window |
| `idempotency` | Four new idempotency keys: one completed under tenant `acme` with an `orders.create/v1` result, one in flight with the recovery point `payment-authorized`, one that stalls after its 1 s lease, and one in flight past its 1 s retention; the call returns each key, its tenant, its state, and when it reaches it | Look each key up on Idempotency: Completed with Tenant `acme` (not found with Tenant blank), In flight, Stalled, and In flight kept past retention. Records follow the sandbox store: in memory by default, the PostgreSQL table under `STORE=postgres` |
| `health-healthy`, `health-degraded`, `health-unhealthy` | The `sandbox-dependency` health check reports that status from its next probe on | The Cluster module's Health view: Probe now and Probe all, Needs you health-check items, and the Degraded and Unhealthy counts |
| `cluster-peers` | Three peer incarnations in the membership store (`STORE=postgres` only; the memory store answers `409`): `orders-api-1` keeps beating, `orders-worker-1` beats once more and then stops, and `orders-worker-2` never beats again; `orders-worker-1` carries `app.version` `4.2.0` against `4.2.1` | The Cluster view with Alive, Suspected, and Dead nodes, the metadata disagreement, and a node that leaves the snapshot once the store prunes it |
| `leases` | Fenced leases in every state, with a per-call suffix on each resource: live leases in the kinds `ledger-export`, `invoice-batch`, and `search-reindex` across the host scope and the tenants `acme` and `globex` (one with 1,229 bytes of progress under `export-cursor/v1` and no holder), two expired, one settled, one released, one revoked, one taken over after expiry, and one abandoned by a sweep | The Leases module's Fenced leases view: the state, kind, tenant, and prefix filters, the counts, and a lease's panel with its holder, takeovers, and progress size and contract |
| `locks` | Four held distributed locks, with the same kind of suffix: three with a two-minute TTL that the sandbox renews while it runs, and one with no expiry | The Leases module's Distributed locks view: the time to live as read, `No expiry`, and the provider scope note |

The Cluster module shows the Health view on every store: the host registers the `sandbox-dependency` check before the dashboard, so `AddCluster()` registers the health check monitor, and the Jobs and Messaging checks appear beside it. Checks run only when an operator probes them. The Cluster view needs Coordination, which only `STORE=postgres` registers, in the cluster `orders` with role `api` and `app.version` `4.2.1`; on the memory store it explains that Coordination is not registered. The `cluster-peers` peers own no work, so their eventual dead and left events reach the Jobs and Messaging dead-owner recovery harmlessly. Their beats stop with the host.

The Leases module reads in-memory Fencing and the in-memory lock provider on every store, so its views work with `STORE=memory`. The in-memory lock provider lists only this host's handles, which is the case the Distributed locks view's scope note describes. A sandbox task holds and renews the `locks` scenario's locks and stops with the host; both scenarios' rows live in memory, so a restart clears them.

The host also defines `sandbox.nightly-report` in code with a daily cron schedule, so the Cron jobs view always lists
a cron job defined in code, whose schedule stays read-only.

The sandbox runs one node. A run on a second node needs `STORE=postgres` and a second process: start
`dotnet sandboxes/Headless.Dashboards.Sandbox/bin/Release/net10.0/Headless.Dashboards.Sandbox.dll` by hand with
`ASPNETCORE_URLS` on another port, `ASPNETCORE_ENVIRONMENT=Development`, `Sandbox__Store=Postgres`, and the same
`Sandbox__PostgresConnectionString` and `Sandbox__MessagingDashboardPassword` that `make up` uses. `make down` does not
stop it.

`STORE=memory` (the default) forgets everything when the process stops. `STORE=postgres` keeps Jobs state in a
per-checkout PostgreSQL volume, built with `EnsureCreated` instead of migrations; after a Jobs schema change, run
`make sandbox-reset CONFIRM=1` to drop it. With `STORE=postgres` Messaging stores its rows there too; its transport is always in memory.

The sandbox consumers' failures are deliberate, so a `FailOn<InvalidOperationException>()` default failure policy with no retries makes them terminal at once. Inbox operations such as Force reprocess act only on terminal generations. Four consumers differ, each for one state:

| Consumer | Lane | Differs by | State it holds |
| --- | --- | --- | --- |
| `sandbox.shipments` | Queue | A failure policy of eight delayed retries, from 1 minute up to 15 | Failed rows with a retry pending |
| `sandbox.payments` | Bus | A circuit breaker that opens after two timeouts in a row, for 30 minutes | An open circuit. While it is open the consumer receives nothing, so a later message for it waits in the transport until Reset, or until the 30 minutes pass |
| `sandbox.stock-counts` | Queue | A handler that takes as long as the message asks, eight messages at a time | Received rows that wait to be handled |
| `sandbox.carrier-quotes` | Queue | A rate limit of one delivery every 30 s | Received rows that wait for a permit. Its message has no declared contract, so the Contracts view does not list it |

The host declares a contract for four of its messages under the message's type name, so the Contracts view lists them. The scenarios act on received rows as the operator `sandbox-seed`, through the store's inbox operations, the ones the dashboard's commands call.

The in-memory transport never fails a send, so `message-publish-failed` ends its row another way: the message's delivery deadline (`DeliverWithin`) has passed before the stored row is dispatched, and the row becomes `Failed` without a send. Re-send on that row fails again for the same reason.
