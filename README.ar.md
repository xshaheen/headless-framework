<p align="center">
  <img src="assets/banner.svg" alt=".NET Headless Framework" width="100%">
</p>

<div dir="rtl" align="right">

# .NET Headless Framework

</div>

<div align="center" dir="rtl">

**Infrastructure جاهزة للـ production لخدمات .NET، من غير ما framework يفرض عليك شكل تطبيقك.**

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![NuGet](https://img.shields.io/nuget/v/Headless.Extensions?label=nuget)](https://www.nuget.org/packages?q=Headless.)
[![GitHub Stars](https://img.shields.io/github/stars/xshaheen/headless-framework?style=social)](https://github.com/xshaheen/headless-framework)
[![English](https://img.shields.io/badge/lang-English-2563EB?style=flat-square)](README.md)

168 package &bull; نفس الـ setup في كل حتة &bull; بدّل أي provider بسطر واحد

[ليه Headless](#ليه-headless) &bull; [ابدأ في 60 ثانية](#ابدأ-في-60-ثانية) &bull; [Setup واحد لكل المجالات](#setup-واحد-لكل-المجالات) &bull; [إيه اللي في الصندوق](#إيه-اللي-في-الصندوق) &bull; [فهرس الحزم](#فهرس-الحزم)

</div>

---

<div dir="rtl" align="right">

## ليه Headless

أي backend service محتاجة نفس العشرين حاجة: cache، وblob storage، وbackground jobs، وmessage bus، وdistributed locks، وfeature flags، وdynamic settings، وaudit log، وemails، وSMS. وقدامك 3 اختيارات معروفة، وكل واحد فيهم بيكلّفك حاجة.

- **تكتبها بنفسك.** هتكتب الـ outbox، والـ query اللي بتعمل claim للـ job، وتجديد الـ lock، كله بإيدك. ودي بالظبط الحاجات اللي بتقع الساعة 3 الفجر.
- **تلزّق عشرين library مع بعض.** StackExchange.Redis و Hangfire و MassTransit و Azure SDKs: كل واحدة ليها setup بشكل مختلف، وفهم مختلف لمعنى "connection"، ورأي خاص في الـ DI container بتاعك.
- **تاخد framework تطبيق كامل.** هتاخد كل حاجة مرة واحدة، ومعاها base classes لازم ترث منها، وmodule system لازم تمشي عليه، وطريق خروج محدش عايز يمشي فيه.

Headless هو الاختيار الرابع. كل feature بتيجي كـ package صغيرة فيها الـ abstractions بس، ومعاها packages للـ providers إنت اللي تختار منها في الـ composition root. كودك بيشوف `ICache`، مش Redis. وكل الـ features ليها نفس شكل الـ setup، يعني لما تتعلم الـ caching تبقى اتعلمت الـ blob storage. مفيش حاجة بترث من حاجة، ومفيش حاجة بتشتغل من غير ما تسجّلها إنت.

### إيه اللي بتكسبه

**تبدّل الـ provider بسطر واحد.** شغّل in-memory في الـ tests، وRedis في الـ production، ومتغيّرش أي حاجة تانية.

</div>

```csharp
// Composition root. This is the only file that names a provider.
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory()); // dev and tests
builder.Services.AddHeadlessCaching(setup => setup.UseRedis(...)); // production
```

<div dir="rtl" align="right">

أي service أو repository أو handler بياخد `ICache` مش هيتأثر بالتعديل دا. ونفس الكلام على `IBlobStorage` بين S3 و Azure و Cloudflare R2 و FileSystem و Redis و SFTP، وعلى `IEmailSender` بين SES و Azure Communication Services و SMTP، وعلى الـ messaging بين 8 transports.

**هتركّب 3 packages، مش 168.** الفهرس كبير عشان عدد الـ providers كبير. الخدمة اللي محتاجة caching بتركّب `Headless.Caching.Abstractions` و `Headless.Caching.Core` وprovider واحد. والـ libraries بتاعة الـ domain والـ application بتعتمد على الـ Abstractions package لوحدها: `Headless.Caching.Abstractions` بتجرّ وراها حاجة واحدة بس، هي `Headless.Extensions`.

**الـ tests مش محتاجة Docker عشان تبقى سريعة.** الـ caching والـ distributed locks والـ messaging فيهم in-memory providers؛ والـ emails والـ SMS والـ push notifications فيهم dev providers مش بتبعت حاجة؛ والـ blob storage بيشتغل على الـ file system المحلي. يعني الـ unit tests بتجرّب نفس الـ contract الحقيقي من غير containers. ولما تحتاج الـ backend الحقيقي، `Headless.Testing.Testcontainers` بتجهّزلك الـ fixtures. الـ repo نفسه ماشي على التقسيمة دي: 130 مشروع unit tests و 60 مشروع integration tests.

**الحاجات الصعبة متكتوبة خلاص.** الـ background jobs بتعمل claim للشغل بشكل atomic عن طريق `FOR UPDATE SKIP LOCKED` في PostgreSQL و `UPDLOCK, READPAST, ROWLOCK` في SQL Server. والـ messaging بتكتب في transactional outbox جوه نفس الـ save بتاع EF Core. والـ distributed locks بتستخدم advisory locks في PostgreSQL و application locks في SQL Server، مش `SET NX` مرتجل. والـ node membership بتقرا الـ liveness من ساعة الـ server، مش من ساعة الـ node نفسها. كل نقطة من دول مكان بتضيع فيه messages أو بيتنفّذ فيه job مرتين بسبب implementation شكله سليم وهو مش سليم.

**Dashboards جايين معاه.** `Headless.Jobs.Dashboard` و `Headless.Messaging.Dashboard` واجهات web حقيقية تتابع بيها الـ runs والـ failures والـ retries، بـ authentication مشتركة وnode discovery على Kubernetes.

**توثيق مظبوط لوكلاء الـ AI.** [`docs/llms/`](docs/llms/) فيه docs لكل domain، متكتوبة عشان الـ agents تجيبها وقت الحاجة. شوف [استخدام Headless مع وكلاء الـ AI](#استخدام-headless-مع-وكلاء-الـ-ai).

### إمتى متستخدمهوش

Headless مش template لتطبيق ولا starter kit. مش هيديك scaffolding، ولا admin UI، ولا CRUD generator، ولا رأي في الـ architecture بتاعتك. لو عايز platform كاملة ترصّلك التطبيق من أوله لآخره، ABP أو Orchard Core أنسب.

وكمان مش بيفرض عليك حصرية. كمّل على MassTransit وضيف `Headless.Caching` بس. كمّل على Hangfire وضيف `Headless.Blobs` بس. كل family بتقف لوحدها.

## ابدأ في 60 ثانية

### شغّل API host

</div>

```bash
dotnet add package Headless.Api.ServiceDefaults
```

```csharp
var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, OpenAPI, problem details, JSON, health checks, forwarded headers,
// compression, exception handling, HSTS, status-code pages, and Headless endpoints.
builder.AddHeadless();

var app = builder.Build();

// Applies the Headless middleware order: forwarded headers, compression,
// status-code/problem-details handling, exceptions, HTTPS/HSTS, and no-cache defaults.
app.UseHeadless();

// Maps Headless operational endpoints such as health, liveness, OpenAPI JSON,
// and static web assets when enabled.
app.MapHeadlessEndpoints();

app.Run();
```

<div dir="rtl" align="right">

### ضيف Cache

الكود اللي بيستخدم الـ cache بس بيعتمد على `Headless.Caching.Abstractions`. أما الـ host اللي بيشغّل الخدمة فبيضيف package الـ runtime وprovider واحد.

</div>

```bash
dotnet add package Headless.Caching.Abstractions
dotnet add package Headless.Caching.Core
dotnet add package Headless.Caching.InMemory
```

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseInMemory();
    setup.AddNamed("sessions", cache => cache.UseInMemory());
});
```

<div dir="rtl" align="right">

وعشان تنقل على Redis، ركّب package الـ provider وغيّر سطر الـ setup. الكود اللي بيعتمد على `ICache` مش هيتغيّر.

</div>

```bash
dotnet add package Headless.Caching.Redis
```

```csharp
builder.Services.AddHeadlessCaching(setup =>
{
    setup.UseRedis(options =>
    {
        options.ConnectionMultiplexer =
            ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!);
    });
});
```

<div dir="rtl" align="right">

### ضيف Blob Storage

استخدم الـ named stores لما التطبيق يحتاج أكتر من backend للتخزين، أو أكتر من instance من نفس الـ backend.

</div>

```bash
dotnet add package Headless.Blobs.Abstractions
dotnet add package Headless.Blobs.Core
dotnet add package Headless.Blobs.FileSystem
```

```csharp
builder.Services.AddHeadlessBlobs(blobs =>
{
    blobs.UseFileSystem(options => options.BaseDirectoryPath = "/var/app/blobs");
    blobs.AddNamed("scratch", store => store.UseFileSystem(options => options.BaseDirectoryPath = "/tmp/app-blobs"));
});
```

<div dir="rtl" align="right">

### خطوات أبعد

في [`docs/llms/index.md`](docs/llms/index.md) هتلاقي خدمة كاملة شغالة: upload متحقَّق منه، وكتابة في الـ blob storage، وقراية عن طريق read-through cache، وجدولة background job. وفي [`demo/`](demo/) فيه 20 مثال تقدر تشغّلهم.

## Setup واحد لكل المجالات

شكل الـ setup واحد في كل حتة. اتعلمه مرة واحدة.

</div>

```csharp
services.AddHeadless<Feature>(setup => setup.Use<Provider>(options => { ... }));
```

<div dir="rtl" align="right">

| الـ Feature | نقطة الدخول | الـ Providers المتاحة |
|------------|------------|---------------------|
| Caching | `AddHeadlessCaching` | `UseInMemory`، `UseRedis`، `UseHybrid` (L1+L2) |
| Blob storage | `AddHeadlessBlobs` | `UseAws`، `UseAzure`، `UseCloudflareR2`، `UseFileSystem`، `UseRedis`، `UseSsh` |
| Emails | `AddHeadlessEmails` | `UseAwsSes`، `UseAzure`، `UseMailkit`، `UseDevelopment`، `UseNoop` |
| SMS | `AddHeadlessSms` | `UseTwilio`، `UseAwsSns`، `UseInfobip`، `UseCequens`، `UseConnekio`، `UseVictoryLink`، `UseVodafone`، `UseDevelopment` |
| Push notifications | `AddHeadlessPushNotifications` | `UseFirebase`، `UseNoop` |
| Distributed locks | `AddHeadlessDistributedLocks` | `UseInMemory`، `UseRedis`، `UsePostgreSql`، `UseSqlServer` |
| Node membership | `AddHeadlessCoordination` | `UseRedis`، `UsePostgreSql`، `UseSqlServer` |
| Feature flags | `AddHeadlessFeatures` | `UseEntityFramework<TContext>`، `UsePostgreSql`، `UseSqlServer` |
| Dynamic settings | `AddHeadlessSettings` | `UseEntityFramework<TContext>`، `UsePostgreSql`، `UseSqlServer` |
| Permissions | `AddHeadlessPermissions` | `UseEntityFramework<TContext>`، `UsePostgreSql`، `UseSqlServer` |
| Audit log | `AddHeadlessAuditLog` | `UseEntityFramework<TContext>`، `UsePostgreSql`، `UseSqlServer` |
| CAPTCHA | `AddHeadlessCaptcha` | `UseReCaptchaV2`، `UseReCaptchaV3`، `UseTurnstile` |
| Messaging | `AddHeadlessMessaging` | Transport: `UseRabbitMq`، `UseKafka`، `UseAws`، `UseAzureServiceBus`، `UseNats`، `UsePulsar`، `UseRedis`، `UseInMemory`. Storage: `UsePostgreSql`، `UseSqlServer`، `UseInMemoryStorage` |
| Background jobs | `AddHeadlessJobs` | Storage على EF Core مع atomic claims في PostgreSQL أو SQL Server |

وقاعدتين بتيجوا مع الشكل دا:

- **الـ setup صريح.** الخدمة بتسجّل الـ features والـ providers اللي بتستخدمها بس. مفيش package بتسجّل نفسها، ومفيش واحدة بتعمل scan للـ assemblies بتاعتك من ورا ظهرك.
- **الـ named instances جاهزة.** لما الخدمة تتعامل مع cache-ين، أو 3 blob stores، أو SMS senders مختلفين، `AddNamed` بتدي لكل واحد key بدل ما تضطر تعمل container تاني.

## إيه اللي في الصندوق

| المجال | إيه اللي بتاخده |
|-------|-----------------|
| **API host** | Setup بسطر واحد عن طريق `AddHeadless()`: Problem Details، وOpenTelemetry، وOpenAPI، وhealth checks، وcompression، وforwarded headers، وHSTS، وvalidation وقت الـ startup. وتكامل مع Minimal API و MVC، وfilters للـ FluentValidation، وHTTP idempotency بأسلوب Stripe. |
| **Data** | Conventions للـ EF Core، وglobal filters، وsoft deletes، وbase types للـ DDD، وseed data. وconnection factories خام لـ PostgreSQL و SQL Server و SQLite. وCouchbase. ودعم geospatial عن طريق NetTopologySuite. |
| **State & storage** | Caching (memory، وRedis، وhybrid L1/L2، وtagging، وحماية من الـ stampede). وblob storage على 6 backends. وdynamic settings وfeature flags وpermissions وaudit log، كل واحدة بـ 3 storage providers. |
| **Distributed runtime** | Messaging بـ transactional outbox وretries وdelayed delivery فوق 8 transports. وbackground jobs بـ cron وretries وregistration بيتولّد وقت الـ compile. وdistributed locks. وnode membership وliveness. وunit of work scoped بيصرّف شغل الـ outbox والـ jobs بشكل atomic عند الـ commit. |
| **Integrations** | Emails، وSMS، وpush notifications، وCAPTCHA، وimage processing، واستخراج نصوص من الـ media، ومدفوعات Paymob، وresumable uploads عن طريق TUS، وsitemaps، وslugs، وبناء الـ URLs. |
| **Multi-tenancy** | الـ tenant context بيسري في الـ HTTP resolution، وفي الـ query filters بتاعة EF Core، وفي cache الـ permissions، وفي headers الـ messaging، ومعاه tenant catalog اختياري. |
| **Testing** | Base classes للـ xUnit v3، وbuilders بـ Bogus، وfixtures فوق `WebApplicationFactory` مع reset للـ database، وfixtures للـ Testcontainers، وtest harness للـ messaging بيتحقق من الـ published والـ consumed والـ faulted. |

## شكل الـ Packages

معظم الـ families ماشية بنفس التقسيمة:

</div>

```text
Headless.<Feature>.Abstractions  -> contracts your application code depends on
Headless.<Feature>.Core          -> provider-agnostic runtime and setup builder
Headless.<Feature>.<Provider>    -> concrete backend integration
Headless.<Feature>.Testing       -> test helpers, where the domain has them
```

<div dir="rtl" align="right">

خلّي الـ libraries بتاعة الـ domain والـ application تعتمد على الـ Abstractions package لوحدها. وضيف الـ Core package وpackage الـ provider في الـ composition root بتاع الـ host. التقسيمة دي هي اللي بتمنع أنواع الـ provider إنها تتسرّب لكود الـ business.

## ملاحظات مهمة للـ Production

الجاهزية للـ production قرار composition، مش switch عام بتفتحه.

- أي state لازم تعيش بعد restart، استخدم لها durable provider.
- الـ in-memory والـ dev providers للتطوير والـ tests والـ demos المعزولة.
- استخدم named instances لو الخدمة بتتعامل مع أكتر من store أو أكتر من sender.
- خلّي الـ configuration بتاعة الـ provider في الـ composition root، ومتسرّبش أنواعه لكود الـ business، إلا لو الـ option بيعرض نوع من الـ SDK عن قصد.
- اقرا الـ README بتاع كل package بتركّبها. كل واحدة بتوضّح الـ dependencies والـ side effects ومتطلبات الـ setup وحدود الـ provider.
- اختبر بنفس مجموعة الـ providers اللي هتشغّلها في الـ production، طول ما السلوك بيعتمد على الـ storage أو الـ transactions أو الـ locks أو الـ ordering أو تسليم الـ broker أو خصائص خدمة cloud.

## الإصدارات والتوافق

الـ packages منشورة على nuget.org عند `0.4.x`. الـ framework لسه قبل 1.0، والمشروع greenfield: الـ breaking changes بتنزل لما تحسّن الصحة أو الـ API بشكل جوهري، بدل ما نراكم طبقات توافق. ثبّت الـ versions بتاعتك واقرا الـ release notes قبل أي upgrade.

- أغلب الـ packages بتستهدف `.NET 10`. وpackages الـ source generators بتستهدف `netstandard2.0`.
- الـ repo بيثبّت version الـ .NET SDK في [`global.json`](global.json).
- الـ release notes بتتنشر عن طريق [GitHub releases](https://github.com/xshaheen/headless-framework/releases).

وركّز بالذات على الـ release notes اللي بتخص الـ configuration APIs، وsetup الـ providers، وschema الـ storage، وسلوك الـ retry، والكود المتولّد.

## استخدام Headless مع وكلاء الـ AI

ضيف المقطع دا في `AGENTS.md` أو `CLAUDE.md` عشان الـ coding agents تجيب الـ docs الصح بدل ما تخمّن الـ API:

</div>

```markdown
## Headless Framework

This project uses [Headless .NET Framework](https://github.com/xshaheen/headless-framework).

When working with Headless packages, fetch the docs index:
https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/index.md

The index lists per-domain docs to fetch as needed.
```

<div dir="rtl" align="right">

الـ index فيه الـ rules بتاعة الـ framework للـ agents، وlinks لتوثيق كل domain تحت [`docs/llms/`](docs/llms/).

## توسيع Headless

packages الـ providers دي packages عادية على NuGet. عشان تضيف backend خاص بيك، نفّذ الـ abstraction بتاعة الـ feature، واعمل extension اسمه `Use{Provider}` ماشي على نفس شكل الـ builder بتاع الـ family، وخلّي تفاصيل الـ provider في الـ composition root. واقرا الأول الـ README بتاع أقرب provider موجود في نفس الـ feature.

## فهرس الحزم

</div>

<details dir="rtl" align="right">
<summary><strong>كل الـ 168 package، مرتّبة حسب الـ domain</strong> — اضغط للعرض</summary>

### API & Web

APIs جاهزة للـ production على ASP.NET Core: conventions للـ request والـ response، وvalidation pipelines، وstructured logging، وتوثيق OpenAPI.

| Package | الوصف |
|---------|-------|
| [Headless.Api.Core](src/Headless.Api.Core/README.md) | Building blocks لبناء ASP.NET Core APIs (Problem Details، JWT، identity، middleware) |
| [Headless.Api.ServiceDefaults](src/Headless.Api.ServiceDefaults/README.md) | نقطة دخول `AddHeadless()` مع defaults بأسلوب Aspire (OpenTelemetry، OpenAPI، service discovery) |
| [Headless.Api.Abstractions](src/Headless.Api.Abstractions/README.md) | الـ abstractions والـ contracts بتاعة الـ API |
| [Headless.Api.DataProtection](src/Headless.Api.DataProtection/README.md) | تخزين مفاتيح الـ Data Protection على أي `IBlobStorage` |
| [Headless.Api.FluentValidation](src/Headless.Api.FluentValidation/README.md) | ربط FluentValidation بطبقة الـ API |
| [Headless.Api.Logging.Serilog](src/Headless.Api.Logging.Serilog/README.md) | Enrichers لـ Serilog على مستوى كل request |
| [Headless.Api.MinimalApi](src/Headless.Api.MinimalApi/README.md) | أدوات للـ Minimal API |
| [Headless.Api.Mvc](src/Headless.Api.Mvc/README.md) | أدوات للـ MVC |
| [Headless.Api.Idempotency](src/Headless.Api.Idempotency/README.md) | HTTP idempotency middleware بأسلوب Stripe: بيـ cache الـ response ويرجّعه عند الـ retry |

### Core

Building blocks مشتركة عبر الـ framework: domain primitives، وbase types للـ DDD، وguard clauses، وentity/event infrastructure.

| Package | الوصف |
|---------|-------|
| [Headless.Extensions](src/Headless.Extensions/README.md) | Extension methods وcollections وIO وthreading وreflection helpers |
| [Headless.Core](src/Headless.Core/README.md) | Building blocks للـ Domain-Driven Design |
| [Headless.Security.Abstractions](src/Headless.Security.Abstractions/README.md) | الـ contracts والـ options بتاعة الـ security |
| [Headless.Security](src/Headless.Security/README.md) | تشفير الـ strings والـ hashing |
| [Headless.Checks](src/Headless.Checks/README.md) | Guard clauses وvalidation للـ arguments |
| [Headless.Domain](src/Headless.Domain/README.md) | Entities وevents بتاعة الـ domain |
| [Headless.Domain.LocalEventBus](src/Headless.Domain.LocalEventBus/README.md) | `ILocalEventBus` لنشر الـ domain events جوه نفس الـ process |
| [Headless.Mediator](src/Headless.Mediator/README.md) | Pipeline behaviors للـ mediator (FluentValidation، وlogging للـ request والـ response) |
| [Headless.MultiTenancy.Abstractions](src/Headless.MultiTenancy.Abstractions/README.md) | الـ contracts بتاعة الـ tenant context، ومعاها الـ store SPI والـ models بتاعة الـ tenant catalog الاختياري |
| [Headless.MultiTenancy](src/Headless.MultiTenancy/README.md) | تركيب الـ tenant posture عبر packages الـ Headless |
| [Headless.MultiTenancy.Storage.EntityFramework](src/Headless.MultiTenancy.Storage.EntityFramework/README.md) | `ITenantStore` على EF Core للـ tenant catalog الاختياري |

### Audit Log

Audit logging على مستوى الـ property لتتبّع تغييرات الـ entities وأحداث الـ business الصريحة: إيه اللي اتغيّر، ومين غيّره، وإمتى.

| Package | الوصف |
|---------|-------|
| [Headless.AuditLog.Abstractions](src/Headless.AuditLog.Abstractions/README.md) | الـ contracts بتاعة الـ audit log |
| [Headless.AuditLog.Core](src/Headless.AuditLog.Core/README.md) | الـ DI setup، وvalidation للـ options، وpipeline الـ providers |
| [Headless.AuditLog.Storage.EntityFramework](src/Headless.AuditLog.Storage.EntityFramework/README.md) | تخزين الـ audit log على EF Core |
| [Headless.AuditLog.Storage.PostgreSql](src/Headless.AuditLog.Storage.PostgreSql/README.md) | تخزين الـ audit log في PostgreSQL |
| [Headless.AuditLog.Storage.SqlServer](src/Headless.AuditLog.Storage.SqlServer/README.md) | تخزين الـ audit log في SQL Server |

### Blob Storage

Interface واحدة لتخزين الملفات، وproviders لكل cloud وprotocol رئيسي.

| Package | الوصف |
|---------|-------|
| [Headless.Blobs.Abstractions](src/Headless.Blobs.Abstractions/README.md) | الـ interfaces بتاعة الـ blob storage |
| [Headless.Blobs.Core](src/Headless.Blobs.Core/README.md) | Setup builder للـ default store وللـ named stores |
| [Headless.Blobs.Aws](src/Headless.Blobs.Aws/README.md) | Provider لـ AWS S3 |
| [Headless.Blobs.Azure](src/Headless.Blobs.Azure/README.md) | Provider لـ Azure Blob Storage |
| [Headless.Blobs.CloudflareR2](src/Headless.Blobs.CloudflareR2/README.md) | Provider لـ Cloudflare R2 (متوافق مع S3) |
| [Headless.Blobs.FileSystem](src/Headless.Blobs.FileSystem/README.md) | تخزين على الـ file system المحلي |
| [Headless.Blobs.Redis](src/Headless.Blobs.Redis/README.md) | تخزين جوه Redis |
| [Headless.Blobs.SshNet](src/Headless.Blobs.SshNet/README.md) | Provider لـ SFTP |

### Caching

Cache متعدد الـ tiers خلف abstraction واحدة: in-memory، وRedis، وhybrid L1/L2.

| Package | الوصف |
|---------|-------|
| [Headless.Caching.Abstractions](src/Headless.Caching.Abstractions/README.md) | الـ interfaces بتاعة الـ caching |
| [Headless.Caching.Core](src/Headless.Caching.Core/README.md) | Orchestration مشتركة قايمة على الـ factories |
| [Headless.Caching.Hybrid](src/Headless.Caching.Hybrid/README.md) | Hybrid cache بـ L1/L2 |
| [Headless.Caching.InMemory](src/Headless.Caching.InMemory/README.md) | Cache جوه الـ process |
| [Headless.Caching.Redis](src/Headless.Caching.Redis/README.md) | Cache فوق Redis |
| [Headless.Caching.Bcl](src/Headless.Caching.Bcl/README.md) | Adapter بيعرض الـ Headless cache كـ `IDistributedCache` |
| [Headless.Caching.DistributedLocks](src/Headless.Caching.DistributedLocks/README.md) | حماية من الـ cache stampede بـ distributed locks |
| [Headless.Caching.OutputCache](src/Headless.Caching.OutputCache/README.md) | بيخلي الـ ASP.NET Core output caching يشتغل على Headless cache |

### Captcha

تحقّق من الـ CAPTCHA tokens خلف abstraction واحدة بتنجح أو تفشل. ركّب Google reCAPTCHA v2/v3 و Cloudflare Turnstile من نفس الـ builder.

| Package | الوصف |
|---------|-------|
| [Headless.Captcha.Abstractions](src/Headless.Captcha.Abstractions/README.md) | الـ interfaces بتاعة الـ CAPTCHA verification والـ builder |
| [Headless.Captcha.Core](src/Headless.Captcha.Core/README.md) | الـ setup وpipeline الـ validation |
| [Headless.Captcha.ReCaptcha](src/Headless.Captcha.ReCaptcha/README.md) | Provider لـ Google reCAPTCHA v2/v3 |
| [Headless.Captcha.Turnstile](src/Headless.Captcha.Turnstile/README.md) | Provider لـ Cloudflare Turnstile |

### Email

إرسال الـ transactional والـ marketing emails عن طريق interface واحدة.

| Package | الوصف |
|---------|-------|
| [Headless.Emails.Abstractions](src/Headless.Emails.Abstractions/README.md) | الـ interfaces بتاعة إرسال الـ email |
| [Headless.Emails.Core](src/Headless.Emails.Core/README.md) | الـ setup builder وأدوات قايمة على MimeKit |
| [Headless.Emails.Aws](src/Headless.Emails.Aws/README.md) | Provider لـ AWS SES |
| [Headless.Emails.Azure](src/Headless.Emails.Azure/README.md) | Provider لـ Azure Communication Services |
| [Headless.Emails.Dev](src/Headless.Emails.Dev/README.md) | Dev provider مش بيبعت حاجة |
| [Headless.Emails.Mailkit](src/Headless.Emails.Mailkit/README.md) | إرسال SMTP عن طريق MailKit |

### Feature Management

Feature flags وقت الـ runtime مدعومة بـ storage دائم. بدّل الـ features من غير deployment جديد.

| Package | الوصف |
|---------|-------|
| [Headless.Features.Abstractions](src/Headless.Features.Abstractions/README.md) | الـ interfaces بتاعة الـ feature flags |
| [Headless.Features.Core](src/Headless.Features.Core/README.md) | الـ implementation بتاعة إدارة الـ features |
| [Headless.Features.Storage.EntityFramework](src/Headless.Features.Storage.EntityFramework/README.md) | Storage على EF Core |
| [Headless.Features.Storage.PostgreSql](src/Headless.Features.Storage.PostgreSql/README.md) | Storage خام في PostgreSQL |
| [Headless.Features.Storage.SqlServer](src/Headless.Features.Storage.SqlServer/README.md) | Storage خام في SQL Server |

### Identity

Persistence وstorage extensions لـ ASP.NET Core Identity، مبنية على EF Core.

| Package | الوصف |
|---------|-------|
| [Headless.Identity.Storage.EntityFramework](src/Headless.Identity.Storage.EntityFramework/README.md) | Storage لـ ASP.NET Core Identity على EF Core |

### Imaging

Image processing بـ backends قابلة للتبديل: resize، وcrop، وconvert، وoptimize.

| Package | الوصف |
|---------|-------|
| [Headless.Imaging.Abstractions](src/Headless.Imaging.Abstractions/README.md) | الـ interfaces بتاعة الـ image processing |
| [Headless.Imaging.Core](src/Headless.Imaging.Core/README.md) | الـ image processing الأساسي |
| [Headless.Imaging.ImageSharp](src/Headless.Imaging.ImageSharp/README.md) | Implementation على ImageSharp |

### Logging

أدوات وenrichers للـ structured logging مبنية على Serilog.

| Package | الوصف |
|---------|-------|
| [Headless.Logging.Serilog](src/Headless.Logging.Serilog/README.md) | أدوات logging على Serilog |

### Media

Content indexing واستخراج الـ metadata من ملفات الـ media والـ documents.

| Package | الوصف |
|---------|-------|
| [Headless.Media.Indexing.Abstractions](src/Headless.Media.Indexing.Abstractions/README.md) | الـ interfaces بتاعة الـ media indexing |
| [Headless.Media.Indexing](src/Headless.Media.Indexing/README.md) | الـ implementation بتاعة الـ media indexing |

### Messaging

Message bus موزّع بـ transactional outbox، وretries، وdelayed delivery، وconsumers type-safe. 8 transports و3 storage backends.

| Package | الوصف |
|---------|-------|
| [Headless.Messaging.Abstractions](src/Headless.Messaging.Abstractions/README.md) | الـ interfaces والـ contracts الأساسية بتاعة الـ messaging |
| [Headless.Messaging.Bus.Abstractions](src/Headless.Messaging.Bus.Abstractions/README.md) | الـ contracts بتاعة الـ publisher بنمط pub/sub |
| [Headless.Messaging.Queue.Abstractions](src/Headless.Messaging.Queue.Abstractions/README.md) | الـ contracts بتاعة الـ publisher بنمط point-to-point |
| [Headless.Messaging.Core](src/Headless.Messaging.Core/README.md) | الـ runtime engine: outbox، وretries، وdelayed delivery، وorchestration للـ consumers |
| [Headless.Messaging.Dashboard](src/Headless.Messaging.Dashboard/README.md) | Web UI لمتابعة الـ messages والـ failures وحالة النظام |
| [Headless.Messaging.Dashboard.K8s](src/Headless.Messaging.Dashboard.K8s/README.md) | اكتشاف الـ nodes أوتوماتيك جوه Kubernetes |
| [Headless.Messaging.Testing](src/Headless.Messaging.Testing/README.md) | Test harness جوه الـ process للتحقق من الـ published والـ consumed والـ faulted messages |

**الـ Transports:**

| Package | الوصف |
|---------|-------|
| [Headless.Messaging.RabbitMq](src/Headless.Messaging.RabbitMq/README.md) | RabbitMQ (AMQP) |
| [Headless.Messaging.Kafka](src/Headless.Messaging.Kafka/README.md) | Apache Kafka |
| [Headless.Messaging.Aws](src/Headless.Messaging.Aws/README.md) | AWS SQS + SNS |
| [Headless.Messaging.AzureServiceBus](src/Headless.Messaging.AzureServiceBus/README.md) | Azure Service Bus |
| [Headless.Messaging.Nats](src/Headless.Messaging.Nats/README.md) | NATS مع JetStream |
| [Headless.Messaging.Pulsar](src/Headless.Messaging.Pulsar/README.md) | Apache Pulsar |
| [Headless.Messaging.Redis](src/Headless.Messaging.Redis/README.md) | Redis Streams للـ queues و Redis Pub/Sub للـ broadcast |
| [Headless.Messaging.InMemory](src/Headless.Messaging.InMemory/README.md) | In-memory (للتطوير والـ tests) |

**الـ Storage backends:**

| Package | الوصف |
|---------|-------|
| [Headless.Messaging.Storage.PostgreSql](src/Headless.Messaging.Storage.PostgreSql/README.md) | تخزين الـ messages في PostgreSQL |
| [Headless.Messaging.Storage.PostgreSql.EntityFramework](src/Headless.Messaging.Storage.PostgreSql.EntityFramework/README.md) | بيربط تخزين PostgreSQL بـ EF Core context وبالـ transactional outbox |
| [Headless.Messaging.Storage.SqlServer](src/Headless.Messaging.Storage.SqlServer/README.md) | تخزين الـ messages في SQL Server |
| [Headless.Messaging.Storage.SqlServer.EntityFramework](src/Headless.Messaging.Storage.SqlServer.EntityFramework/README.md) | بيربط تخزين SQL Server بـ EF Core context وبالـ transactional outbox |
| [Headless.Messaging.Storage.InMemory](src/Headless.Messaging.Storage.InMemory/README.md) | Storage مؤقت (للتطوير والـ tests) |

### Jobs

جدولة background jobs موزّعة بـ cron expressions، وتنفيذ مؤجل، وdashboard للمتابعة، وobservability عن طريق OpenTelemetry. تسجيل الـ jobs بيتولّد وقت الـ compile.

| Package | الوصف |
|---------|-------|
| [Headless.Jobs.Abstractions](src/Headless.Jobs.Abstractions/README.md) | الـ interfaces بتاعة الـ job scheduling |
| [Headless.Jobs.Core](src/Headless.Jobs.Core/README.md) | الـ job engine: cron، وdelays، وretries، وmonitoring |
| [Headless.Jobs.SourceGenerator](src/Headless.Jobs.SourceGenerator/README.md) | توليد كود وقت الـ compile للـ methods المعلّمة بـ `[JobFunction]` |
| [Headless.Jobs.Dashboard](src/Headless.Jobs.Dashboard/README.md) | Web UI لمتابعة الـ jobs |
| [Headless.Jobs.EntityFramework](src/Headless.Jobs.EntityFramework/README.md) | تخزين حالة الـ jobs على EF Core؛ بيستخدم `Headless.Caching.ICache` اختيارياً لـ caching الـ cron expressions |
| [Headless.Jobs.EntityFramework.PostgreSql](src/Headless.Jobs.EntityFramework.PostgreSql/README.md) | Atomic claims في PostgreSQL بـ `FOR UPDATE SKIP LOCKED` |
| [Headless.Jobs.EntityFramework.SqlServer](src/Headless.Jobs.EntityFramework.SqlServer/README.md) | Atomic claims في SQL Server بـ `UPDLOCK` و `READPAST` و `ROWLOCK` |

### OpenAPI

توليد الـ spec وواجهات توثيق تفاعلية.

| Package | الوصف |
|---------|-------|
| [Headless.OpenApi.Nswag](src/Headless.OpenApi.Nswag/README.md) | توليد OpenAPI عن طريق NSwag |
| [Headless.OpenApi.Nswag.OData](src/Headless.OpenApi.Nswag.OData/README.md) | دعم OData في NSwag |
| [Headless.OpenApi.Scalar](src/Headless.OpenApi.Scalar/README.md) | Scalar كواجهة لعرض التوثيق |

### ORM

الوصول للـ database عن طريق Entity Framework Core و Couchbase: conventions، وseed data، وsoft deletes، ودعم multi-tenancy.

| Package | الوصف |
|---------|-------|
| [Headless.EntityFramework](src/Headless.EntityFramework/README.md) | أدوات لـ Entity Framework Core |
| [Headless.EntityFramework.Core](src/Headless.EntityFramework.Core/README.md) | EF converters وprimitive mappings وquery helpers من غير `HeadlessDbContext` |
| [Headless.EntityFramework.Messaging](src/Headless.EntityFramework.Messaging/README.md) | Outbox dispatcher للـ EF Core: كتابة الـ integration events بشكل atomic مع الـ save |
| [Headless.Couchbase](src/Headless.Couchbase/README.md) | أدوات للوصول لبيانات Couchbase |

### Payments

تكاملات payment gateways لمنطقة الشرق الأوسط: cash-in وcash-out عن طريق Paymob.

| Package | الوصف |
|---------|-------|
| [Headless.Payments.Paymob.CashIn](src/Headless.Payments.Paymob.CashIn/README.md) | تحصيل المدفوعات عن طريق Paymob |
| [Headless.Payments.Paymob.CashOut](src/Headless.Payments.Paymob.CashOut/README.md) | صرف المدفوعات عن طريق Paymob |
| [Headless.Payments.Paymob.Services](src/Headless.Payments.Paymob.Services/README.md) | خدمات Paymob المشتركة |

### Permissions

Permission system مدعوم بـ database. عرّف الـ permissions في الكود، وخزّن الـ assignments، واستعلم عن الـ access control وقت الـ runtime.

| Package | الوصف |
|---------|-------|
| [Headless.Permissions.Abstractions](src/Headless.Permissions.Abstractions/README.md) | الـ interfaces بتاعة الـ permission system |
| [Headless.Permissions.Core](src/Headless.Permissions.Core/README.md) | الـ implementation بتاعة الـ permission system |
| [Headless.Permissions.Testing](src/Headless.Permissions.Testing/README.md) | Doubles للـ tests بترجّع allow دايماً |
| [Headless.Permissions.Storage.EntityFramework](src/Headless.Permissions.Storage.EntityFramework/README.md) | تخزين الـ permissions على EF Core |
| [Headless.Permissions.Storage.PostgreSql](src/Headless.Permissions.Storage.PostgreSql/README.md) | تخزين الـ permissions في PostgreSQL |
| [Headless.Permissions.Storage.SqlServer](src/Headless.Permissions.Storage.SqlServer/README.md) | تخزين الـ permissions في SQL Server |

### Push Notifications

Firebase Cloud Messaging خلف abstraction واضحة، ومعاه dev provider مش بيبعت حاجة.

| Package | الوصف |
|---------|-------|
| [Headless.PushNotifications.Abstractions](src/Headless.PushNotifications.Abstractions/README.md) | الـ interfaces بتاعة الـ push notifications |
| [Headless.PushNotifications.Core](src/Headless.PushNotifications.Core/README.md) | Setup builder للـ services المسماة |
| [Headless.PushNotifications.Dev](src/Headless.PushNotifications.Dev/README.md) | Dev provider للـ push |
| [Headless.PushNotifications.Firebase](src/Headless.PushNotifications.Firebase/README.md) | Provider لـ Firebase Cloud Messaging |

### Distributed Locking

تنسيق الوصول للـ resources المشتركة بين الخدمات الموزّعة.

| Package | الوصف |
|---------|-------|
| [Headless.DistributedLocks.Abstractions](src/Headless.DistributedLocks.Abstractions/README.md) | الـ interfaces بتاعة الـ distributed locking |
| [Headless.DistributedLocks.Core](src/Headless.DistributedLocks.Core/README.md) | الـ implementation بتاعة الـ locking |
| [Headless.DistributedLocks.Core.Database](src/Headless.DistributedLocks.Core.Database/README.md) | أساس relational مشترك لـ providers الـ database |
| [Headless.DistributedLocks.InMemory](src/Headless.DistributedLocks.InMemory/README.md) | Locking جوه نفس الـ process |
| [Headless.DistributedLocks.PostgreSql](src/Headless.DistributedLocks.PostgreSql/README.md) | Locking بـ advisory locks في PostgreSQL |
| [Headless.DistributedLocks.Redis](src/Headless.DistributedLocks.Redis/README.md) | Locking على Redis |
| [Headless.DistributedLocks.SqlServer](src/Headless.DistributedLocks.SqlServer/README.md) | Locking بـ application locks في SQL Server |

### Coordination

Cluster membership وliveness tracking: اعرف مين من الـ nodes شغّال في deployment موزّع.

| Package | الوصف |
|---------|-------|
| [Headless.Coordination.Abstractions](src/Headless.Coordination.Abstractions/README.md) | الـ contracts بتاعة الـ membership والـ liveness والـ lifecycle |
| [Headless.Coordination.Core](src/Headless.Coordination.Core/README.md) | Membership engine من غير provider محدد |
| [Headless.Coordination.Core.Database](src/Headless.Coordination.Core.Database/README.md) | أساس relational مشترك لـ providers الـ SQL |
| [Headless.Coordination.PostgreSql](src/Headless.Coordination.PostgreSql/README.md) | Membership في PostgreSQL بـ liveness من ساعة الـ server |
| [Headless.Coordination.Redis](src/Headless.Coordination.Redis/README.md) | Membership على Redis عن طريق Lua وساعة Redis |
| [Headless.Coordination.SqlServer](src/Headless.Coordination.SqlServer/README.md) | Membership في SQL Server بـ writes محروسة |

### Unit of Work

Unit of work صريح وscoped: تبدأه في السطر اللي تختاره، وتعمل شغل الـ business، وتكمّله. شغل الـ outbox والـ durable jobs المنضمّين جواه بيتصرّف بشكل atomic عند الـ commit ويتلغي عند الـ rollback.

| Package | الوصف |
|---------|-------|
| [Headless.UnitOfWork.Abstractions](src/Headless.UnitOfWork.Abstractions/README.md) | الـ contracts بتاعة الـ scoped unit of work: `IUnitOfWorkManager`، `IUnitOfWork`، `IUnitOfWorkResource`، `TransactionEnlistment` (من غير dependencies) |
| [Headless.UnitOfWork](src/Headless.UnitOfWork/README.md) | الـ manager والـ engine وتسجيل `AddUnitOfWork()` |
| [Headless.UnitOfWork.EntityFramework](src/Headless.UnitOfWork.EntityFramework/README.md) | Provider للـ EF Core: `BeginAsync(db)` / `Enlist(db, tx)` / `RunAsync(db, ...)` |
| [Headless.UnitOfWork.PostgreSql](src/Headless.UnitOfWork.PostgreSql/README.md) | Provider لـ `NpgsqlConnection` بالـ ADO الخام، بنفس الشكل |
| [Headless.UnitOfWork.SqlServer](src/Headless.UnitOfWork.SqlServer/README.md) | Provider لـ `SqlConnection` بالـ ADO الخام، بنفس الشكل |

### Serialization

Interface واحدة للـ JSON APIs وللـ binary wire formats.

| Package | الوصف |
|---------|-------|
| [Headless.Serializer.Abstractions](src/Headless.Serializer.Abstractions/README.md) | الـ interfaces بتاعة الـ serialization |
| [Headless.Serializer.Json](src/Headless.Serializer.Json/README.md) | Serializer على System.Text.Json |
| [Headless.Serializer.MessagePack](src/Headless.Serializer.MessagePack/README.md) | Serializer على MessagePack |

### Settings

Application settings ديناميكية متخزّنة في database. غيّر الـ configuration وقت الـ runtime، مع caching وchange notification.

| Package | الوصف |
|---------|-------|
| [Headless.Settings.Abstractions](src/Headless.Settings.Abstractions/README.md) | الـ interfaces بتاعة الـ dynamic settings |
| [Headless.Settings.Core](src/Headless.Settings.Core/README.md) | الـ implementation بتاعة إدارة الـ settings |
| [Headless.Settings.Storage.EntityFramework](src/Headless.Settings.Storage.EntityFramework/README.md) | تخزين الـ settings على EF Core |
| [Headless.Settings.Storage.PostgreSql](src/Headless.Settings.Storage.PostgreSql/README.md) | تخزين الـ settings في PostgreSQL |
| [Headless.Settings.Storage.SqlServer](src/Headless.Settings.Storage.SqlServer/README.md) | تخزين الـ settings في SQL Server |

### SMS

إرسال SMS عن طريق interface واحدة، بـ providers إقليمية وعالمية.

| Package | الوصف |
|---------|-------|
| [Headless.Sms.Abstractions](src/Headless.Sms.Abstractions/README.md) | الـ interfaces بتاعة إرسال الـ SMS |
| [Headless.Sms.Core](src/Headless.Sms.Core/README.md) | الـ setup builder واختيار الـ provider |
| [Headless.Sms.Aws](src/Headless.Sms.Aws/README.md) | Provider لـ AWS SNS |
| [Headless.Sms.Cequens](src/Headless.Sms.Cequens/README.md) | Provider لـ Cequens |
| [Headless.Sms.Connekio](src/Headless.Sms.Connekio/README.md) | Provider لـ Connekio |
| [Headless.Sms.Dev](src/Headless.Sms.Dev/README.md) | Dev provider مش بيبعت حاجة |
| [Headless.Sms.Infobip](src/Headless.Sms.Infobip/README.md) | Provider لـ Infobip |
| [Headless.Sms.Twilio](src/Headless.Sms.Twilio/README.md) | Provider لـ Twilio |
| [Headless.Sms.VictoryLink](src/Headless.Sms.VictoryLink/README.md) | Provider لـ VictoryLink |
| [Headless.Sms.Vodafone](src/Headless.Sms.Vodafone/README.md) | Provider لـ Vodafone |

### SQL

Connection factories للوصول لـ SQL خام لما تحتاج تنزل تحت الـ ORM.

| Package | الوصف |
|---------|-------|
| [Headless.Sql.Abstractions](src/Headless.Sql.Abstractions/README.md) | الـ interfaces بتاعة الـ SQL connections |
| [Headless.Sql.Core](src/Headless.Sql.Core/README.md) | Implementation للـ current connection على مستوى الـ scope |
| [Headless.Sql.PostgreSql](src/Headless.Sql.PostgreSql/README.md) | Connection factory لـ PostgreSQL |
| [Headless.Sql.SqlServer](src/Headless.Sql.SqlServer/README.md) | Connection factory لـ SQL Server |
| [Headless.Sql.Sqlite](src/Headless.Sql.Sqlite/README.md) | Connection factory لـ SQLite |

### Testing

Base classes وbuilders وfixtures وتكامل Testcontainers لـ integration tests على databases حقيقية.

| Package | الوصف |
|---------|-------|
| [Headless.Testing](src/Headless.Testing/README.md) | أدوات وbase classes للـ tests |
| [Headless.Testing.AspNetCore](src/Headless.Testing.AspNetCore/README.md) | Integration-test server لـ ASP.NET Core مع تحكم في الوقت وreset للـ database |
| [Headless.Testing.Testcontainers](src/Headless.Testing.Testcontainers/README.md) | Fixtures على Testcontainers |

### TUS (Resumable Uploads)

دعم [بروتوكول TUS](https://tus.io) للـ resumable uploads، مع Azure Blob Storage وdistributed locking.

| Package | الوصف |
|---------|-------|
| [Headless.Tus](src/Headless.Tus/README.md) | أدوات بروتوكول TUS |
| [Headless.Tus.Azure](src/Headless.Tus.Azure/README.md) | TUS store على Azure Blob |
| [Headless.Tus.DistributedLocks](src/Headless.Tus.DistributedLocks/README.md) | Locking لملفات الـ TUS |

### Utilities

أدوات عامة مش تابعة لـ domain واحد.

| Package | الوصف |
|---------|-------|
| [Headless.Dashboard.Authentication](src/Headless.Dashboard.Authentication/README.md) | Authentication مشتركة للـ dashboards بتاعة الـ Jobs والـ Messaging |
| [Headless.FluentValidation](src/Headless.FluentValidation/README.md) | Extensions لـ FluentValidation |
| [Headless.Generator.Primitives](src/Headless.Generator.Primitives/README.md) | Source generator للـ primitive types |
| [Headless.Generator.Primitives.Abstractions](src/Headless.Generator.Primitives.Abstractions/README.md) | الـ abstractions بتاعة الـ generator |
| [Headless.Hosting](src/Headless.Hosting/README.md) | أدوات للـ .NET hosting |
| [Headless.NetTopologySuite](src/Headless.NetTopologySuite/README.md) | أدوات geospatial |
| [Headless.Primitives](src/Headless.Primitives/README.md) | Value objects، والـ result pattern، وpaging models، وdomain primitives |
| [Headless.Redis](src/Headless.Redis/README.md) | أدوات لـ Redis |
| [Headless.Sitemaps](src/Headless.Sitemaps/README.md) | توليد XML sitemaps |
| [Headless.Slugs](src/Headless.Slugs/README.md) | توليد الـ URL slugs |
| [Headless.Urls](src/Headless.Urls/README.md) | Fluent URL builder وparser |

</details>

<div dir="rtl" align="right">

القايمة المرجعية للـ packages موجودة في [`eng/expected-packages.txt`](eng/expected-packages.txt)، معرّف واحد لكل packable project.

## المساهمة

المساهمات مرحّب بيها: issues، أو feature requests، أو pull requests. اقرا الأول الـ README بتاع الـ package اللي بتشتغل عليها؛ كل واحدة بتوضّح الـ dependencies والـ side effects وحدود الـ provider.

</div>
