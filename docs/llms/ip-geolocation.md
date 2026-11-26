---
domain: IP Geolocation
packages: IpGeolocation.Abstractions, IpGeolocation, IpGeolocation.MaxMind
---

# IP Geolocation

> Resolve an IP address to its country, subdivision, city, coordinates, time zone, and autonomous system from local MaxMind databases that the host keeps current.

## Orientation

Install all three packages in the host:

- `Headless.IpGeolocation.Abstractions`: `IIpGeolocator` and the `IpLocation` result. Libraries that only look up addresses reference this package alone.
- `Headless.IpGeolocation`: `AddHeadlessIpGeolocation(geo => …)`.
- `Headless.IpGeolocation.MaxMind`: the only provider, chosen with `geo.UseMaxMind(…)`. It brings the database updater, a readiness warm-up, and the `MaxMind` provisioned resource.

```csharp
builder.Services.AddHeadlessIpGeolocation(geo =>
    geo.UseMaxMind(options =>
    {
        options.DatabaseDirectory = Path.Combine(builder.Environment.ContentRootPath, "geoip");
        options.AccountId = builder.Configuration["MaxMind:AccountId"];
        options.LicenseKey = builder.Configuration["MaxMind:LicenseKey"];
    })
);
```

```csharp
public sealed class SessionLocationReader(IIpGeolocator geolocator)
{
    public string? Describe(string remoteIpAddress)
    {
        if (!IPAddress.TryParse(remoteIpAddress, out var address))
        {
            return null;
        }

        var location = geolocator.Locate(address);

        return location is null ? null : $"{location.City}, {location.CountryCode} (AS{location.AutonomousSystemNumber})";
    }
}
```

**Verify:** `dotnet MyApp.dll resources status -k MaxMind` prints each edition's build date (`built yyyy-MM-dd`) or `missing`. With credentials, the `warmup` readiness check reports `Healthy` once every edition is on disk.

## Agent Rules

- Call `AddHeadlessIpGeolocation` once, with exactly one provider. Zero providers, two providers, or a second call on the same service collection throws `InvalidOperationException` at registration.
- Inject `IIpGeolocator`. It is a thread-safe singleton. `Locate` is synchronous and reads an in-memory database, so it does no network I/O.
- Treat a `null` result as normal. `Locate` returns `null` for private, loopback, and reserved addresses, for addresses no database covers, and while the databases are not loaded yet. Every `IpLocation` property is nullable for the same reasons.
- Parse the address before you call `Locate`. It takes an `IPAddress`: use `IPAddress.TryParse` on stored strings. Pass `HttpContext.Connection.RemoteIpAddress` only after forwarded headers are applied, or you look up the proxy.
- Set `AccountId` and `LicenseKey` together, from a secret store, never in committed configuration. A GeoLite2 license key is free with a MaxMind account. Setting only one of them fails options validation at startup.
- Without a license key, put the `.mmdb` files in `DatabaseDirectory` yourself, named `<EditionId>.mmdb` (for example `GeoLite2-City.mmdb`). Nothing refreshes them.
- Do not commit MaxMind databases to source control or ship them in a package. The GeoLite license requires using each new release within 30 days of it. The updater meets that rule; a committed copy goes stale.
- Give each host a writable `DatabaseDirectory`. A directory that survives restarts (a mounted volume), or one an image build filled with `dotnet MyApp.dll resources provision -k MaxMind`, lets a new host report ready at once. An empty directory keeps the host not ready until the first download finishes. A baked-in copy still needs credentials at run time, so the updater can replace it before it goes stale.

---

## Core Concepts

### One provider, two local databases

`IIpGeolocator` has one implementation per application. The MaxMind provider reads a location edition (City or Country) and the ASN edition, and merges both into one `IpLocation`. You can turn either edition off, but not both.

### Updates without a restart

With credentials, the host checks MaxMind at startup and every `UpdateCheckInterval`. Each check sends one HEAD request per edition and downloads an edition only when MaxMind's release is newer than the local file. A new database is verified, written beside the old one, moved into place, and swapped into the geolocator atomically. Lookups already running finish on the previous database.

### Readiness waits for missing databases

With credentials, a host that starts without a database downloads it before the `warmup` readiness check reports `Healthy` ([Warm-up](utilities.md#warm-up)). Until then the check names the pending `MaxMindDatabases` warm-up, with the error of a failed first download. A failed release check retries at the warm-up back-off. An edition whose download failed is not downloaded again until `RetryDelay` has passed: until then each attempt fails at once with the remembered error and sends no request, because every download counts against the account's daily limit.

With every file present, the warm-up does nothing and does not hold readiness: the host serves from the files at once, and the updater checks for newer releases at startup. Without credentials nothing downloads, so a missing file logs a startup warning and readiness is not held.

### Download ahead of time

The provider registers a provisioned resource of kind `MaxMind` (`SetupMaxMind.DatabasesResourceKind`), named `databases` ([Resource provisioning](utilities.md#resource-provisioning)). Run `dotnet MyApp.dll resources provision -k MaxMind` in an image build or a deploy step, with the account credentials available to that step. It downloads every missing or outdated edition into `DatabaseDirectory` before a host starts.

### A failure keeps the last good database

A failed check or download, a checksum mismatch, or a file that is not a database leaves the current database in use, and the next attempt waits `RetryDelay`. The updater never stops the host. A corrupt file found at startup is logged and treated as missing.

---

## Headless.IpGeolocation.Abstractions

The provider-agnostic contract for IP geolocation.

### Setup

```bash
dotnet add package Headless.IpGeolocation.Abstractions
```

Reference this package from application and domain libraries that only call `IIpGeolocator`. The host registers the provider.

**Verify:** no signal of its own. The host's Verify step under [Headless.IpGeolocation.MaxMind](#headlessipgeolocationmaxmind) covers it.

### Design and runtime behavior

- `IIpGeolocator.Locate(IPAddress address)` returns `IpLocation?` and throws `ArgumentNullException` for a `null` address.
- `IpLocation` is an immutable record. Its properties are `CountryCode` (ISO 3166-1 alpha-2), `CountryName`, `SubdivisionCode` and `SubdivisionName` (the most specific subdivision), `City`, `PostalCode`, `Latitude`, `Longitude`, `AccuracyRadiusKilometers`, `TimeZone` (IANA), `AutonomousSystemNumber`, and `AutonomousSystemOrganization`. Every property is nullable.
- Names come in the first configured locale the database has. Codes and coordinates do not depend on the locale.

---

## Headless.IpGeolocation

The registration entry point.

### Setup

```bash
dotnet add package Headless.IpGeolocation
```

```csharp
builder.Services.AddHeadlessIpGeolocation(geo => geo.UseMaxMind(builder.Configuration.GetSection("MaxMind")));
```

**Verify:** a registration mistake throws `InvalidOperationException` from `AddHeadlessIpGeolocation`, before the host builds.

### Design and runtime behavior

- `services.AddHeadlessIpGeolocation(Action<HeadlessIpGeolocationSetupBuilder> configure)` registers the chosen provider.
- Zero providers, two providers, or a second `AddHeadlessIpGeolocation` call throws `InvalidOperationException` during registration.

---

## Headless.IpGeolocation.MaxMind

The MaxMind GeoIP2 and GeoLite2 provider, with automatic database updates.

### Setup

```bash
dotnet add package Headless.IpGeolocation.MaxMind
```

`UseMaxMind` has three overloads: `UseMaxMind(IConfiguration)`, `UseMaxMind(Action<MaxMindOptions>)`, and `UseMaxMind(Action<MaxMindOptions, IServiceProvider>)`.

```csharp
builder.Services.AddHeadlessIpGeolocation(geo =>
    geo.UseMaxMind(options =>
    {
        options.DatabaseDirectory = "/var/lib/myapp/geoip";
        options.LocationEditionId = "GeoLite2-City";
        options.AsnEditionId = "GeoLite2-ASN";
        options.AccountId = builder.Configuration["MaxMind:AccountId"];
        options.LicenseKey = builder.Configuration["MaxMind:LicenseKey"];
        options.Locales = ["ar", "en"];
    })
);
```

Configuration binding:

```json
{
  "MaxMind": {
    "DatabaseDirectory": "/var/lib/myapp/geoip",
    "AsnEditionId": "",
    "UpdateCheckInterval": "06:00:00"
  }
}
```

To turn an edition off from configuration, set `AsnEditionId` or `LocationEditionId` to an empty string, because configuration cannot bind `null`.

To route downloads through a proxy or add a handler, configure the named client: `services.AddHttpClient(MaxMindOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(...)`.

**Verify:** run `dotnet MyApp.dll resources verify -k MaxMind`. It reads only the disk, reports `Failed` naming each edition whose file is missing or unreadable, and exits 1. At run time, the `warmup` readiness check reports `Healthy` once the databases are loaded. In a test, call `await host.Services.WaitForWarmupAsync(cancellationToken)` before the first lookup.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `DatabaseDirectory` | (required) | Holds `<EditionId>.mmdb`. The updater creates it when missing. A relative path resolves against the host's content root, not the working directory. |
| `LocationEditionId` | `GeoLite2-City` | Also `GeoLite2-Country`, or a paid `GeoIP2-*` City or Country edition. Null or empty turns it off. |
| `AsnEditionId` | `GeoLite2-ASN` | Null or empty turns it off. At least one edition must stay on. |
| `AccountId`, `LicenseKey` | none | Set both or neither. Neither means local files only. |
| `Locales` | `["en"]` when left empty | Name languages in order of preference. A configured list replaces the default instead of appending to it. |
| `UpdateCheckInterval` | 12 hours | At least 1 hour. GeoLite accounts may download 30 databases a day. HEAD checks do not count. |
| `RetryDelay` | 30 minutes | The wait after a failed check, and the least time before a failed edition downloads again (`resources provision` excepted). At least 1 minute. |
| `DownloadEndpoint` | `https://download.maxmind.com/` | HTTPS, or HTTP on loopback for tests. |
| `DownloadTimeout` | 10 minutes | Per request. |

Options validate at startup and throw `OptionsValidationException`.

### Design and runtime behavior

- **Startup load.** The host loads each enabled edition from `DatabaseDirectory`. A missing file logs warning `MaxMindDatabaseMissing`. An unreadable file logs error `MaxMindDatabaseUnreadable`. Lookups then leave that edition's fields empty.
- **Update schedule.** With credentials and every database loaded, the updater checks at startup and then every `UpdateCheckInterval`, on the registered `TimeProvider`. When a database was missing, the warm-up makes the first check and holds readiness until it succeeds, and the updater's first check comes one `UpdateCheckInterval` after the start. The updater, the warm-up, and a resource operation run one at a time.
- **Download.** A download fetches the archive and its published SHA-256, rejects a mismatch, extracts the `.mmdb`, proves it opens, moves it over the old file, sets the file's write time to MaxMind's `Last-Modified`, and logs `MaxMindDatabaseUpdated`.
- **Failure.** A failed check or download logs `MaxMindDatabaseUpdateFailed`, keeps the current database, and retries after `RetryDelay`.
- **Staleness.** A database built more than 30 days ago logs warning `MaxMindDatabaseStale`: at startup without credentials, and at each check with them.
- **Coverage.** Lookups on a City or Enterprise database fill every field. Lookups on a Country database fill only the country fields.
- **Memory.** Databases load fully into memory, so a file can be replaced while in use on every OS. Memory use equals the database sizes: about 64 MB for GeoLite2 City and 9 MB for GeoLite2 ASN.
- **Per host.** Each host checks and downloads for its own `DatabaseDirectory`. No download is shared across hosts.
- **License.** The package ships no MaxMind data. Downloads use the application's own account, under MaxMind's license terms.

The `MaxMind` resource supports these `resources` subcommands. Its startup check is `None`, because the warm-up already holds readiness until the files are on disk.

| Subcommand | Behavior |
| --- | --- |
| `provision` | With credentials, checks every edition and downloads each one that is missing or older than MaxMind's release, even inside the `RetryDelay` after a failed download. Fails naming each edition it could not update. Without credentials, does nothing. |
| `verify` | Reads only the disk. Fails with `ResourceProvisioningException` naming each edition whose file is missing or is not a readable database. |
| `purge` | Unsupported: the files are the only state. |
| `delete` | Deletes every edition's file, and the running host stops answering from it. |
| `status` | Each edition's build date (`built yyyy-MM-dd`) or `missing`, then whether downloads are enabled. |
