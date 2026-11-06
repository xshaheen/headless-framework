# Sandboxes

Local hosts that run framework UI so a person or an agent can drive it. They are test fixtures, not samples: the
wiring favors reaching every state quickly over showing production setup. For consumer examples, see `demo/`.

## Dashboard sandbox

`Headless.Dashboards.Sandbox` serves the Headless dashboard, with its Jobs module, and the Messaging
dashboard on loopback. The project CLI owns it:

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
| Headless, with the Jobs module | `http://127.0.0.1:5300/headless/` (Jobs at `/headless/jobs`) | Host: sign in at `/sandbox/headless-sign-in` (below) |
| Messaging | `http://127.0.0.1:5300/messaging/dashboard` | Basic: user `sandbox`, password `Sandbox__MessagingDashboardPassword` in `sandbox.env` under the checkout's `.local/cli/` folder, generated on the first `make up` |

Messaging uses Basic auth because inbox and scheduled operations refuse an anonymous operator.

The Headless dashboard always runs in host mode, through a fake host scheme that stands in for an application that
signs its users in with a bearer token and has no sign-in redirect. Open
`http://127.0.0.1:5300/sandbox/headless-sign-in` and enter the Messaging password from `sandbox.env` to sign in as
`sandbox-operator`, or `viewer-<password>` to sign in as `sandbox-viewer`. The page posts the token as
`Authorization: Bearer ...` with `X-Requested-With: XMLHttpRequest` to `/headless/api/auth/session`, which sets the
`__Host-headless-dashboard-headless-dashboards-sandbox` session cookie, then opens `/headless/`. The viewer passes the
`SandboxViewers` view policy only, so every module shows its actions disabled and the server refuses them with `403`;
the operator also passes the `SandboxOperators` operate policy. `Sandbox__HostSecret` replaces the password as the
token secret. `GET /` reports the dashboards' paths and auth modes.

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
| `messages`, `scheduled-message` | Ten published messages, two failing, and one delayed 10 minutes | The Messaging dashboard's received, failed, and scheduled pages |

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

The sandbox consumer's failures are deliberate, so a `FailOn<InvalidOperationException>()` default failure policy makes them terminal at once. Inbox operations such as Force reprocess act only on terminal generations.
