# Testing and selected API example verification

Audit date: 2026-11-04. Repository revision: ff488de5dd152235ee66d01ed70cd2b4d999361a. No repository files changed by this evaluator. Read all of `references/testing.md` and the API sections discussed below. Environment: SDK10.0.302, runtime10.0.13, Linux x64; package versions MSTest.Sdk4.5.1, xunit.v3 3.2.2, Testcontainers.PostgreSql4.16.0, Respawn6.2.1/7.0.0, Microsoft.Extensions.TimeProvider.Testing10.0.0, MVC.Testing/TestHost10.0.13, Asp.Versioning.Http10.2.3 and companion MVC.ApiExplorer10.2.1.

## Confirmed findings

1. **Respawn PostgreSQL example fails across both checked versions.** `references/testing.md` lines196-200 passes a string to `Respawner.CreateAsync` with Postgres adapter; lines207-210 passes string to `ResetAsync`. Respawn7 removed these overloads: exact full OrdersApiTests snippet compiled with only request/response/Program support stubs fails CS1503 twice (string cannot convert to DbConnection), build exit1, 1 warning (obsolete PostgreSqlBuilder), 2 errors. See `respawn-full-build.log`, `RespawnFull/`. Respawn6 has the overload but explicitly rejects Postgres before opening a connection: focused runtime reproduction exits0 only after observing expected ArgumentException. See `respawn6-run.log`, `Respawn6/`. Fix recommendation: use opened NpgsqlConnection for CreateAsync and ResetAsync; do not pin old Respawn to retain a SQL-only convenience overload. No production code changed.

   Sources: https://raw.githubusercontent.com/jbogard/Respawn/v6.2.1/Respawn/Respawner.cs (string create is SQL Server-only); https://raw.githubusercontent.com/jbogard/Respawn/v7.0.0/Respawn/Respawner.cs (only DbConnection overloads).

2. **Minimal API antiforgery advice names a nonexistent extension and an MVC-only filter.** `references/api.md` antiforgery bullet calls `.RequireAntiforgery()`. Actual net10.0 framework reference compilation gives CS1061, build exit1,0 warnings1 error (`antiforgery-build.log`, `Antiforgery/`). The alternative `[ValidateAntiForgeryToken]` is an MVC IFilterFactory, not native Minimal API antiforgery metadata. TestServer runtime reproduction registered AddAntiforgery/UseAntiforgery and placed that attribute on a Minimal handler manually reading form fields: a tokenless POST executed and returned200. A native `[FromForm]` binding control rejected the same tokenless request with400. See `antiforgery-runtime.log`, `AntiforgeryRuntime/` (exit0). Fix: distinguish MVC action filters from Minimal API form-binding/antiforgery middleware and required metadata; demonstrate a real tokenless rejection. Do not simply replace the invented extension with another assumed call.

   Sources: https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.13/src/Mvc/Mvc.ViewFeatures/src/ValidateAntiForgeryTokenAttribute.cs ; https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0 . Actual framework reference XML contains RequireAntiforgeryTokenAttribute and IAntiforgeryMetadata, not RequireAntiforgery extension.

3. **Generic test filters do not work for the supported xUnit native-MTP combination.** `SKILL.md` Validation advertises `--filter FullyQualifiedName...` and TestCategory as general focused-test syntax. xunit.v3 3.2.2 running under native MTP SDK10 rejects `--filter` with exit5 and zero tests (`xunit-invalid-filter-escalated.log`). Positive control `--filter-method AuditTests.First` runs exactly1 selected test, passes, exit0, TRX parsed (`xunit-valid-filter.log`, `parsed-results.json`). Detect runner/framework before command selection. The documented MSTest filter did work on MSTest4.5.1 in native MTP.

   Sources: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test ; https://xunit.net/docs/getting-started/v3/cmdline . Runtime help retained in failure log lists supported --filter-method/class/trait/query flags.

4. **Versioning example omits an API Explorer dependency.** `references/api.md` API Versioning section names Asp.Versioning.Http and uses `.AddApiExplorer(options => ...)`. With only that package10.2.3 this registration fails CS1501 (no1-argument overload), build exit1,0 warnings1 error. Same code plus Asp.Versioning.Mvc.ApiExplorer10.2.1 compiles exit0,0 warnings0 errors. See `Versioning/`, `VersioningControl/`, respective build logs. This is an incomplete dependency declaration, not a fabricated AddApiExplorer API. Routing/response behavior was not executed.

   Sources: https://dotnet.github.io/aspnet-api-versioning/aspnet-core/docs/overview.html ; https://www.nuget.org/packages/Asp.Versioning.Mvc.ApiExplorer . Companion package remains needed even for Minimal APIs despite its name.

## Testing reference claim ledger

| Location/claim | Verdict | Evidence and boundary |
|---|---|---|
|19-23 behavior-focused tests and boundary-based choice | Pass as contextual guidance | Assertions target visible behavior; not a requirement to prove framework internals |
|24 real provider vs EF InMemory | Pass core warning; Needs qualification on exclusive Testcontainers wording | Testcontainers is a valid setup mechanism, but an externally provisioned isolated real DB can prove same provider invariants |
|25 direct time reads prohibited everywhere | Needs qualification | TimeProvider is valuable when behavior depends on controllable time; broad universal ban exceeds that need |
|33 current MSTest primary default | Preference | Valid local greenfield default; not an industry-wide requirement |
|36-43 sample MSTest.Sdk3.8.2 | Needs qualification | Historical literal not resolved/run; current resolved audit version4.5.1 worked. Avoid labeling fixed old version current |
|46-63 MSTest project config | Pass with supplied versions/CPM | Audit MSTest project uses current SDK4.5.1 and explicit resolved dependencies; bare sample requires external CPM versions; native runner also selected in global.json |
|67-95 xUnit fixture convention and project config | Pass with supplied versions; command-mode qualification | xunit3.2.2 compiled; native MTP needs global.json runner selection and xUnit-specific filters; preserving existing frameworks remains valid |
|98-130 static DatabaseFixture | Observed lifecycle Pass; Needs qualification | Exact snippet with only MSTest using added starts actual PostgreSQL Testcontainer;1 proof test passes, cleanup completes. Current4.16 builder constructor is obsolete CS0618. Official MSTEST0002 warns static TestClass is invalid, but do not report a runtime failure for lifecycle-only static class: this executed successfully on4.5.1 |
|135-151 WAF configuration override | Compile Pass | Exact factory also builds independently in FactoryCompile (exit0,0warnings0errors; factory-build.log). Configuration precedence depends on actual app registrations; runtime DB-provider replacement not verified here |
|157 ValidateOnStart host-build timing | Needs qualification | Startup validation occurs when host starts, often triggered by WAF CreateClient; do not imply Build alone always runs startup validators |
|159-163 override config, preserve validation | Pass as default; Needs qualification on DI absolute prohibition | Config override tests actual binding; focused tests may intentionally replace options/services |
|169-171 Respawn truncation ~5ms | Needs qualification | Version6 source generates ordered DELETE statements, not a general TRUNCATE claim. Duration is schema/data/environment-specific, not measured here |
|173 serialization of shared reset DB | Pass safety requirement for this sharing model | Independent DB per concurrent test is another safe design; serialization need not cover unrelated isolated tests |
|174-223 OrdersApiTests | Fail | Respawn7 build2xCS1503; Respawn6 string Postgres create rejects adapter at runtime. HTTP test asserts response but does not independently query persisted state despite name |
|230-255 xunit IAsyncLifetime sample | Compile Pass; runtime incomplete | Exact block with minimal CustomApiFactory/Program/request support compiles (2 warnings: obsolete PostgreSqlBuilder and xUnit1051 cancellation). PostgreSqlFixture is not connected to sample CustomApiFactory in shown code; actual persistence wiring/application behavior remains unverified |
|262-282 deterministic time example | Pass | Exact service/method, method placed inside a [TestClass] as indicated by fragment, test1/1 passes with real FakeTimeProvider; TRX parsed |
|286-295 antipatterns | Pass core guidance with context | Mocked DbSet cannot prove SQL behavior. Extracting private methods purely to test is not universally necessary. Existing abstractions can still be unit-tested |

## Every fenced testing.md block

| Block index (zero-based), line | Kind | Disposition |
|---|---|---|
|0,36 JSON SDK mapping | Configuration fragment | Syntax valid; historical3.8.2 version not restored, current4.5.1 selected explicitly for execution |
|1,46 XML MSTest project | Configuration fragment | Supports valid current SDK configuration; source omits package versions intentionally under CPM. Required supporting version declarations provided in audit projects |
|2,71 XML xUnit project | Configuration fragment | Current xunit3.2.2 project compiles and nativeMTP executes1 selected test; runner-mode choice supplied separately |
|3,102 C# DatabaseFixture | Runnable with MSTest import/project | Pass1/1 real container lifecycle test; CS0618 warning on4.16 default constructor |
|4,138 C# WAF factory | Illustrative reusable type | Compile checked with real MVC.Testing10.0.13; app-specific provider binding not run |
|5,174 C# OrdersApiTests | Runnable with app/DTO support | Fail2 errors with Respawn7; adapter exception with6; HTTP route not supplied/executed |
|6,234 C# xUnit fixture/test | Runnable with factory/app/DTO support | Compile Pass2warnings0errors; DB/application runtime not verified |
|7,266 C# service + test fragment | Illustrative separate production/test contexts | Pass1/1 Trial_After15Days_IsExpired after test placed in class; no logic edits |

## Commands and result artifacts

All commands target `/tmp/dotnet-testing-audit`; SDK-style new temporary projects, no app repository migrations or package changes. Restore failures from sandbox network were retried with approved escalation. Native MTP attempts first failed sandbox IPC pipe binding; escalated runs produced the actual results below, not counted as skill defects.

- `dotnet build MSTest/MSTest.csproj --no-restore -v minimal`: exit0,1warning,0errors. `mstest-build.log`.
- `dotnet test --project MSTest/MSTest.csproj --no-restore --report-trx`: exit0,1/1 passed FixtureInitializationRan. `mstest-run-escalated.log`, `TestResults/MSTest_net10.0_x64.trx`.
- `dotnet test --project MSTest/MSTest.csproj --no-restore --filter FullyQualifiedName~TimeProof.Trial_After15Days_IsExpired --report-trx --report-trx-filename time.trx`: exit0,1/1 passed Trial_After15Days_IsExpired. `time-run.log`, `TestResults/time.trx`.
- `dotnet test --project Xunit/Xunit.csproj --no-restore --filter FullyQualifiedName~AuditTests.First`: exit5,0 tests, Unknown option. `xunit-invalid-filter-escalated.log`.
- `dotnet test --project Xunit/Xunit.csproj --no-restore --filter-method AuditTests.First --report-xunit-trx --report-xunit-trx-filename xunit-valid.trx`: exit0,1/1 passed AuditTests.First. `xunit-valid-filter.log`, `Xunit/bin/Debug/net10.0/TestResults/xunit-valid.trx`.
- `dotnet run --project Respawn6/Respawn6.csproj --no-restore`: exit0 after expected rejection reproduced. `respawn6-run.log`.
- `dotnet build RespawnFull/RespawnFull.csproj --no-restore -v minimal`: exit1,1warning,2errors. `respawn-full-build.log`.
- `dotnet build Antiforgery/Antiforgery.csproj --no-restore -v minimal`: exit1,0warnings,1error. `antiforgery-build.log`.
- `dotnet run --project AntiforgeryRuntime/AntiforgeryRuntime.csproj --no-restore`: exit0, observed200tokenless attributed endpoint vs400native binding control. `antiforgery-runtime.log`.
- `dotnet build Versioning/Versioning.csproj --no-restore -v minimal`: exit1,0warnings,1error. `versioning-http-only-build.log`.
- `dotnet build VersioningControl/VersioningControl.csproj --no-restore -v minimal`: exit0,0warnings,0errors. `versioning-control-build.log`.
- `dotnet build XFixture/XFixture.csproj --no-restore -v minimal`: exit0,2warnings,0errors. `xfixture-build.log`.
- `dotnet build FactoryCompile/FactoryCompile.csproj --no-restore -v minimal`: exit0,0warnings,0errors. `factory-build.log`.

All3 produced TRX files parsed with XML parser; each had exactly1 UnitTestResult outcome Passed. Stable test names exclude arbitrary test data. `parsed-results.json` retains identities.

## Unverified and risks

No AOT publication, real API version negotiation/OpenAPI export, original Orders HTTP endpoint persistence, actual PostgreSQL Respawn reset after correction, or SDK3.8.2 run. Static TestClass is documented invalid by MSTEST0002 but its assembly lifecycle was observed to work in current4.5.1; do not convert this into a confirmed runtime defect. Runtime CSRF reproduction applies to a manually read form endpoint, showing the advertised MVC attribute alone does not enforce Minimal API validation; standard native form binding already adds protection. No skill files changed.
