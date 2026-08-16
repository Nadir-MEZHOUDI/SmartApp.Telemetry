# SmartApp Telemetry

نسخة أولى بسيطة لمنصة Telemetry مركزية لتطبيقات .NET Desktop وASP.NET.

## ما تم تنفيذه

- Modular monolith واحد: API + Infrastructure + Core.
- PostgreSQL وEF Core مع migration أولية.
- Multi-app عبر Application وApplicationId.
- Anonymous installation ID محفوظ محليًا في %LocalAppData%.
- SDK مشتركة SmartApp.Telemetry.Client، ويتم نشرها كحزمة NuGet محليًا وإلى Azure Artifacts.
- إعداد الحزمة موجود في clients/SmartApp.Telemetry.Client، وAzure DevOps pipeline في azure-pipelines.yml.
- Queue داخل الذاكرة، batching حتى 50 حدثًا، retry محدود، وoffline JSONL queue بحد أقصى.
- أحداث الاستخدام، feature tracking، استثناءات، fingerprinting، sanitization.
- Dashboard بسيط لعرض التطبيقات، installations، activity، versions، features والأخطاء.
- DAU/WAU/MAU مبنية على Installation.LastSeenAt.
- Rate limiting، body limit، allowed event names، health checks، CORS، وCloudflare country header.
- Background maintenance للـ daily aggregates وretention.
- Docker Compose لـ PostgreSQL وAPI وDashboard.

## التشغيل المحلي

المتطلبات: .NET 10. لتشغيل API محليًا يجب وجود PostgreSQL، ثم:

~~~powershell
dotnet restore Telemetry.sln
dotnet run --project src/SmartApp.Telemetry.Api
dotnet run --project src/SmartApp.Telemetry.Dashboard
~~~

أو شغّل كل شيء:

~~~powershell
docker compose up --build
~~~

لبناء حزمة العميل محليًا:

~~~powershell
dotnet pack clients/SmartApp.Telemetry.Client/SmartApp.Telemetry.Client.csproj --configuration Release
~~~

ستجد الحزمة في D:\Programming\LocalNuget.

بعدها:

- Dashboard: http://localhost:8080
- API: http://localhost:5000
- Health: http://localhost:5000/health
- OpenAPI: http://localhost:5000/openapi/v1.json

غيّر كلمات مرور PostgreSQL وDashboard__AdminKey قبل أي نشر عام.

## تسجيل تطبيق جديد

~~~powershell
curl -X POST http://localhost:5000/api/v1/applications -H "Content-Type: application/json" -d '{"name":"SmartPharm","slug":"smartpharm"}'
~~~

## دمج تطبيق WPF/WinForms

أضف مرجع مشروع الـ SDK أو حوّلها لاحقًا إلى NuGet:

~~~csharp
services.AddTelemetry(options =>
{
    options.Application = "smartpharm";
    options.Endpoint = "https://telemetry.example.com";
    options.Version = AppVersion.Current;
    options.EnableAnalytics = true;
    options.EnableCrashReporting = true;
});
~~~

ثم استخدم:

~~~csharp
var telemetry = services.GetRequiredService<ITelemetryClient>();

telemetry.TrackAppStarted();
telemetry.TrackFeatureUsed("ExportPdf");

try
{
    // operation
}
catch (Exception exception)
{
    telemetry.TrackException(exception, new { operation = "ExportPdf" });
}
~~~

لـ AppDomain وTaskScheduler:

~~~csharp
TelemetryExceptionHooks.AttachProcessWide(telemetry);
~~~

وفي WPF اربط Application.DispatcherUnhandledException داخل التطبيق نفسه، لأن الـ SDK لا تعتمد على WPF:

~~~csharp
Application.Current.DispatcherUnhandledException += (_, args) =>
{
    telemetry.TrackException(args.Exception);
};
~~~

عند توقف الخادم أو الإنترنت، الـ SDK لا ترمي خطأ إلى التطبيق؛ تحفظ batch صغيرة في:

~~~text
%LocalAppData%/SmartAppTelemetry/app-name/telemetry-queue.jsonl
~~~

## API ingestion

الأحداث المدعومة:

~~~text
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
~~~

لا توجد أسرار داخل SDK. حماية Dashboard تتم عبر X-Admin-Key عند ضبط Dashboard__AdminKey. ingestion endpoint عام عمدًا، ولذلك يجب إبقاء rate limiting وCloudflare وNginx مفعّلة.

## قاعدة البيانات

يتم تشغيل MigrateAsync عند بدء API. الـ migration اليدوية الأولى موجودة في:

~~~text
src/SmartApp.Telemetry.Infrastructure/Migrations/20260816190000_InitialCreate.cs
~~~

الاحتفاظ الافتراضي:

- raw events: 90 يومًا
- error occurrences: 180 يومًا
- installations وerror groups وdaily aggregates: دائمًا

يمكن تغيير ذلك عبر:

~~~text
Telemetry__RawEventRetentionDays
Telemetry__ErrorRetentionDays
Telemetry__MaintenanceIntervalHours
~~~
