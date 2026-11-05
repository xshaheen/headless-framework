# Sandboxes

Local hosts that run framework UI so a person or an agent can drive it. They are test fixtures, not samples: the
wiring favors reaching every state quickly over showing production setup. For consumer examples, see `demo/`.

## Dashboard sandbox

`Headless.Dashboards.Sandbox` serves the Headless dashboard and the Jobs and Messaging dashboards on loopback. The project CLI owns it:

```sh
make up                                   # build and start; STORE=postgres adds sandboxes/compose.yaml
make ready                                # wait until every dashboard answers
make seed SCENARIO=progress-running       # create a dashboard state; SCENARIO=all creates every one
make db-q Q="select status, progress_percent from headless.time_jobs"   # STORE=postgres only; read-only
make logs                                 # FOLLOW=1 to keep following
make down                                 # stop what `up` started
```

| Dashboard | URL | Auth |
| --- | --- | --- |
| Headless | `http://127.0.0.1:5300/headless/` | Host: sign in at `/sandbox/headless-sign-in` (below) |
| Jobs | `http://127.0.0.1:5300/jobs/dashboard` | none by default; `Sandbox__JobsDashboardAuth` picks another mode |
| Messaging | `http://127.0.0.1:5300/messaging/dashboard` | Basic: user `sandbox`, password `Sandbox__MessagingDashboardPassword` in `sandbox.env` under the checkout's `.local/cli/` folder, generated on the first `make up` |

Messaging uses Basic auth because inbox and scheduled operations refuse an anonymous operator.

The Headless dashboard always runs in host mode, through the sandbox's fake host scheme described below, and serves
its diagnostic module, which registers only in Development (`make up` sets it). Open
`http://127.0.0.1:5300/sandbox/headless-sign-in` and enter the Messaging password from `sandbox.env` to sign in as
`sandbox-operator`, or `viewer-<password>` to sign in as `sandbox-viewer`, who may view but not run commands. The page
exchanges the token for the `__Host-headless-dashboard-headless-dashboards-sandbox` session cookie, then opens
`/headless/`. The diagnostic module starts empty: `make seed SCENARIO=diagnostic-runs` adds six runs in every state,
two of them failed, so Needs you, Overview, the runs view, and Requeue have something to show.

Each dashboard has its own auth, so the modes can differ. To walk the Jobs sign-in, session, or live-update
journeys under auth, set these before `make up` (`make down` first if it is running):

| Variable | Values |
| --- | --- |
| `Sandbox__JobsDashboardAuth` | `None` (default), `Basic`, `ApiKey`, or `Host` |
| `Sandbox__JobsDashboardSecret` | The password, API key, or host token secret. Defaults to the Messaging password in `sandbox.env`. |

Basic uses the user `sandbox`. `Host` stands in for an application that signs its users in with a bearer token, through a fake scheme that has no sign-in redirect. Open `http://127.0.0.1:5300/sandbox/sign-in` and enter `<secret>` to sign in as `sandbox-operator`, or `viewer-<secret>` to sign in as `sandbox-viewer`. The page posts the token as `Authorization: Bearer ...` with `X-Requested-With: XMLHttpRequest` to `/jobs/dashboard/api/auth/session`, which sets the dashboard's session cookie, then opens the dashboard. The viewer passes the `SandboxViewers` view policy only, so the dashboard hides write actions and refuses them with `403`; the operator also passes the `SandboxOperators` operate policy. The cookie is `Secure`, and browsers and curl accept that on loopback. The session ends after 30 minutes without a dashboard request or 8 hours after sign-in, and the dashboard's sign-out ends it at once. `GET /` reports the modes as `headlessAuth`, `jobsAuth`, and `messagingAuth`.

```sh
Sandbox__JobsDashboardAuth=Host make up
# Without a browser: exchange the token, then call the API with the session cookie alone.
curl -si -c jar -X POST -H "Authorization: Bearer $SECRET" -H 'X-Requested-With: XMLHttpRequest' \
  http://127.0.0.1:5300/jobs/dashboard/api/auth/session
curl -s -b jar http://127.0.0.1:5300/jobs/dashboard/api/auth/info
```

`GET /sandbox/scenarios` lists the scenarios and what each creates; `POST /sandbox/scenarios/{name}` runs one. Each
call creates new rows, so running a scenario again is safe. Jobs run with short cadences (progress writes every
500 ms, a 30 s lease, a 5 s fallback poll) so states appear within seconds.

`STORE=memory` (the default) forgets everything when the process stops. `STORE=postgres` keeps Jobs state in a
per-checkout PostgreSQL volume, built with `EnsureCreated` instead of migrations; after a Jobs schema change, run
`make sandbox-reset CONFIRM=1` to drop it. With `STORE=postgres` Messaging stores its rows there too; its transport is always in memory.

The sandbox consumer's failures are deliberate, so a `FailOn<InvalidOperationException>()` default failure policy makes them terminal at once. Inbox operations such as Force reprocess act only on terminal generations.
