# Sandboxes

Local hosts that run framework UI so a person or an agent can drive it. They are test fixtures, not samples: the
wiring favors reaching every state quickly over showing production setup. For consumer examples, see `demo/`.

## Dashboard sandbox

`Headless.Dashboards.Sandbox` serves the Jobs and Messaging dashboards on loopback. The project CLI owns it:

```sh
make up                                   # build and start; STORE=postgres adds sandboxes/compose.yaml
make ready                                # wait until both dashboards answer
make seed SCENARIO=progress-running       # create a dashboard state; SCENARIO=all creates every one
make db-q Q="select status, progress_percent from headless.time_jobs"   # STORE=postgres only; read-only
make logs                                 # FOLLOW=1 to keep following
make down                                 # stop what `up` started
```

| Dashboard | URL | Auth |
| --- | --- | --- |
| Jobs | `http://127.0.0.1:5300/jobs/dashboard` | none, so live SignalR updates work |
| Messaging | `http://127.0.0.1:5300/messaging` | Basic: user `sandbox`, password `Sandbox__MessagingDashboardPassword` in `.context/cli/sandbox.env`, generated on the first `make up` |

Messaging uses Basic auth because inbox and scheduled operations refuse an anonymous operator.

`GET /sandbox/scenarios` lists the scenarios and what each creates; `POST /sandbox/scenarios/{name}` runs one. Each
call creates new rows, so running a scenario again is safe. Jobs run with short cadences (progress writes every
500 ms, a 30 s lease, a 5 s fallback poll) so states appear within seconds.

`STORE=memory` (the default) forgets everything when the process stops. `STORE=postgres` keeps Jobs state in a
per-checkout PostgreSQL volume, built with `EnsureCreated` instead of migrations; after a Jobs schema change, run
`make sandbox-reset CONFIRM=1` to drop it. Messaging always runs in memory.
