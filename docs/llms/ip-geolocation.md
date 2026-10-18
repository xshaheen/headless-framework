---
domain: IP Geolocation
packages: IpGeolocation.Abstractions, IpGeolocation, IpGeolocation.MaxMind
---

# IP Geolocation

> Resolve an IP address to its country, subdivision, city, coordinates, time zone, and autonomous system, from local MaxMind databases that keep themselves current.

## Orientation

Install all three packages:

- `Headless.IpGeolocation.Abstractions`: `IIpGeolocator` and the `IpLocation` result
- `Headless.IpGeolocation`: registration through `AddHeadlessIpGeolocation(geo => …)`
- `Headless.IpGeolocation.MaxMind`: the MaxMind provider, chosen with `geo.UseMaxMind(…)`, its database updater, its readiness warm-up, and the `MaxMind` stateful resource

Typical registration:

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

Lookup:

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

## Agent Rules

- Install all three packages. The abstractions package has no implementation, and `AddHeadlessIpGeolocation` throws `InvalidOperationException` unless exactly one provider is chosen.
- Register once. A second `AddHeadlessIpGeolocation` call on the same service collection throws `InvalidOperationException`.
- Inject `IIpGeolocator`. It is a thread-safe singleton, and `Locate` is synchronous: it reads an in-memory database and does no network I/O.
- Treat a `null` result as normal. Private, loopback, and reserved addresses, addresses no database covers, and a host whose databases are not loaded yet all return `null`. Every `IpLocation` property is nullable for the same reason. With credentials, a host that lacks a database reports not ready until its first download lands, so an orchestrator that honors readiness sends it no lookups before then.
- Parse the address first. `Locate` takes an `IPAddress`; use `IPAddress.TryParse` on stored strings and pass `HttpContext.Connection.RemoteIpAddress` only after forwarded headers are applied.
- Set `AccountId` and `LicenseKey` together, from a secret store, never in committed configuration. A GeoLite2 key is free with a MaxMind account. One without the other fails options validation at first resolution.
- Without a license key, place the `.mmdb` files in `DatabaseDirectory` yourself, named `<EditionId>.mmdb` (for example `GeoLite2-City.mmdb`). Nothing refreshes them, and the provider logs a warning at startup when one is more than 30 days old.
- Do not commit MaxMind databases to source control or ship them in a package. The GeoLite license requires using each new release within 30 days of it; the updater meets that, and a committed copy goes stale.
- Give each host a writable `DatabaseDirectory`. A directory that survives restarts, such as a mounted volume, or one an image build filled with `dotnet MyApp.dll resources setup -t MaxMind`, lets a new host report ready at once; an empty one keeps it not ready until the first download finishes. A baked-in copy still needs credentials at run time, so the updater replaces it before it goes stale.

---

## Core Concepts

### One provider, local databases

`IIpGeolocator` has one implementation per application. The MaxMind provider reads two databases: a location edition (City or Country) and the ASN edition. A lookup merges both into one `IpLocation`; either edition can be turned off.

### Updates without a restart

With credentials, a hosted service checks MaxMind at startup (the warm-up makes that first check when a database is missing) and every `UpdateCheckInterval`. It sends a HEAD request per edition and downloads only when MaxMind's `Last-Modified` is newer than the release the local file came from. A new database is verified, written beside the old one, moved into place, and swapped into the geolocator atomically. Lookups already running finish on the previous database.

### Ready only with databases

With credentials, the `MaxMindDatabases` warm-up makes a host that started without a database download it before the `warmup` readiness check reports healthy ([Warm-up](utilities.md#warm-up)). It checks every edition, so the updater skips its own first check and next checks an interval later. A failed first download is named in the readiness check. A failed release check is retried at the warm-up back-off, but an edition whose download failed is not downloaded again until `RetryDelay` has passed: until then each warm-up attempt, and the updater, fail at once with the remembered error and send no request, because every download counts against the account's daily limit. With every file present, the warm-up does nothing and does not hold readiness: the host serves from the files at once and the updater checks for newer releases at startup. Without credentials nothing can download, so a missing file stays a startup warning and readiness is not held.

### Downloading ahead of time

The provider registers a stateful resource of type `MaxMind`, named `databases` ([Stateful resources](utilities.md#stateful-resources)). Run `dotnet MyApp.dll resources setup -t MaxMind` in an image build or a deploy step, with the account credentials available to that step, to download every missing or outdated edition into `DatabaseDirectory` before a host starts.

### Failure keeps the last good database

A failed check or download, a checksum mismatch, or a file that is not a database leaves the current database in use and schedules the next attempt after `RetryDelay`. A corrupt file found at startup is logged and treated as missing.

---

## Headless.IpGeolocation.Abstractions

Provider-agnostic contract for IP geolocation.

### API and behavior

- `IIpGeolocator.Locate(IPAddress address)`: returns `IpLocation?`; throws `ArgumentNullException` for a `null` address.
- `IpLocation`: an immutable record with `CountryCode` (ISO 3166-1 alpha-2), `CountryName`, `SubdivisionCode` and `SubdivisionName` (the most specific subdivision), `City`, `PostalCode`, `Latitude`, `Longitude`, `AccuracyRadiusKilometers`, `TimeZone` (IANA), `AutonomousSystemNumber`, and `AutonomousSystemOrganization`. Every property is nullable.

### Install

```bash
dotnet add package Headless.IpGeolocation.Abstractions
```

### Setup and use

Reference this package from application and domain libraries that only call `IIpGeolocator`. The host registers the provider.

### Runtime behavior

- Names come in the first configured locale the database has; codes and coordinates do not depend on locale.

---

## Headless.IpGeolocation

Registration entry point.

### API and behavior

- `services.AddHeadlessIpGeolocation(Action<HeadlessIpGeolocationSetupBuilder> configure)`: registers the chosen provider.
- `HeadlessIpGeolocationSetupBuilder.RegisterExtension(IIpGeolocationProviderOptionsExtension)`: the hook provider packages use from their `Use*` member; applications do not call it.

### Install

```bash
dotnet add package Headless.IpGeolocation
```

### Setup and use

```csharp
builder.Services.AddHeadlessIpGeolocation(geo => geo.UseMaxMind(builder.Configuration.GetSection("MaxMind")));
```

### Runtime behavior

- Zero providers, two providers, or a second `AddHeadlessIpGeolocation` call throws `InvalidOperationException` during registration, before the host starts.

---

## Headless.IpGeolocation.MaxMind

MaxMind GeoIP2 and GeoLite2 provider with automatic database updates.

### API and behavior

- `UseMaxMind(IConfiguration)`, `UseMaxMind(Action<MaxMindOptions>)`, and `UseMaxMind(Action<MaxMindOptions, IServiceProvider>)` choose the provider.
- `MaxMindOptions.HttpClientName` names the `HttpClient` that downloads updates, so an application can add a proxy or a handler: `services.AddHttpClient(MaxMindOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(...)`.
- `UseMaxMind` also registers the `MaxMindDatabases` warm-up and the `MaxMind` stateful resource. The resource's operations:

| Operation | Behavior |
| --- | --- |
| `setup` | With credentials, checks every edition and downloads each one that is missing or older than MaxMind's release, even inside the `RetryDelay` after a failed download; fails naming each edition it could not update. Without credentials, does nothing. |
| `check` | Reads only the disk. Fails with `StatefulResourceException` naming each edition whose file is missing or is not a readable database. |
| `clear` | Unsupported: the files are the only state. |
| `teardown` | Deletes every edition's file, and the running host stops answering from it. |
| `status` | Each edition's build date (`built yyyy-MM-dd`) or `missing`, then whether downloads are enabled. |

### Design constraints

- The package ships no MaxMind data. Downloads use the application's own account, under MaxMind's license terms.
- Databases load fully into memory so a file can be replaced while it is in use, on every OS. Memory use equals the database sizes: about 64 MB for GeoLite2 City and 9 MB for GeoLite2 ASN.
- Each host checks and downloads for its own `DatabaseDirectory`; there is no download shared across hosts.

### Install

```bash
dotnet add package Headless.IpGeolocation.MaxMind
```

### Setup and use

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

An empty `AsnEditionId` or `LocationEditionId` turns that edition off; configuration cannot bind `null`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `DatabaseDirectory` | (required) | Holds `<EditionId>.mmdb`; created by the updater when missing. |
| `LocationEditionId` | `GeoLite2-City` | Also `GeoLite2-Country` or a paid `GeoIP2-*` City or Country edition. Null or empty turns it off. |
| `AsnEditionId` | `GeoLite2-ASN` | Null or empty turns it off. At least one edition must stay on. |
| `AccountId`, `LicenseKey` | none | Both or neither. Neither means local files only. |
| `Locales` | `["en"]` | Name languages in order of preference. |
| `UpdateCheckInterval` | 12 hours | At least 1 hour. GeoLite accounts may download 30 databases a day; HEAD checks do not count. |
| `RetryDelay` | 30 minutes | Wait after a failed check, and the least time before an edition whose download failed is downloaded again (`resources setup` excepted); at least 1 minute. |
| `DownloadEndpoint` | `https://download.maxmind.com/` | HTTPS, or HTTP on loopback for tests. |
| `DownloadTimeout` | 10 minutes | Per request. |

Options validate when first resolved and throw `OptionsValidationException`.

### Runtime behavior

- Startup loads each enabled edition from `DatabaseDirectory`. A missing file logs warning `MaxMindDatabaseMissing`; an unreadable one logs error `MaxMindDatabaseUnreadable`. Lookups then leave that edition's fields empty.
- With credentials and every database loaded, the updater checks at startup and then every `UpdateCheckInterval`, on the registered `TimeProvider`. When a database was missing, the warm-up makes the first check and holds readiness until it succeeds, and the updater's first check comes one `UpdateCheckInterval` after the start. The updater, the warm-up, and a resource operation run one at a time. Each check sends one HEAD request per edition. A download fetches the archive and its published SHA-256, rejects a mismatch, extracts the `.mmdb`, proves it opens, moves it over the old file, sets the file's write time to MaxMind's `Last-Modified`, and logs `MaxMindDatabaseUpdated`.
- A failure logs `MaxMindDatabaseUpdateFailed`, keeps the current database, and retries after `RetryDelay`. The updater never stops the host.
- A database built more than 30 days ago logs warning `MaxMindDatabaseStale`: at startup without credentials, and at each check with them.
- Lookups on a City or Enterprise database fill every field; on a Country database they fill only the country fields.
