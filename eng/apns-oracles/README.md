# APNs cross-library oracles

We have no Apple credentials in tests, so we check our APNs wire contract against three widely used APNs libraries. Each generator runs one library on the same scenarios and records the request that library sends. The .NET conformance test in `tests/Headless.PushNotifications.Apns.Tests.Unit/CrossLibrary/` compares our requests against those recordings.

| Generator | Library | Runtime | How the request is captured |
| --- | --- | --- | --- |
| `node/` | [`@parse/node-apn`](https://github.com/parse-community/node-apn) 8.1.0 | Node 22+ | A real `Provider` sends to a local HTTP/2 TLS server. |
| `java/` | [`com.eatthepath:pushy`](https://github.com/jchambers/pushy) 0.15.6 | Java 17 and Maven | A real `ApnsClient` sends to pushy's `MockApnsServer`. |
| `go/` | [`github.com/sideshow/apns2`](https://github.com/sideshow/apns2) v0.25.0 | Docker (`golang:1.27.0-alpine`, pinned by digest) | A real `apns2.Client` sends through an `http.RoundTripper` that records the request and returns 200. |

Every header and payload in a fixture comes from the library's own request-building code. None of it is written by hand.

## Regenerate

```bash
make apns-oracles
```

The command runs `run-all.sh`, which runs each generator in turn and overwrites `tests/Headless.PushNotifications.Apns.Tests.Unit/CrossLibrary/Fixtures/{node-apn,pushy,apns2}.json`. The output is deterministic, so a second run with the same pins produces byte-identical files. Commit the fixtures after you review the diff.

CI does not run the generators. It runs only the .NET comparison against the committed fixtures. Regenerate when you change `scenarios.json` or bump a library pin.

## Scenarios

`scenarios.json` is library-neutral input:

- `pushType`, `topic`: always set.
- `priority`, `expiration`, `collapseId`, `apnsId`: optional. When a field is absent, the library falls back to its own default, and the fixture records whatever that default sends.
- `aps`: Apple key names. Each generator passes every key to the library's own setter for that key. A generator never writes a key into the payload directly. Where a library places the value is part of what it records. For example, pushy moves a Live Activity sound into `aps.alert`.
- `custom`: keys that go beside `aps` at the payload root.
- `raw`: a complete payload, sent through the library's raw-payload path, with `aps` ignored.

When a library has no API for a scenario input, the fixture marks that scenario `"supported": false` and gives the reason. The .NET test skips that scenario for that library only.

## Fixture format

```json
{"library":"","version":"","generatedAt":"","scenarios":[{"id":"","supported":true,"unsupportedReason":null,"headers":{"apns-push-type":"","apns-topic":"","apns-priority":"","apns-expiration":null,"apns-collapse-id":null,"apns-id":null},"payload":{}}],"jwt":{"header":{},"claims":{}}}
```

- `generatedAt` is the fixed `generatedAt` value from `scenarios.json`, not the wall clock.
- A header the library did not send is `null`.
- `payload` is `null` when the request had no body. node-apn sends no body when the compiled payload is `{}`.
- Payload object keys are sorted so that diffs stay clean. Key order carries no meaning.
- `"<now+86400>"` in `apns-expiration`: pushy's default expiration is the send time plus one day. The generator checks that the header matches that default, then records the marker in its place.
- `"<number>"` for a JWT `iat` means the library stamps `iat` from the wall clock and has no API to fix it, so the fixture records only that `iat` is an integer. pushy's `AuthenticationToken` accepts an issue time, so its fixture uses the fixed `jwt.issuedAt`. The generator also checks that the token pushy sent on the wire has the same header and issuer.

## Test-only keys

`test-keys/` holds throwaway keys that were generated for these oracles. They are not Apple credentials.

- `AuthKey_TESTKEY123.TEST-ONLY.p8`: a P-256 provider-token key. Key id `TESTKEY123`, team id `TESTTEAM12`.
- `localhost.TEST-ONLY.crt` and `localhost.TEST-ONLY.key`: a self-signed TLS certificate for the local capture servers.

## Supply chain

- npm: `node/.npmrc` sets `ignore-scripts=true` and `save-exact=true`. `run-all.sh` also passes `--ignore-scripts` to `npm ci`. `package-lock.json` is committed.
- Maven: `pom.xml` pins pushy and each build plugin to an exact version.
- Go: `go.sum` is committed. The container runs with `GOFLAGS=-mod=readonly` and `GOTOOLCHAIN=local`, and the image is pinned by digest.
- Some transitive versions have OSV advisories. apns2 v0.25.0 pulls 2022 versions of `golang.org/x/net` and `github.com/golang-jwt/jwt/v4`, and pushy 0.15.6 pulls `netty-codec-http2` 4.1.133.Final. This is acceptable here for three reasons: the generators talk only to a loopback server or an in-memory recorder, they run only on demand, and nothing ships from them.
