<p align="center">
  <img src="assets/banner.svg" alt=".NET Headless Framework" width="100%">
</p>

<div dir="rtl" align="right">

# .NET Headless Framework

</div>

<div align="center" dir="rtl">

**Framework خفيف ومقسّم لـ .NET: يخليك تبني بطريقتك، من غير ما يحبسك في تصميم أو مزوّد معيّن.**

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![GitHub Stars](https://img.shields.io/github/stars/xshaheen/headless-framework?style=social)](https://github.com/xshaheen/headless-framework)
[![English](https://img.shields.io/badge/lang-English-2563EB?style=flat-square)](README.md)

أكثر من 150 حزمة NuGet &bull; ابدأ من العقود &bull; اختَر المزوّد المناسب &bull; بدّل البنية التحتية من غير ما تلمس كود التطبيق

[ابدأ من هنا](#ابدأ-من-هنا) &bull; [طريقة تنظيم الحزم](#طريقة-تنظيم-الحزم) &bull; [اختيار الحزم حسب المهمة](#اختيار-الحزم-حسب-المهمة) &bull; [البدء السريع](#البدء-السريع) &bull; [فهرس الحزم](#فهرس-الحزم)

</div>

---

<div dir="rtl" align="right">

## ابدأ من هنا

Headless Framework هو مجموعة حزم عملية لخدمات .NET وواجهات الـ API. الفكرة ببساطة: خُد العقود والتجهيزات المتكررة التي تحتاجها في أغلب المشاريع، وسيب اختيار Redis أو SQL Server أو PostgreSQL أو RabbitMQ أو Azure أو غيره لطبقة التشغيل، مش لكود البزنس.

بدل ما تربط الدومين عندك بمزوّد معيّن، تبدأ من حزم `*.Abstractions`. كود التطبيق يتعامل مع interfaces واضحة، والمضيف هو اللي يقرر التنفيذ الفعلي من خلال حزم `*.Core` والمزوّدات.

يعني لو بكرة غيّرت التخزين من FileSystem إلى Azure Blob، أو نقلت الـ cache من InMemory إلى Redis، أو بدّلت الـ messaging broker، المفروض التغيير يحصل في نقطة التركيب، مش في قلب التطبيق.

استخدم Headless لما مشروعك يحتاج واحد أو أكثر من الحاجات دي:

- تجهيزات API جاهزة: Problem Details، health/liveness endpoints، OpenTelemetry، OpenAPI، forwarded headers، compression، والتحقق وقت التشغيل.
- طبقة عقود للبنية التحتية: cache، blob storage، SQL access، settings، audit log، permissions، و feature flags.
- أدوات للتشغيل الموزّع: background jobs، messaging، distributed locks، node coordination، commit coordination، ولوحات مراقبة.
- تكاملات جاهزة: emails، SMS، push notifications، CAPTCHA، image processing، media indexing، payments، TUS uploads، و serialization.
- دعم للاختبارات: in-memory providers، dev providers، ASP.NET Core test hosting، و Testcontainers setup.

المشروع مش template كامل لتطبيق، ومش package واحدة تحطها وخلاص. هو toolbox منظم: اختَر العائلة المناسبة، خلّي كودك يعتمد على العقود، واربط التنفيذ الحقيقي في الـ host.

## طريقة تنظيم الحزم

معظم الميزات في Headless ماشية بنفس الشكل:

</div>

```text
Headless.<Feature>.Abstractions  -> العقود التي يعتمد عليها كود التطبيق
Headless.<Feature>.Core          -> منطق التشغيل والتهيئة بدون مزوّد محدد
Headless.<Feature>.<Provider>    -> التكامل الحقيقي مع مزوّد أو نظام معين
Headless.<Feature>.Testing       -> أدوات اختبار عند الحاجة
```

<div dir="rtl" align="right">

الهدف من التقسيمة دي إن قرار البنية التحتية يفضل خارج كود الأعمال:

- كود التطبيق يتعامل مع عقود زي `ICache`, `IBlobStorage`, `IEmailSender`, `IDistributedLock`, `ISettingManager`، مديري المهام، أو ناشري الرسائل.
- حزم المزوّدات تضيف extension methods زي `UseRedis`, `UsePostgreSql`, `UseFileSystem`, `UseAws`, و `UseAzure`.
- كل خدمة تسجّل فقط الحزم والمزوّدات التي تحتاجها فعلاً.
- مزوّدات التطوير والاختبار ممتازة محلياً، لكن في الإنتاج لازم تختبر على نفس نوع المزوّد الحقيقي، خصوصاً في الاستمرارية، المعاملات، الترتيب، الأقفال، وحدود التشغيل.

## اختيار الحزم حسب المهمة

| المهمة | ابدأ بـ | أضف عند الحاجة |
|-------|---------|----------------|
| تجهيزات API والمضيف | `Headless.Api.Abstractions` | `Headless.Api.Core` أو `Headless.Api.ServiceDefaults` لو عايز تشغيل API كامل |
| التخزين المؤقت | `Headless.Caching.Abstractions` | `Headless.Caching.Core` مع InMemory أو Redis أو Hybrid cache |
| تخزين الملفات | `Headless.Blobs.Abstractions` | `Headless.Blobs.Core` مع Azure أو AWS أو Cloudflare R2 أو FileSystem أو Redis أو SFTP |
| المهام الخلفية | `Headless.Jobs.Abstractions` | `Headless.Jobs.Core`, `Headless.Jobs.SourceGenerator`, Dashboard، OpenTelemetry، أو تخزين دائم عبر EF Core |
| الأقفال الموزّعة | `Headless.DistributedLocks.Abstractions` | `Headless.DistributedLocks.Core` مع InMemory أو Redis أو PostgreSQL أو SQL Server |
| عضوية العُقد وحالتها | `Headless.Coordination.Abstractions` | `Headless.Coordination.Core` مع Redis أو PostgreSQL أو SQL Server |
| ربط الآثار الجانبية بالمعاملة | `Headless.CommitCoordination.Abstractions` | `Headless.CommitCoordination.Core` مع EF Core أو PostgreSQL أو SQL Server أو InMemory أو Durable Work |
| الإعدادات الديناميكية | `Headless.Settings.Abstractions` | `Headless.Settings.Core` مع تخزين EF Core أو PostgreSQL أو SQL Server |
| Feature flags | `Headless.Features.Abstractions` | `Headless.Features.Core` مع تخزين EF Core أو PostgreSQL أو SQL Server |
| الصلاحيات | `Headless.Permissions.Abstractions` | `Headless.Permissions.Core` مع تخزين EF Core أو PostgreSQL أو SQL Server أو مزوّد اختبار |
| سجلات التدقيق | `Headless.AuditLog.Abstractions` | `Headless.AuditLog.Core` مع تخزين EF Core أو PostgreSQL أو SQL Server |
| البريد الإلكتروني | `Headless.Emails.Abstractions` | `Headless.Emails.Core` مع AWS SES أو Azure Communication Services أو MailKit SMTP أو مزوّد تطوير |
| الرسائل النصية SMS | `Headless.Sms.Abstractions` | `Headless.Sms.Core` مع AWS أو Cequens أو Connekio أو Infobip أو Twilio أو VictoryLink أو Vodafone أو مزوّد تطوير |
| الإشعارات الفورية | `Headless.PushNotifications.Abstractions` | `Headless.PushNotifications.Core` مع Firebase أو مزوّد تطوير |
| الرسائل بين الخدمات | `Headless.Messaging.Abstractions` | `Headless.Messaging.Core`، عقود bus/queue، broker، تخزين دائم، Dashboard، أدوات اختبار، و OpenTelemetry |
| الاختبارات فقط | حزمة Abstractions الخاصة بالمجال | InMemory provider أو Dev provider أو حزمة Testing |

## البدء السريع

### تشغيل مضيف API

</div>

```bash
dotnet add package Headless.Api.ServiceDefaults
```

```csharp
var builder = WebApplication.CreateBuilder(args);

// يضيف OpenTelemetry و OpenAPI و Problem Details و JSON و health checks
// و forwarded headers و compression و exception handling ونقاط Headless.
builder.AddHeadless();

var app = builder.Build();

// يطبق ترتيب middlewares الخاص بـ Headless:
// forwarded headers ثم compression ثم معالجة status codes و exceptions و HTTPS/HSTS.
app.UseHeadless();

// يربط نقاط التشغيل مثل health و liveness و OpenAPI JSON
// و static web assets عندما تكون مفعلة.
app.MapHeadlessEndpoints();

app.Run();
```

<div dir="rtl" align="right">

### إضافة Cache

لو الكود عندك محتاج يستهلك cache فقط، ابدأ بـ `Headless.Caching.Abstractions`. أما الـ host الذي يشغّل الخدمة فيضيف `Core` والمزوّد المناسب:

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

### إضافة Blob Storage

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

### أضف Messaging فقط عندما تحتاجها

Messaging في Headless مجرد عائلة من عائلات الإطار، مش قلب المشروع كله. استخدمها لما الخدمة تحتاج publish/consume بين processes مختلفة، أو queues، أو outbox، أو delayed delivery، أو retries محفوظة.

ابدأ من:

- [`docs/llms/messaging.md`](docs/llms/messaging.md) لفهم النموذج التشغيلي.
- [`demo/Headless.Messaging.Console.Demo`](demo/Headless.Messaging.Console.Demo) لتجربة محلية باستخدام InMemory.
- [`demo/Headless.Messaging.RabbitMq.SqlServer.Demo`](demo/Headless.Messaging.RabbitMq.SqlServer.Demo) أو [`demo/Headless.Messaging.Kafka.PostgreSql.Demo`](demo/Headless.Messaging.Kafka.PostgreSql.Demo) لأمثلة durable أقرب للإنتاج.

## ملاحظات مهمة للإنتاج

- أي حالة لازم تعيش بعد restart استخدم لها durable provider.
- InMemory و Dev providers ممتازين للتطوير والاختبارات والعروض، لكن لا تبني عليهم افتراضات إنتاج.
- استخدم named instances لو الخدمة تتعامل مع أكثر من مخزن منطقي أو مرسل منطقي.
- خلي إعدادات المزوّد في نقطة التركيب، ولا تسرّب تفاصيله لكود البزنس.
- اقرأ README الخاص بكل package قبل استخدامها؛ كل واحدة توضح التبعيات، الآثار الجانبية، متطلبات الإعداد، وحدود المزوّد.
- اختبر بنفس مجموعة المزوّدات التي ستستخدمها في الإنتاج لو السلوك يعتمد على التخزين، المعاملات، الأقفال، ترتيب الرسائل، broker behavior، أو خصائص خدمة سحابية.

## الإصدارات والتوافق

- أغلب الحزم تستهدف `.NET 10`.
- حزم source generators تستهدف `netstandard2.0`.
- المستودع يثبت إصدار .NET SDK في [`global.json`](global.json).
- ملاحظات الإصدارات تنشر عبر [GitHub releases](https://github.com/xshaheen/headless-framework/releases).

## فهرس الحزم

### API & Web

| Package | الوصف |
|---------|-------|
| [Headless.Api.Core](src/Headless.Api.Core/README.md) | مكونات أساسية لبناء ASP.NET Core APIs |
| [Headless.Api.ServiceDefaults](src/Headless.Api.ServiceDefaults/README.md) | نقطة دخول جاهزة عبر `AddHeadless()` لتجهيزات المضيف الشائعة |
| [Headless.Api.Abstractions](src/Headless.Api.Abstractions/README.md) | عقود API المشتركة التي يعتمد عليها كود التطبيق |
| [Headless.Api.DataProtection](src/Headless.Api.DataProtection/README.md) | تخزين مفاتيح Data Protection بطريقة قابلة للاستبدال |
| [Headless.Api.FluentValidation](src/Headless.Api.FluentValidation/README.md) | ربط FluentValidation مع طبقة الـ API |
| [Headless.Api.Logging.Serilog](src/Headless.Api.Logging.Serilog/README.md) | تجهيز Serilog للتسجيل المنظم |
| [Headless.Api.MinimalApi](src/Headless.Api.MinimalApi/README.md) | أدوات مساعدة لمشاريع Minimal API |
| [Headless.Api.Mvc](src/Headless.Api.Mvc/README.md) | أدوات مساعدة لمشاريع MVC |
| [Headless.Api.Idempotency](src/Headless.Api.Idempotency/README.md) | Middleware للتكرار الآمن وإعادة استخدام الاستجابات |

### Core

| Package | الوصف |
|---------|-------|
| [Headless.Extensions](src/Headless.Extensions/README.md) | أدوات وامتدادات عامة مستخدمة عبر الحزم |
| [Headless.Core](src/Headless.Core/README.md) | أساسيات Domain-Driven Design داخل Headless |
| [Headless.Security.Abstractions](src/Headless.Security.Abstractions/README.md) | عقود وخيارات الأمان |
| [Headless.Security](src/Headless.Security/README.md) | تشفير النصوص و hashing |
| [Headless.Checks](src/Headless.Checks/README.md) | Guard clauses والتحقق من المدخلات |
| [Headless.Domain](src/Headless.Domain/README.md) | كيانات الدومين وأحداثه |
| [Headless.Domain.LocalEventBus](src/Headless.Domain.LocalEventBus/README.md) | `ILocalEventBus` للأحداث داخل نفس العملية |
| [Headless.Mediator](src/Headless.Mediator/README.md) | Pipeline behaviors حول mediator |
| [Headless.MultiTenancy](src/Headless.MultiTenancy/README.md) | تركيب سياق تعدد المستأجرين عبر حزم Headless |

### Audit Log

| Package | الوصف |
|---------|-------|
| [Headless.AuditLog.Abstractions](src/Headless.AuditLog.Abstractions/README.md) | عقود سجلات التدقيق |
| [Headless.AuditLog.Core](src/Headless.AuditLog.Core/README.md) | منطق وتهيئة audit log |
| [Headless.AuditLog.Storage.EntityFramework](src/Headless.AuditLog.Storage.EntityFramework/README.md) | تخزين audit log عبر EF Core |
| [Headless.AuditLog.Storage.PostgreSql](src/Headless.AuditLog.Storage.PostgreSql/README.md) | تخزين audit log في PostgreSQL |
| [Headless.AuditLog.Storage.SqlServer](src/Headless.AuditLog.Storage.SqlServer/README.md) | تخزين audit log في SQL Server |

### Blob Storage

| Package | الوصف |
|---------|-------|
| [Headless.Blobs.Abstractions](src/Headless.Blobs.Abstractions/README.md) | عقود تخزين الملفات |
| [Headless.Blobs.Core](src/Headless.Blobs.Core/README.md) | Builder لتجهيز المخزن الافتراضي والمخازن المسماة |
| [Headless.Blobs.Aws](src/Headless.Blobs.Aws/README.md) | مزوّد AWS S3 |
| [Headless.Blobs.Azure](src/Headless.Blobs.Azure/README.md) | مزوّد Azure Blob Storage |
| [Headless.Blobs.CloudflareR2](src/Headless.Blobs.CloudflareR2/README.md) | مزوّد Cloudflare R2 |
| [Headless.Blobs.FileSystem](src/Headless.Blobs.FileSystem/README.md) | تخزين الملفات على FileSystem |
| [Headless.Blobs.Redis](src/Headless.Blobs.Redis/README.md) | تخزين الملفات داخل Redis |
| [Headless.Blobs.SshNet](src/Headless.Blobs.SshNet/README.md) | مزوّد SFTP |

### Caching

| Package | الوصف |
|---------|-------|
| [Headless.Caching.Abstractions](src/Headless.Caching.Abstractions/README.md) | عقود التخزين المؤقت |
| [Headless.Caching.Core](src/Headless.Caching.Core/README.md) | منطق موحد لبناء cache factories |
| [Headless.Caching.Hybrid](src/Headless.Caching.Hybrid/README.md) | Cache هجين بطبقتي L1/L2 |
| [Headless.Caching.InMemory](src/Headless.Caching.InMemory/README.md) | Cache داخل الذاكرة |
| [Headless.Caching.Redis](src/Headless.Caching.Redis/README.md) | Cache فوق Redis |
| [Headless.Caching.Bcl](src/Headless.Caching.Bcl/README.md) | Adapter يعرض Headless cache كـ `IDistributedCache` |
| [Headless.Caching.DistributedLocks](src/Headless.Caching.DistributedLocks/README.md) | حماية من cache stampede باستخدام distributed locks |
| [Headless.Caching.OutputCache](src/Headless.Caching.OutputCache/README.md) | ربط ASP.NET Core output caching بـ Headless cache |

### Captcha

| Package | الوصف |
|---------|-------|
| [Headless.Captcha.Abstractions](src/Headless.Captcha.Abstractions/README.md) | عقود التحقق من CAPTCHA |
| [Headless.Captcha.Core](src/Headless.Captcha.Core/README.md) | منطق التهيئة والتحقق |
| [Headless.Captcha.ReCaptcha](src/Headless.Captcha.ReCaptcha/README.md) | مزوّد Google reCAPTCHA |
| [Headless.Captcha.Turnstile](src/Headless.Captcha.Turnstile/README.md) | مزوّد Cloudflare Turnstile |

### Email

| Package | الوصف |
|---------|-------|
| [Headless.Emails.Abstractions](src/Headless.Emails.Abstractions/README.md) | عقود إرسال البريد الإلكتروني |
| [Headless.Emails.Core](src/Headless.Emails.Core/README.md) | Builder للتهيئة وتحويل الطلبات إلى MimeKit |
| [Headless.Emails.Aws](src/Headless.Emails.Aws/README.md) | مزوّد AWS SES |
| [Headless.Emails.Azure](src/Headless.Emails.Azure/README.md) | مزوّد Azure Communication Services |
| [Headless.Emails.Dev](src/Headless.Emails.Dev/README.md) | مزوّد تطوير للبريد الإلكتروني |
| [Headless.Emails.Mailkit](src/Headless.Emails.Mailkit/README.md) | إرسال SMTP عبر MailKit |

### Feature Management

| Package | الوصف |
|---------|-------|
| [Headless.Features.Abstractions](src/Headless.Features.Abstractions/README.md) | عقود Feature Flags |
| [Headless.Features.Core](src/Headless.Features.Core/README.md) | منطق إدارة Feature Flags |
| [Headless.Features.Storage.EntityFramework](src/Headless.Features.Storage.EntityFramework/README.md) | تخزين عبر EF Core |
| [Headless.Features.Storage.PostgreSql](src/Headless.Features.Storage.PostgreSql/README.md) | تخزين في PostgreSQL |
| [Headless.Features.Storage.SqlServer](src/Headless.Features.Storage.SqlServer/README.md) | تخزين في SQL Server |

### Identity

| Package | الوصف |
|---------|-------|
| [Headless.Identity.Storage.EntityFramework](src/Headless.Identity.Storage.EntityFramework/README.md) | تخزين ASP.NET Core Identity عبر EF Core |

### Imaging

| Package | الوصف |
|---------|-------|
| [Headless.Imaging.Abstractions](src/Headless.Imaging.Abstractions/README.md) | عقود معالجة الصور |
| [Headless.Imaging.Core](src/Headless.Imaging.Core/README.md) | تهيئة خدمات معالجة الصور |
| [Headless.Imaging.ImageSharp](src/Headless.Imaging.ImageSharp/README.md) | مزوّد ImageSharp |

### Logging

| Package | الوصف |
|---------|-------|
| [Headless.Logging.Serilog](src/Headless.Logging.Serilog/README.md) | أدوات تسجيل مبنية على Serilog |

### Media

| Package | الوصف |
|---------|-------|
| [Headless.Media.Indexing.Abstractions](src/Headless.Media.Indexing.Abstractions/README.md) | عقود فهرسة الوسائط |
| [Headless.Media.Indexing](src/Headless.Media.Indexing/README.md) | منطق فهرسة الوسائط |

### Messaging

| Package | الوصف |
|---------|-------|
| [Headless.Messaging.Abstractions](src/Headless.Messaging.Abstractions/README.md) | العقود الأساسية للـ messaging |
| [Headless.Messaging.Bus.Abstractions](src/Headless.Messaging.Bus.Abstractions/README.md) | عقود النشر بنمط pub/sub |
| [Headless.Messaging.Queue.Abstractions](src/Headless.Messaging.Queue.Abstractions/README.md) | عقود الإرسال إلى queues |
| [Headless.Messaging.Core](src/Headless.Messaging.Core/README.md) | Outbox و retries و delayed delivery وتشغيل المستهلكين |
| [Headless.Messaging.Dashboard](src/Headless.Messaging.Dashboard/README.md) | Dashboard لمتابعة الرسائل والإخفاقات |
| [Headless.Messaging.Dashboard.K8s](src/Headless.Messaging.Dashboard.K8s/README.md) | اكتشاف العُقد داخل Kubernetes |
| [Headless.Messaging.OpenTelemetry](src/Headless.Messaging.OpenTelemetry/README.md) | Tracing و metrics وسياق التتبع |
| [Headless.Messaging.Testing](src/Headless.Messaging.Testing/README.md) | أدوات اختبار للرسائل المنشورة والمستهلكة والفاشلة |
| [Headless.Messaging.RabbitMq](src/Headless.Messaging.RabbitMq/README.md) | Broker عبر RabbitMQ |
| [Headless.Messaging.Kafka](src/Headless.Messaging.Kafka/README.md) | Broker عبر Kafka |
| [Headless.Messaging.Aws](src/Headless.Messaging.Aws/README.md) | Broker عبر AWS SQS/SNS |
| [Headless.Messaging.AzureServiceBus](src/Headless.Messaging.AzureServiceBus/README.md) | Broker عبر Azure Service Bus |
| [Headless.Messaging.Nats](src/Headless.Messaging.Nats/README.md) | Broker عبر NATS |
| [Headless.Messaging.Pulsar](src/Headless.Messaging.Pulsar/README.md) | Broker عبر Pulsar |
| [Headless.Messaging.Redis](src/Headless.Messaging.Redis/README.md) | Broker عبر Redis Streams و Pub/Sub |
| [Headless.Messaging.InMemory](src/Headless.Messaging.InMemory/README.md) | Broker داخل الذاكرة للتطوير والاختبارات |
| [Headless.Messaging.Storage.PostgreSql](src/Headless.Messaging.Storage.PostgreSql/README.md) | تخزين الرسائل في PostgreSQL |
| [Headless.Messaging.Storage.SqlServer](src/Headless.Messaging.Storage.SqlServer/README.md) | تخزين الرسائل في SQL Server |
| [Headless.Messaging.InMemoryStorage](src/Headless.Messaging.InMemoryStorage/README.md) | تخزين مؤقت داخل الذاكرة |

### Jobs

| Package | الوصف |
|---------|-------|
| [Headless.Jobs.Abstractions](src/Headless.Jobs.Abstractions/README.md) | عقود جدولة وتشغيل jobs |
| [Headless.Jobs.Core](src/Headless.Jobs.Core/README.md) | مشغّل الجدولة والتنفيذ |
| [Headless.Jobs.SourceGenerator](src/Headless.Jobs.SourceGenerator/README.md) | Source generator لتعريف job functions |
| [Headless.Jobs.Dashboard](src/Headless.Jobs.Dashboard/README.md) | Dashboard لمتابعة jobs |
| [Headless.Jobs.OpenTelemetry](src/Headless.Jobs.OpenTelemetry/README.md) | Tracing و metrics لتنفيذ jobs |
| [Headless.Jobs.EntityFramework](src/Headless.Jobs.EntityFramework/README.md) | تخزين حالة jobs عبر EF Core |

### OpenAPI

| Package | الوصف |
|---------|-------|
| [Headless.OpenApi.Nswag](src/Headless.OpenApi.Nswag/README.md) | توليد OpenAPI عبر NSwag |
| [Headless.OpenApi.Nswag.OData](src/Headless.OpenApi.Nswag.OData/README.md) | دعم OData في NSwag |
| [Headless.OpenApi.Scalar](src/Headless.OpenApi.Scalar/README.md) | واجهة Scalar لعرض التوثيق |

### ORM

| Package | الوصف |
|---------|-------|
| [Headless.Orm.EntityFramework](src/Headless.Orm.EntityFramework/README.md) | أدوات مساعدة لـ Entity Framework Core |
| [Headless.Orm.EntityFramework.Messaging](src/Headless.Orm.EntityFramework.Messaging/README.md) | ربط outbox بـ EF Core |
| [Headless.Orm.Couchbase](src/Headless.Orm.Couchbase/README.md) | أدوات مساعدة لـ Couchbase |

### Payments

| Package | الوصف |
|---------|-------|
| [Headless.Payments.Paymob.CashIn](src/Headless.Payments.Paymob.CashIn/README.md) | تحصيل مدفوعات Paymob |
| [Headless.Payments.Paymob.CashOut](src/Headless.Payments.Paymob.CashOut/README.md) | صرف مدفوعات Paymob |
| [Headless.Payments.Paymob.Services](src/Headless.Payments.Paymob.Services/README.md) | خدمات Paymob المشتركة |

### Permissions

| Package | الوصف |
|---------|-------|
| [Headless.Permissions.Abstractions](src/Headless.Permissions.Abstractions/README.md) | عقود الصلاحيات |
| [Headless.Permissions.Core](src/Headless.Permissions.Core/README.md) | منطق إدارة الصلاحيات |
| [Headless.Permissions.Testing](src/Headless.Permissions.Testing/README.md) | بدائل اختبارية للصلاحيات والتفويض |
| [Headless.Permissions.Storage.EntityFramework](src/Headless.Permissions.Storage.EntityFramework/README.md) | تخزين الصلاحيات عبر EF Core |
| [Headless.Permissions.Storage.PostgreSql](src/Headless.Permissions.Storage.PostgreSql/README.md) | تخزين الصلاحيات في PostgreSQL |
| [Headless.Permissions.Storage.SqlServer](src/Headless.Permissions.Storage.SqlServer/README.md) | تخزين الصلاحيات في SQL Server |

### Push Notifications

| Package | الوصف |
|---------|-------|
| [Headless.PushNotifications.Abstractions](src/Headless.PushNotifications.Abstractions/README.md) | عقود الإشعارات الفورية |
| [Headless.PushNotifications.Core](src/Headless.PushNotifications.Core/README.md) | Builder لتهيئة خدمات الإشعارات |
| [Headless.PushNotifications.Dev](src/Headless.PushNotifications.Dev/README.md) | مزوّد تطوير للإشعارات |
| [Headless.PushNotifications.Firebase](src/Headless.PushNotifications.Firebase/README.md) | مزوّد Firebase Cloud Messaging |

### Distributed Locking

| Package | الوصف |
|---------|-------|
| [Headless.DistributedLocks.Abstractions](src/Headless.DistributedLocks.Abstractions/README.md) | عقود الأقفال الموزّعة |
| [Headless.DistributedLocks.Core](src/Headless.DistributedLocks.Core/README.md) | منطق الأقفال والـ semaphores |
| [Headless.DistributedLocks.Core.Database](src/Headless.DistributedLocks.Core.Database/README.md) | أساس علائقي لمزوّدات الأقفال |
| [Headless.DistributedLocks.InMemory](src/Headless.DistributedLocks.InMemory/README.md) | مزوّد داخل نفس العملية |
| [Headless.DistributedLocks.PostgreSql](src/Headless.DistributedLocks.PostgreSql/README.md) | مزوّد PostgreSQL |
| [Headless.DistributedLocks.Redis](src/Headless.DistributedLocks.Redis/README.md) | مزوّد Redis |
| [Headless.DistributedLocks.SqlServer](src/Headless.DistributedLocks.SqlServer/README.md) | مزوّد SQL Server |

### Coordination

| Package | الوصف |
|---------|-------|
| [Headless.Coordination.Abstractions](src/Headless.Coordination.Abstractions/README.md) | عقود عضوية العُقد وحالتها |
| [Headless.Coordination.Core](src/Headless.Coordination.Core/README.md) | منطق عضوية العُقد |
| [Headless.Coordination.Core.Database](src/Headless.Coordination.Core.Database/README.md) | أساس علائقي لمزوّدات coordination |
| [Headless.Coordination.PostgreSql](src/Headless.Coordination.PostgreSql/README.md) | مزوّد PostgreSQL |
| [Headless.Coordination.Redis](src/Headless.Coordination.Redis/README.md) | مزوّد Redis |
| [Headless.Coordination.SqlServer](src/Headless.Coordination.SqlServer/README.md) | مزوّد SQL Server |

### Commit Coordination

| Package | الوصف |
|---------|-------|
| [Headless.CommitCoordination.Abstractions](src/Headless.CommitCoordination.Abstractions/README.md) | عقود commit coordination |
| [Headless.CommitCoordination.Core](src/Headless.CommitCoordination.Core/README.md) | Ambient scopes وربط الشغل بحدود المعاملة |
| [Headless.CommitCoordination.DurableWork](src/Headless.CommitCoordination.DurableWork/README.md) | Durable work stores تُكتب داخل نفس المعاملة |
| [Headless.CommitCoordination.EntityFramework](src/Headless.CommitCoordination.EntityFramework/README.md) | ربط EF Core بحدود commit/rollback |
| [Headless.CommitCoordination.InMemory](src/Headless.CommitCoordination.InMemory/README.md) | إشارات داخل نفس العملية |
| [Headless.CommitCoordination.PostgreSql](src/Headless.CommitCoordination.PostgreSql/README.md) | نقاط تسجيل PostgreSQL |
| [Headless.CommitCoordination.SqlServer](src/Headless.CommitCoordination.SqlServer/README.md) | إشارات commit/rollback في SQL Server |

### Serialization

| Package | الوصف |
|---------|-------|
| [Headless.Serializer.Abstractions](src/Headless.Serializer.Abstractions/README.md) | عقود serialization |
| [Headless.Serializer.Json](src/Headless.Serializer.Json/README.md) | مزوّد System.Text.Json |
| [Headless.Serializer.MessagePack](src/Headless.Serializer.MessagePack/README.md) | مزوّد MessagePack |

### Settings

| Package | الوصف |
|---------|-------|
| [Headless.Settings.Abstractions](src/Headless.Settings.Abstractions/README.md) | عقود الإعدادات الديناميكية |
| [Headless.Settings.Core](src/Headless.Settings.Core/README.md) | منطق إدارة الإعدادات |
| [Headless.Settings.Storage.EntityFramework](src/Headless.Settings.Storage.EntityFramework/README.md) | تخزين الإعدادات عبر EF Core |
| [Headless.Settings.Storage.PostgreSql](src/Headless.Settings.Storage.PostgreSql/README.md) | تخزين الإعدادات في PostgreSQL |
| [Headless.Settings.Storage.SqlServer](src/Headless.Settings.Storage.SqlServer/README.md) | تخزين الإعدادات في SQL Server |

### SMS

| Package | الوصف |
|---------|-------|
| [Headless.Sms.Abstractions](src/Headless.Sms.Abstractions/README.md) | عقود إرسال SMS |
| [Headless.Sms.Core](src/Headless.Sms.Core/README.md) | Builder لتهيئة مزوّدات SMS |
| [Headless.Sms.Aws](src/Headless.Sms.Aws/README.md) | مزوّد AWS SNS |
| [Headless.Sms.Cequens](src/Headless.Sms.Cequens/README.md) | مزوّد Cequens |
| [Headless.Sms.Connekio](src/Headless.Sms.Connekio/README.md) | مزوّد Connekio |
| [Headless.Sms.Dev](src/Headless.Sms.Dev/README.md) | مزوّد تطوير لـ SMS |
| [Headless.Sms.Infobip](src/Headless.Sms.Infobip/README.md) | مزوّد Infobip |
| [Headless.Sms.Twilio](src/Headless.Sms.Twilio/README.md) | مزوّد Twilio |
| [Headless.Sms.VictoryLink](src/Headless.Sms.VictoryLink/README.md) | مزوّد VictoryLink |
| [Headless.Sms.Vodafone](src/Headless.Sms.Vodafone/README.md) | مزوّد Vodafone |

### SQL

| Package | الوصف |
|---------|-------|
| [Headless.Sql.Abstractions](src/Headless.Sql.Abstractions/README.md) | عقود اتصالات SQL |
| [Headless.Sql.Core](src/Headless.Sql.Core/README.md) | إدارة الاتصال الحالي داخل النطاق |
| [Headless.Sql.PostgreSql](src/Headless.Sql.PostgreSql/README.md) | Factory لاتصالات PostgreSQL |
| [Headless.Sql.SqlServer](src/Headless.Sql.SqlServer/README.md) | Factory لاتصالات SQL Server |
| [Headless.Sql.Sqlite](src/Headless.Sql.Sqlite/README.md) | Factory لاتصالات SQLite |

### Testing

| Package | الوصف |
|---------|-------|
| [Headless.Testing](src/Headless.Testing/README.md) | أدوات اختبار مشتركة |
| [Headless.Testing.AspNetCore](src/Headless.Testing.AspNetCore/README.md) | Test host لاختبارات تكامل ASP.NET Core |
| [Headless.Testing.Testcontainers](src/Headless.Testing.Testcontainers/README.md) | تجهيزات اختبار مبنية على Testcontainers |

### TUS

| Package | الوصف |
|---------|-------|
| [Headless.Tus](src/Headless.Tus/README.md) | دعم بروتوكول TUS للرفع القابل للاستكمال |
| [Headless.Tus.Azure](src/Headless.Tus.Azure/README.md) | تخزين TUS على Azure Blob |
| [Headless.Tus.DistributedLocks](src/Headless.Tus.DistributedLocks/README.md) | أقفال لحماية عمليات TUS |

### Utilities

| Package | الوصف |
|---------|-------|
| [Headless.Dashboard.Authentication](src/Headless.Dashboard.Authentication/README.md) | مصادقة مشتركة للوحات Jobs و Messaging |
| [Headless.FluentValidation](src/Headless.FluentValidation/README.md) | امتدادات FluentValidation |
| [Headless.Generator.Primitives](src/Headless.Generator.Primitives/README.md) | مولّد شيفرة للبدائيات |
| [Headless.Generator.Primitives.Abstractions](src/Headless.Generator.Primitives.Abstractions/README.md) | عقود source generator |
| [Headless.Hosting](src/Headless.Hosting/README.md) | أدوات مساعدة للاستضافة |
| [Headless.NetTopologySuite](src/Headless.NetTopologySuite/README.md) | أدوات جغرافية مكانية |
| [Headless.Primitives](src/Headless.Primitives/README.md) | Value objects و Result وأنواع Paging الأساسية |
| [Headless.Redis](src/Headless.Redis/README.md) | أدوات مساعدة لـ Redis |
| [Headless.Sitemaps](src/Headless.Sitemaps/README.md) | توليد XML sitemaps |
| [Headless.Slugs](src/Headless.Slugs/README.md) | توليد URL slugs |
| [Headless.Urls](src/Headless.Urls/README.md) | بناء وتحليل URLs بأسلوب fluent |

## AI Agents

لو مشروعك يستخدم حزم Headless، أضف المقطع التالي في `AGENTS.md` أو `CLAUDE.md`. هذا يساعد أدوات الذكاء الاصطناعي أنها تجيب التوثيق المناسب بدل ما تخمّن من أسماء الحزم فقط:

</div>

```markdown
## Headless Framework

This project uses [Headless .NET Framework](https://github.com/xshaheen/headless-framework).

When working with Headless packages, fetch the docs index:
https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/index.md

The index lists per-domain docs to fetch as needed.
```

<div dir="rtl" align="right">

## المساهمة

المساهمات مرحّب بها، سواء كانت bug reports، اقتراحات، أو pull requests. راجع README الخاصة بالحزمة التي تعمل عليها لمعرفة التفاصيل والحدود الخاصة بها.

</div>
