# المهمة

أريد بناء منصة **Centralized Telemetry Platform** خاصة بي لاستخدامها مع عدة تطبيقات .NET، خصوصًا تطبيقات Desktop مثل WPF/WinForms، وربما ASP.NET/Blazor مستقبلًا.

لدي VPS خاص بي، وأريد عدم الاعتماد على PostHog أو Sentry في المرحلة الحالية.

التقنيات الأساسية:

- .NET 10
- ASP.NET Core
- PostgreSQL
- EF Core
- Serilog
- Docker
- Nginx
- Cloudflare
- تطبيقات Desktop: WPF / WinForms
- Dashboard يمكن بناؤها بـ Blazor أو ASP.NET Core

المنصة يجب أن تكون قابلة لإعادة الاستخدام من جميع تطبيقاتي.

---

# 1. الهدف العام

أريد معرفة لكل تطبيق:

- عدد Installations.
- عدد المستخدمين النشطين:
  - Daily Active Installations
  - Weekly Active Installations
  - Monthly Active Installations
- أول تشغيل.
- آخر تشغيل.
- إصدار التطبيق المستخدم.
- نظام التشغيل.
- Architecture:
  - x64
  - x86
  - ARM64
- لغة النظام/التطبيق.
- البلد.
- Features الأكثر استعمالًا.
- Events المهمة داخل التطبيق.
- الأخطاء والاستثناءات.
- عدد مرات حدوث كل Error.
- عدد الأجهزة المتأثرة بكل Error.
- الإصدار الذي ظهر فيه Error.
- First Seen.
- Last Seen.
- إمكانية معرفة هل Error ما زال موجودًا بعد إصدار جديد.

يجب أن تعمل المنصة مع عشرات التطبيقات بدون الحاجة لإنشاء Backend منفصل لكل تطبيق.

---

# 2. Architecture المطلوبة

اعتمد Architecture بالشكل التالي:

```text
                    Application A
                    Application B
                    Application C
                         │
                         │
                  Telemetry.Client
                  Shared NuGet Package
                         │
                         ▼
                 HTTPS / JSON / Batch
                         │
                         ▼
                    Cloudflare
                         │
                         ▼
              telemetry.example.com
                         │
                         ▼
              SmartApp.Telemetry.Web
                         │
            ┌────────────┴────────────┐
            │                         │
            ▼                         ▼
       PostgreSQL                Background Jobs
            │
            ▼
     Blazor Dashboard + API
```

يجب إنشاء Projects رئيسية:

```text
Telemetry.sln

src/
    SmartApp.Telemetry.Web
    SmartApp.Telemetry.Core
    SmartApp.Telemetry.Infrastructure

clients/
    SmartApp.Telemetry.Client

tests/
    SmartApp.Telemetry.Web.Tests
    SmartApp.Telemetry.Client.Tests
```

لا تستخدم Microservices.

استخدم Modular Monolith بسيط وقابل للتوسع.

---

# 3. دعم عدة تطبيقات Multi-App

يجب ألا تكون البيانات مرتبطة بتطبيق واحد.

أنشئ Entity باسم:

```text
Application
```

وتحتوي مثلًا:

```text
Id UUID
Name
Slug
Description
IsEnabled
CreatedAt
```

أمثلة:

```text
smartpharm
alsoque
store-dz
a3lam
modawana
```

كل Telemetry Event يجب أن يرتبط بـ:

```text
ApplicationId
```

أو:

```text
AppSlug
```

بحيث يمكن Dashboard عرض:

```text
All Applications

SmartPharm
StoreDz
AlSoque
A3lam
...
```

ثم اختيار تطبيق ومشاهدة إحصائياته.

---

# 4. Installation Identity

لا تعتمد على:

- Username
- Email
- Windows username
- MAC address
- HDD serial
- MachineGuid
- Hardware fingerprint

بدلًا من ذلك أنشئ Anonymous Installation ID.

في أول تشغيل:

```csharp
Guid.CreateVersion7()
```

ثم خزنه محليًا بشكل دائم مثل:

```text
%LocalAppData%/{Company}/{Application}/telemetry.json
```

مثال:

```json
{
  "installationId": "019c....",
  "createdAt": "2026-08-16T14:00:00Z"
}
```

يجب ألا يتغير ID عند كل تشغيل.

تغيير InstallationId يحدث فقط إذا حذف المستخدم بيانات التطبيق أو أعاد تثبيته بطريقة تحذف LocalAppData.

---

# 5. Telemetry Client

أنشئ مكتبة مشتركة:

```text
SmartApp.Telemetry.Client
```

ويفضل أن تصبح لاحقًا NuGet package خاصة أو عامة.

الاستخدام داخل أي تطبيق يجب أن يكون بسيطًا جدًا.

مثال:

```csharp
services.AddTelemetry(options =>
{
    options.Endpoint = "https://telemetry.example.com";
    options.Application = "smartpharm";
    options.Version = AppVersion;
});
```

ثم:

```csharp
ITelemetryClient telemetry;
```

والـ API الأساسي:

```csharp
Track(string eventName);

Track(string eventName, object properties);

TrackException(Exception exception);

TrackException(Exception exception, object context);

FlushAsync();

SetEnabled(bool enabled);
```

أضف Helpers مثل:

```csharp
TrackAppStarted();
TrackAppClosed();

TrackFeatureUsed(string feature);

TrackOperationSucceeded(string operation);

TrackOperationFailed(
    string operation,
    Exception? exception = null);
```

---

# 6. Events

لا ترسل Logs عشوائية إلى الخادم.

Telemetry Events يجب أن تكون Structured Events.

الأحداث الأساسية:

```text
app_first_started
app_started
app_closed

feature_used

operation_completed
operation_failed

update_available
update_started
update_completed
update_failed

exception
fatal_exception
```

شكل Event:

```json
{
  "application": "smartpharm",
  "installationId": "019c...",
  "eventName": "feature_used",
  "timestamp": "2026-08-16T14:30:00Z",

  "context": {
    "appVersion": "26.08.1601",
    "os": "Windows 11",
    "architecture": "x64",
    "language": "fr-DZ"
  },

  "properties": {
    "feature": "ExportPdf"
  }
}
```

Properties تكون JSON مرنة ولا تتطلب Migration لكل نوع Event.

استخدم PostgreSQL JSONB حيث يناسب.

---

# 7. لا تجعل Telemetry تؤثر على التطبيق

هذه قاعدة أساسية.

Telemetry يجب أن تكون:

```text
Fire-and-forget
Non-blocking
Failure tolerant
```

يجب ألا يفشل التطبيق إذا:

- VPS متوقف.
- API متوقف.
- PostgreSQL متوقف.
- المستخدم Offline.
- DNS لا يعمل.
- Timeout.
- Cloudflare لا يعمل.

أي مشكلة Telemetry يجب ألا تظهر للمستخدم كخطأ في التطبيق.

---

# 8. Queue محلية

لا ترسل HTTP request عند كل Event.

استخدم Queue.

Architecture:

```text
Application
    ↓
TelemetryClient.Track()
    ↓
Channel<T> / Queue
    ↓
Batch
    ↓
HTTP
```

أرسل الأحداث Batch.

مثال:

```json
{
  "events": [
    {},
    {},
    {},
    {}
  ]
}
```

حدد مثلًا:

```text
Max batch size: 50 events

Flush:
- كل 15-30 ثانية
- أو عند وصول Queue إلى الحد
- أو عند إغلاق التطبيق
```

إذا لم يكن الإنترنت متاحًا:

احفظ Queue صغيرة على القرص.

مثلاً:

```text
telemetry-queue.jsonl
```

ويجب وضع Limits:

```text
Max queue file size

مثلاً:
5-10 MB
```

مع حذف الأقدم عند تجاوز الحد.

لا تسمح للـ Telemetry باستهلاك مساحة غير محدودة.

---

# 9. HTTP Client

استخدم:

```text
HttpClientFactory
```

مع:

```text
Timeout قصير
Compression
JSON
Retry محدود
```

لا تعمل Retry بلا حدود.

مثال:

```text
Timeout: 5 seconds

Retry:
1 أو 2 مرات فقط
```

مع exponential backoff.

---

# 10. API Endpoints

أنشئ API versioned.

مثال:

```text
POST /api/v1/telemetry/events

POST /api/v1/telemetry/errors

POST /api/v1/telemetry/installations/heartbeat
```

ويمكن دمج heartbeat مع:

```text
app_started
```

إذا كان ذلك أبسط.

أضف:

```text
GET /health
```

للـ health checks.

---

# 11. لا تستخدم Secret داخل التطبيقات

التطبيقات ستكون مفتوحة المصدر.

لذلك لا تضع:

```text
API_SECRET
PRIVATE_KEY
MASTER_API_KEY
```

داخل Client.

أي Secret داخل التطبيق يجب اعتباره Public.

اعتبر Telemetry API عبارة عن:

```text
Public ingestion endpoint
```

واحمه Server Side.

---

# 12. حماية API

طبق:

```text
Rate Limiting
Request size limits
JSON schema validation
Allowed event names
App validation
Payload validation
Cloudflare
Nginx limits
```

حدد Max request body مناسب.

مثلاً:

```text
256 KB
```

أو أقل حسب الحاجة.

امنع Events بأسماء ضخمة أو Properties غير محدودة.

ضع Limits مثل:

```text
EventName <= 100 chars

AppVersion <= 50

Property key <= 100

String property <= 2000 chars

Max properties <= 30
```

---

# 13. Country Detection

لا ترسل GPS.

لا تخزن IP.

بما أن API خلف Cloudflare، اقرأ:

```http
CF-IPCountry
```

مثال:

```text
DZ
FR
US
```

خزّن فقط:

```text
CountryCode
```

ولا تخزن Client IP في قاعدة Telemetry.

---

# 14. Installation Entity

أنشئ جدولًا مشابهًا:

```text
Installations
```

الحقول:

```text
Id UUID
ApplicationId UUID

InstallationId UUID

FirstSeenAt
LastSeenAt

FirstVersion
CurrentVersion

CountryCode

OperatingSystem
OperatingSystemVersion

Architecture

Language

CreatedAt
UpdatedAt
```

Unique constraint:

```text
ApplicationId + InstallationId
```

---

# 15. Raw Events

أنشئ:

```text
TelemetryEvents
```

مثل:

```text
Id BIGINT / UUID

ApplicationId

InstallationId

EventName

AppVersion

Properties JSONB

OccurredAt
ReceivedAt
```

Indexes مهمة:

```text
ApplicationId
OccurredAt

ApplicationId + EventName

ApplicationId + InstallationId

ApplicationId + AppVersion
```

لا تضع Index على كل شيء.

---

# 16. Exception Reporting

أنا أستخدم Serilog.

يجب الحفاظ على Serilog للتسجيل المحلي.

مثال:

```text
Serilog

Verbose      → Local only
Debug        → Local only
Information  → Local only
Warning      → Local normally

Error        → Local + Telemetry
Fatal        → Local + Telemetry
```

لا ترسل جميع Serilog Logs للخادم.

أنشئ:

```text
ITelemetryExceptionReporter
```

يرسل فقط Exceptions المهمة.

---

# 17. Global Exception Handling

دعم WPF:

```text
Application.DispatcherUnhandledException
AppDomain.CurrentDomain.UnhandledException
TaskScheduler.UnobservedTaskException
```

مع إرسال الاستثناء إلى Telemetry ثم السماح لـ Serilog بتسجيله محليًا.

تجنب إرسال نفس Exception عدة مرات إذا التقطته أكثر من Handler.

---

# 18. Exception Payload

ارسل:

```text
Application
InstallationId

AppVersion

ExceptionType

Message

StackTrace

InnerException

OperatingSystem

Architecture

OccurredAt
```

مع Sanitization قبل الإرسال.

---

# 19. Sanitization

يجب إنشاء:

```text
TelemetrySanitizer
```

لمنع إرسال معلومات حساسة.

أزل أو Mask أي:

```text
Password

ConnectionString

JWT

Bearer token

API Key

Authorization header

Database password

User email

Phone number عند الإمكان

Full file paths إذا كانت حساسة
```

مثال:

```text
Host=localhost;Database=X;Password=123456
```

يجب ألا يصل إلى السيرفر كما هو.

---

# 20. Error Fingerprinting

لا تعرض كل Error occurrence كخطأ منفصل.

أنشئ Error fingerprint.

مثال يعتمد على:

```text
Exception Type

+

Top application stack frames
```

ثم:

```text
SHA256
```

مثال:

```text
NullReferenceException
SmartPharm.Services.SaleService.SaveSale
SaleService.cs
```

→

```text
Fingerprint
```

يجب تجاهل line numbers بقدر الإمكان حتى لا يتحول نفس الخطأ إلى Group جديد بعد Build جديد.

---

# 21. Error Groups

أنشئ:

```text
ErrorGroups
```

مثل:

```text
Id

ApplicationId

Fingerprint

ExceptionType

Title

FirstSeenAt

LastSeenAt

FirstSeenVersion

LastSeenVersion

TotalOccurrences

AffectedInstallations

IsResolved

ResolvedAt

ResolvedInVersion
```

---

# 22. Error Occurrences

و:

```text
ErrorOccurrences
```

مثل:

```text
Id

ErrorGroupId

ApplicationId

InstallationId

AppVersion

Message

StackTrace

Context JSONB

OccurredAt
```

---

# 23. تحديث ErrorGroups

عند وصول Exception:

```text
Calculate Fingerprint

↓


Find existing ErrorGroup

↓


إذا موجود:

Increment TotalOccurrences

Update LastSeenAt

Update LastSeenVersion


إذا غير موجود:

Create ErrorGroup
```

AffectedInstallations يجب أن يكون عدد Installation IDs الفريدة المتأثرة.

---

# 24. Resolved Errors

في Dashboard يجب أن أستطيع تحديد Error بأنه:

```text
Resolved
```

وتسجيل:

```text
ResolvedAt
ResolvedInVersion
```

إذا ظهر Error مرة أخرى بعد أن تم اعتباره Resolved:

اعرضه:

```text
Regressed
```

---

# 25. Dashboard

أنشئ Dashboard Admin.

الصفحة الرئيسية:

```text
All Applications
```

Cards:

```text
Total Installations

Active Today

Active 7 Days

Active 30 Days

Events Today

Errors Today

Crash-free installations
```

---

# 26. Application Dashboard

عند فتح تطبيق معين:

```text
SmartPharm
```

اعرض:

```text
Installations

DAU
WAU
MAU

New installations today

App versions

Countries

OS versions

Architectures

Languages

Most used features

Recent errors
```

---

# 27. Charts

أريد Charts لـ:

```text
Installations over time

DAU over time

WAU over time

MAU over time

App versions distribution

Countries

OS versions

Feature usage

Errors over time
```

---

# 28. Errors Dashboard

جدول:

```text
Error

Occurrences

Affected installations

First seen

Last seen

First version

Last version

Status
```

مثال:

```text
NullReferenceException
SaleService.SaveSale

Occurrences:
2841

Affected:
193

First:
2026-08-10

Last:
2026-08-16

Versions:
1.4.1 → 1.4.3

Status:
Open
```

---

# 29. Error Details

صفحة Error يجب أن تعرض:

```text
Exception Type

Message

Stack Trace

First Seen

Last Seen

Occurrences

Affected Installations

Versions

Operating systems

Countries

Recent occurrences
```

مع زر:

```text
Mark Resolved
```

---

# 30. Analytics Queries

يجب دعم:

### DAU

Unique Installation IDs التي ظهرت خلال 24 ساعة أو منذ بداية اليوم UTC.

### WAU

Unique installations خلال آخر 7 أيام.

### MAU

Unique installations خلال آخر 30 يومًا.

حدد semantics بوضوح واستخدمها بشكل ثابت في جميع Dashboard queries.

---

# 31. Feature Tracking

مثال:

```csharp
telemetry.TrackFeatureUsed("ProductSearch");

telemetry.TrackFeatureUsed("ExportPdf");

telemetry.TrackFeatureUsed("Backup");

telemetry.TrackFeatureUsed("ImportProducts");
```

في Dashboard:

```text
Most Used Features

ProductSearch     182,921

Backup             18,392

ExportPdf          12,820
```

احسب:

```text
Total usage

Unique installations
```

---

# 32. App Version Tracking

كل Event يجب أن يحتوي AppVersion.

Dashboard يعرض:

```text
Latest version
Previous version
Old versions
```

مثال:

```text
26.08.1601       71%

26.08.1502       18%

Older            11%
```

---

# 33. Privacy

Telemetry يجب أن تكون Anonymous.

لا تجمع افتراضيًا:

```text
Name
Email
Phone
Address
Customer data
Database contents
Files
Documents
Passwords
Tokens
```

أضف:

```text
Telemetry Enabled
```

داخل إعدادات Client.

يجب دعم:

```csharp
telemetry.SetEnabled(false);
```

وعند تعطيله:

لا ترسل أي Analytics أو Exception reports.

---

# 34. Default Telemetry Configuration

قسم الإعدادات:

```text
EnableTelemetry

EnableAnalytics

EnableCrashReporting
```

مثال:

```csharp
services.AddTelemetry(options =>
{
    options.Application = "smartpharm";

    options.Endpoint =
        "https://telemetry.example.com";

    options.EnableAnalytics = true;

    options.EnableCrashReporting = true;
});
```

---

# 35. Performance

Telemetry لا يجب أن:

- تبطئ Startup.
- تبطئ UI.
- تنفذ Network calls على UI Thread.
- تسبب Memory leak.
- تنشئ HttpClient جديدًا لكل Request.

استخدم:

```text
BackgroundService / Worker

Channel<T>

HttpClientFactory

CancellationToken
```

---

# 36. Database Growth

لا أريد الاحتفاظ بكل Raw Event إلى الأبد.

صمم Retention policy.

مبدئيًا:

```text
Raw analytics events:
90 days

Error occurrences:
180 days أو configurable

Installation records:
Permanent

Error groups:
Permanent

Daily aggregates:
Permanent
```

---

# 37. Aggregated Statistics

أنشئ Daily Aggregate Tables مستقبلًا أو من البداية إذا كان implementation بسيطًا.

مثال:

```text
DailyApplicationStats
```

```text
ApplicationId

Date

ActiveInstallations

NewInstallations

TotalEvents

TotalErrors
```

و:

```text
DailyEventStats
```

```text
ApplicationId

Date

EventName

TotalCount

UniqueInstallations
```

هذا يمنع Dashboard من Scan ملايين Raw Events مستقبلًا.

---

# 38. Background Jobs

أنشئ Worker لتنفيذ:

```text
Daily aggregation

Retention cleanup

Error statistics refresh
```

يمكن استخدام:

```text
BackgroundService
```

ولا تضف Hangfire إلا إذا كانت هناك حاجة حقيقية.

---

# 39. PostgreSQL

استخدم PostgreSQL.

اهتم بـ:

```text
Indexes

UTC timestamps

JSONB

Unique constraints

Efficient aggregations
```

جميع timestamps في قاعدة البيانات:

```text
UTC
```

---

# 40. EF Core

استخدم EF Core للموديل وMigrations.

Queries الإحصائية الثقيلة يمكن استخدام:

```text
EF Core

أو

Raw SQL
```

إذا كان Raw SQL أوضح وأسرع.

لا تستخدم abstraction زائدة بدون حاجة.

---

# 41. Docker

أنشئ Dockerfile للـ API والـ Dashboard إذا كانا منفصلين.

و docker-compose مثال يحتوي:

```text
telemetry-api

telemetry-dashboard

postgres
```

لكن في Production يمكن استخدام PostgreSQL الموجود مسبقًا.

---

# 42. Health Checks

أضف:

```text
/health
/health/ready
```

وتحقق من:

```text
API

PostgreSQL
```

---

# 43. Logging على السيرفر

استخدم Serilog على Telemetry API نفسه.

لكن انتبه إلى عدم حدوث Loop مثل:

```text
Telemetry API error
→ send error to Telemetry API
→ error
→ send...
```

Telemetry Server نفسه يسجل أخطاءه محليًا فقط أو في Log system مستقل.

---

# 44. Admin Authentication

Dashboard ليست Public.

أضف Authentication.

في المرحلة الأولى يمكن استخدام:

```text
ASP.NET Core Identity
```

بحساب Admin.

لا تجعل Ingestion API يتطلب نفس Admin authentication.

---

# 45. API Documentation

أضف OpenAPI.

لكن في Production لا تعرض Swagger للعموم إلا عند الحاجة أو احمه.

---

# 46. Client Versioning

مكتبة:

```text
SmartApp.Telemetry.Client
```

يجب أن تكون versioned.

مثال:

```text
1.0.0
1.1.0
```

API كذلك:

```text
/api/v1/
```

حتى يمكن تطوير Client بدون كسر التطبيقات القديمة.

---

# 47. Integration المطلوبة في كل تطبيق

بعد الانتهاء، دمج تطبيق جديد يجب ألا يحتاج أكثر من:

```csharp
services.AddTelemetry(options =>
{
    options.Application = "my-app";

    options.Endpoint =
        "https://telemetry.example.com";

    options.Version =
        AppVersion.Current;
});
```

ثم:

```csharp
telemetry.TrackAppStarted();
```

وفي أي مكان:

```csharp
telemetry.TrackFeatureUsed("FeatureName");
```

وExceptions يجب التقاطها عالميًا قدر الإمكان.

---

# 48. لا تكرر كود Telemetry داخل التطبيقات

ممنوع إنشاء Implementation مختلف لكل تطبيق.

كل logic يجب أن يكون داخل:

```text
SmartApp.Telemetry.Client
```

والتطبيق يحدد فقط:

```text
Application
Version
Endpoint
Enabled/Disabled
```

---

# 49. الاختبارات

أنشئ Tests على الأقل لـ:

```text
InstallationId persistence

Batching

Offline queue

Queue limits

Retry

Disabled telemetry

Exception fingerprinting

Sanitization

API validation

Rate limiting

Application isolation

DAU/WAU/MAU queries
```

---

# 50. ترتيب التنفيذ

نفذ على مراحل.

## Phase 1 — Foundation

أنشئ:

```text
Solution

Domain entities

PostgreSQL

EF migrations

Application registration

Installations
```

## Phase 2 — Client SDK

نفذ:

```text
InstallationId

TelemetryClient

Queue

Batching

HTTP transport

offline behavior
```

## Phase 3 — Analytics ingestion

نفذ:

```text
/events endpoint

app_started

feature_used

installations update
```

## Phase 4 — Error reporting

نفذ:

```text
Exception reporter

Sanitizer

Fingerprinting

ErrorGroups

ErrorOccurrences
```

## Phase 5 — Dashboard

نفذ:

```text
Overview

Applications

Installations

DAU/WAU/MAU

Versions

Countries

Features

Errors
```

## Phase 6 — Production hardening

نفذ:

```text
Rate limiting

Payload limits

Retention

Aggregation

Docker

Nginx

Cloudflare

Health checks
```

---

# 51. أهم قاعدة تصميم

لا تبنِ النظام كـ Log Server عام.

هناك فرق بين:

```text
Application Logs
```

و:

```text
Telemetry
```

Serilog يبقى مسؤولًا عن Logs التفصيلية.

Telemetry مسؤولة عن:

```text
Usage

Analytics

Health

Crashes

Exceptions

Versions

Installations
```

---

# 52. المطلوب من Coding Agent

ابدأ أولًا بفحص Solution الحالية إن وجدت.

ثم:

1. اقترح Architecture النهائية.
2. أنشئ Projects المطلوبة.
3. أنشئ Domain Model.
4. أنشئ PostgreSQL schema وEF migrations.
5. أنشئ Telemetry API.
6. أنشئ reusable .NET Client SDK.
7. أنشئ Error fingerprinting.
8. أنشئ Sanitization.
9. أنشئ Queue + batching + offline persistence.
10. أنشئ Dashboard.
11. أضف Tests.
12. أنشئ Docker deployment.
13. اكتب Documentation لكيفية دمج تطبيق جديد.

لا تكتفِ بكتابة Plan.

قم بتنفيذ الكود فعليًا مرحلة بمرحلة.

لا تقم بإعادة كتابة أجزاء تعمل بالفعل دون سبب.

استخدم أبسط Architecture تحقق المتطلبات.

تجنب:

```text
Microservices
Kafka
RabbitMQ
Redis
Elasticsearch
ClickHouse
Kubernetes
```

في الإصدار الأول إلا إذا ظهرت حاجة فعلية مثبتة لها.

PostgreSQL يجب أن يكون كافيًا في المرحلة الأولى.

---

# 53. Definition of Done

أعتبر النظام جاهزًا عندما أستطيع أخذ تطبيق WPF جديد وكتابة:

```csharp
services.AddTelemetry(options =>
{
    options.Application = "my-new-app";
    options.Endpoint =
        "https://telemetry.example.com";
});
```

ثم تشغيله على جهازين مختلفين، وبعدها أجد في Dashboard:

```text
Installations: 2

Active Today: 2

Country

Version

Operating System
```

وعندما يحدث Exception أجد:

```text
Error Group

Stack Trace

Occurrences

Affected Installations

App Version

First Seen

Last Seen
```

وعندما أستخدم Feature مثل:

```csharp
telemetry.TrackFeatureUsed("ExportPdf");
```

أجدها في Dashboard.

وفي حال إيقاف Telemetry Server بالكامل يجب أن يستمر التطبيق في العمل بشكل طبيعي دون أي بطء أو Error ظاهر للمستخدم.
