# Independent source and example audit: seven dotnet references

Audited 2026-11-04 against SDK 10.0.302 / C# 14 / .NET 10.0.13 on Linux x64. Read all seven assigned references completely. No skill/repository files changed. `skill-creator` criteria applied: distinguish requirements from preferences, preserve context, expose only relevant detail, and test observable behavior.

This is a focused sub-audit, not an overall verification verdict. Pass means the specific evidence described supports the claim; it does not imply full application, AOT, security, performance, or production readiness.

## Material findings

### S1. Two positive security endpoint examples do not compile

- **Severity:** High for example usability; not a demonstrated deployed vulnerability.
- **Location:** `references/security.md:47-69` and `:108-123`.
- **Evidence:** Exact endpoint bodies with minimal domain/database stubs fail `dotnet build` under SDK 10.0.302. GET: CS1593, `RequestDelegate does not take 5 arguments`. PUT: CS1593, `RequestDelegate does not take 4 arguments`. Multiple concrete `TypedResults` return types prevent inference of the route-handler delegate return type.
- **Control:** Changing only `async (` to `async Task<IResult> (` makes both examples compile. An explicit `Task<Results<...>>` union would retain typed metadata and also address the issue.
- **Artifacts:** `/tmp/dotnet-source-audit-proofs/SecurityGet`, `SecurityPut`, `SecurityGetFixed`, `SecurityPutFixed`; each has source and `build.log`.
- **Authority:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses?view=aspnetcore-10.0 (TypedResults multiple-return-type union examples).
- **Fix:** Show an explicit return type in both examples, and retain authorization/binding semantics.

### S2. Health JSON example exposes exception messages

- **Severity:** High when probes are reachable outside a trusted operational boundary.
- **Location:** `references/observability.md:104-125`, particularly `:120-121`.
- **Evidence:** The exact writer serializes `Exception.Message` and description. A HealthReportEntry containing exception marker `SECRET_TEST_VALUE` produces JSON with `"error":"SECRET_TEST_VALUE"`. No authentication/network restriction is shown with the mapped probes. A real database exception may reveal infrastructure details.
- **Authority:** https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0 explains custom writers and authorization. Default writer is plaintext, not a built-in JSON writer. Exception descriptions can also contain the exception message.
- **Fix:** Return coarse status publicly; keep diagnostic details in protected telemetry, or explicitly protect the operational endpoint and define its audience. Remove both exception text and unsafe descriptions from public output.
- **Artifact:** `/tmp/dotnet-source-audit-proofs/Observability/run.log`.

### S3. Registering a redactor does not classify sensitive logging values

- **Severity:** High if consumers rely on the stated guarantee.
- **Location:** `references/observability.md:130-145`.
- **Evidence:** The text promises credentials/identifiers never reach aggregators; the example only registers `EnableRedaction`, a classification, and `ErasingRedactor`. It defines no classification attribute and applies no classification to any logging parameter. Existing `LogMessages` customer ID parameters are unclassified. It cannot establish the promised protection.
- **Authority:** https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation#redacting-sensitive-information-in-logs explicitly requires classified sensitive parameters as well as registration and log-output checking. https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction documents classification-specific redactor selection.
- **Fix:** Demonstrate classification and its application to generated logging parameters; mention required `Microsoft.Extensions.Telemetry` integration and packages. Add an output check with a secret marker. Do not promise registration scans arbitrary strings.
- **Limit:** Source-confirmed incomplete protection. `Microsoft.Extensions.Compliance.Redaction` is not cached here; this snippet was not restored/run.

### S4. The metric example uses an unbounded customer dimension and counts attempts as creations

- **Severity:** Medium.
- **Location:** `references/observability.md:39-51`, particularly `:45-49`.
- **Evidence:** Every customer ID creates another `orders.created` metric dimension; this can hit collector cardinality limits or raise storage cost. `StartOrderCreation` increments before any order is successfully created, so failed attempts are still counted as created orders.
- **Authority:** https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation#multi-dimensional-metrics specifically explains why CustomerID dimensions can explode combinations and drop data/increase cost.
- **Fix:** Keep customer IDs in appropriately controlled traces/logs, use bounded metric attributes, and record the counter on successful creation (or name it attempts).
- **Limit:** Cardinality consequence is source-supported, not load-tested against a collector.

### S5. Blanket database BenchmarkDotNet prohibition contradicts official EF guidance

- **Severity:** Medium.
- **Location:** `references/performance.md:102`.
- **Evidence:** It forbids BenchmarkDotNet for database operations. Microsoft's EF performance-diagnosis documentation recommends BenchmarkDotNet and provides actual async EF query benchmarks. Single-operation database benchmarking and concurrent end-to-end load testing answer different questions.
- **Authority:** https://learn.microsoft.com/en-us/ef/core/performance/performance-diagnosis#benchmarking-with-ef-core.
- **Fix:** Allow controlled representative database microbenchmarks; explain their limits, and reserve load tests for concurrent/request-level questions.

### S6. Architecture preference is turned into a prohibition and conflicts with its own caveat

- **Severity:** Medium for decision quality.
- **Location:** `references/architecture.md:95-98` versus `:113-139` and `:254`.
- **Evidence:** The table prohibits all generic EF repository wrappers, while later text rejects them only by default or when they merely rename EF. It also mandates a `Common`/`Shared` directory and zero external Domain dependencies as universal architecture rules. These are possible local conventions, not inherent platform requirements.
- **Authority:** https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design discusses repositories as valid aggregate boundaries; https://learn.microsoft.com/en-us/dotnet/standard/modern-web-apps-azure-architecture/common-web-application-architectures presents contextual architecture choices. `skill-creator` explicitly cautions against turning preferences into universal requirements.
- **Fix:** Keep the table conditional on the repository's actual design; flag redundant indirection or boundary violations with evidence instead of banning a type name/layout.

### S7. Absolute logging and allocation language exceeds the evidence

- **Severity:** Low/Medium depending on reliance.
- **Location:** `references/csharp.md:174`, `references/observability.md:11`, `references/performance.md:113`.
- **Evidence:** LoggerMessage removes specific template/boxing allocations but arbitrary logger providers and message formatting can still allocate. A BenchmarkDotNet `0 B` managed-allocation result is scoped to the benchmark and diagnoser, not proof of no native/stack allocation or universal runtime behavior.
- **Authority:** https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation says allocations are removed to the maximum extent possible; https://benchmarkdotnet.org/articles/configs/diagnosers.html distinguishes MemoryDiagnoser and native-memory diagnostics.
- **Fix:** Say reduced/avoided logging-call allocations, specify provider/formatting scope, and report measured managed allocation for that benchmark rather than universal zero allocation.

### S8. Worker sample failure handling needs an explicit delivery policy

- **Severity:** Needs qualification, not an unconditional defect.
- **Location:** `references/hosted-services.md:88-103`.
- **Evidence:** Reading removes an item from the Channel; a processing exception is logged and the loop moves on, so the item is lost. This is valid for best-effort work, but not durable business work. There is no completion/drain path; cancellation can abandon queued work.
- **Authority:** https://learn.microsoft.com/en-us/dotnet/core/extensions/channels describes in-memory producer/consumer behavior; https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0 explains shutdown and queue processing.
- **Fix:** State at-most-once/best-effort limits and choose retry/dead-letter/durable processing only when required. Avoid universally logging and continuing for permanent errors.

## All assigned sections: disposition ledger

Paths below are relative to `skills/dotnet/`. Multiple neighboring lines are grouped only when they express the same decision or invariant. Source-checked advice does not imply every hypothetical usage was tested.

| File and lines | Claims/section | Verdict and evidence |
|---|---|---|
| architecture.md:5-29 | Requirements and decision flow | Pass as contextual design advice; no invented API or imposed architecture |
| architecture.md:31-35 | Modular monolith default; justified service boundaries | Pass as recommendation, not requirement; architecture source above |
| architecture.md:37-66 | Vertical slices/CQRS; direct/mediator flow | Needs qualification: legitimate options, not measured universal speed/AOT guarantees |
| architecture.md:68-78 | Clean Architecture layout/dependency direction | Needs qualification: useful sample layout; zero external dependencies is stronger than required domain isolation |
| architecture.md:80-87 | Module-owned models; contracts/events | Pass as an explicitly chosen encapsulated-module convention; not the only possible modular architecture |
| architecture.md:89-98 | Anti-drift rules | Fail as universal rule; S6 |
| architecture.md:100-139 | Ownership and persistence abstractions | Pass as conditional advice; both interface examples compile |
| architecture.md:141-165 | Explicit DTO factories/projections and domain constructors | Pass as optional pattern; example compiles/maps. Actual SQL translation/provider semantics not executed here |
| architecture.md:167-168 | Source-generated mapping vs runtime mapper; preserve existing | Needs qualification: preserve existing good; blanket “fail under Native AOT” requires package/version-specific evidence, not categorical mapper-family claim |
| architecture.md:170-177 | Required config startup validation | Needs qualification: strong default when required infrastructure truly required at startup; local architecture policy, not .NET framework mandate |
| architecture.md:181-201 | DataAnnotations options and startup registration | Pass build; validates basic values. Not a connection-string parser nor connection-availability guarantee |
| architecture.md:203-218 | OptionsValidator/AOT | Pass source-generation/build with SDK10; Native AOT binding also needs configuration binding generator where not auto-enabled; AOT publish not performed |
| architecture.md:220-234 | Cross-field fluent validation | Pass compile with minimal StorageOptions support; source-supported options API |
| architecture.md:236-240 | Options lifetimes | Pass broad lifetime distinction; Snapshot recomputes per scope on access, not unconditional physical config read on every request |
| architecture.md:242-250 | Dependency decision/security primitives | Pass as policy; no runtime claim |
| architecture.md:252-280 | Conditional anti-patterns/review questions | Pass; this nuance should govern earlier prohibitions |
| csharp.md:7-32 | Current C#14/.NET10 and effective language version | Pass; verified official C#14 page; compile uses SDK10.0.302 |
| csharp.md:34-48 | Primary constructors | Pass syntax; sample has unused `clock` CS9113, so unnecessary DI dependency in example |
| csharp.md:50-90 | field/extension blocks/C#14 features | Pass syntax/features; `Name` sample produces CS9264 uninitialized non-null property warning |
| csharp.md:92-102 | Records, init, required; choose semantics | Pass as conditional type-design advice |
| csharp.md:104-119 | Collections/Frozen types | Pass collection syntax. Needs qualification on “significantly faster” Frozen lookups: construction/size/key distribution matter; official API says optimized frequent lookups with higher creation cost |
| csharp.md:121-149 | Pattern matching/nullability/guards | Pass APIs and idioms; no examples claim exhaustive enforcement |
| csharp.md:151-166 | Task/async streams/cancellation/ValueTask/blocking | Pass as contextual runtime advice; modern async API contract preserved |
| csharp.md:168-178 | Span/pooling/LoggerMessage/SearchValues | Needs qualification: zero-allocation S7; SearchValues bytes/chars in .NET8 and strings in .NET9 confirmed |
| csharp.md:180-196 | Generic contracts, nameof, formatting | Pass contextual language guidance |
| csharp.md:198-221 | Exceptions/domain type design | Pass conditional architecture guidance, cancellation preserved |
| csharp.md:223-247 | JsonSerializerContext generation/registration | Pass compile and camelCase serialization check; collections/object runtime types need their own metadata as applicable |
| csharp.md:249-252 | Reflection/trim warnings/generated APIs | Pass as direction; not proof every source-generated library is AOT-compatible |
| csharp.md:254-281 | Modernization/checklist | Pass; discourages unnecessary syntax churn |
| observability.md:5-14 | Tracing/metrics/logging/probes/redaction decisions | Needs qualification: custom JSON writer mislabeled built-in; un-parented root activities can be legitimate; S3/S7 |
| observability.md:17-34 | OTel registration | Pass compile with explicit actual OTel packages, not framework alone. Export/collector end-to-end not run |
| observability.md:36-53 | Domain activities and counters | Fail safe default due customer dimension and premature creation count; S4 |
| observability.md:57-78 | LoggerMessage partial class/methods | Pass actual generator compile; performance qualification S7 |
| observability.md:82-102 | Live vs ready probes | Pass separation design; AddNpgSql needs community package, not built-in framework. PostgreSQL probe not run; degraded status defaults to 200, not all failing/degraded statuses are 503 |
| observability.md:104-126 | JSON health response | Fail public-safe diagnostic output; runtime S2. This is a custom writer using built-in serializer, not prebuilt framework JSON writer |
| observability.md:130-145 | Redaction guarantee/configuration | Fail completeness of stated protection S3; package compilation/runtime Unverified |
| hosted-services.md:5-19 | Scope, timer, bounded queues, cancellation, retry | Pass as general runtime principles; exact retry/failure policy must fit work |
| hosted-services.md:21-52 | Periodic scoped worker | Pass compile; scope async disposal proper. Timer cancellation occurs outside try but normal host cancellation is handled by BackgroundService host, not automatically a defect |
| hosted-services.md:56-76 | Bounded queue | Pass compile and capacity 1000/backpressure/cancellation runtime checks |
| hosted-services.md:78-107 | Consumer worker | Pass compile; qualify best-effort loss/shutdown behavior S8. Scope per message correct |
| hosted-services.md:111-125 | ShutdownTimeout/default/cancellation | Pass 30s generic-host default and configurable timeout; 15s example is a decrease, not inherently wrong. Unnecessary blanket explicit OCE catch requirement; runtime host handles normal cancellation |
| performance.md:5-16 | Measure before optimizing | Pass as method |
| performance.md:18-28 | Algorithms/allocations/Frozen/SearchValues/logging | Needs qualification for categorical Frozen speed; actual data/creation cost/lookup count must justify. SearchValues versions source-confirmed |
| performance.md:30-57 | Async, DB plan/query focus, HTTP retry costs | Pass contextual advice |
| performance.md:59-85 | Serialization/caching/HybridCache | Pass functionality at API-documentation level; HybridCache stampede protection is per instance, not global distributed factory exclusion; distributed L2 requires registration |
| performance.md:87-93 | Bounded concurrency/Task.Run | Pass advice; no benchmark claims |
| performance.md:95-108 | BenchmarkDotNet usage and precautions | Fail blanket DB ban S5; MemoryDiagnoser “always” is a preference when CPU-only metrics suffice |
| performance.md:110-127 | Statistical columns, allocation, benchmark fragment | Needs qualification S7; parser methods/domain result omitted, BDN package absent so compile/run Unverified |
| performance.md:129-137 | Review checklist | Pass contextual measurement questions |
| security.md:5-25 | Fail closed/auth choices/framework primitives | Pass principles; no custom crypto/protocol code |
| security.md:27-40 | Fallback policy | Pass compile; meaningful choice for an authenticated-by-default API, not universal for deliberately public APIs; authentication scheme registration remains necessary |
| security.md:42-69 | Resource lookup/imperative authorization GET | Fail compilation S1. Resource authorization concept sound; handler registration missing in fragment, can be supporting setup |
| security.md:71-94 | Owner-based operations handler | Pass compile/runtime owner/stranger check; broader tenant/operation requirements need their own policies |
| security.md:96-124 | Explicit DTO/PUT overposting defense | Fail compilation S1; DTO prevents these fields' mass assignment only. “SECURE” label is too broad: resource authorization still required separately |
| security.md:128-142 | Middleware order/CORS/input/output | Pass advice; respect target middleware and app boundary |
| security.md:144-162 | Injection/SSRF/serialization/files | Pass safe direction; no executable SSRF/DNS-rebinding implementation supplied or verified |
| security.md:164-188 | Browser cookies/secrets/rate limits/dependencies | Pass contextual advice and real built-in facilities; not full policy implementation |
| security.md:190-223 | Anti-patterns/threat modeling/checklist | Pass general security review direction; individual protections require app-specific testing |
| review.md:5-42 | Workflow/finding evidence/severity | Pass skill instruction design; concrete consequences required |
| review.md:44-70 | Correctness/API/security/EF/architecture | Pass scope checklist; does not force chosen architecture |
| review.md:72-102 | Perf/language/testing/dependencies | Pass contextual and evidence-oriented review guidance |
| review.md:104-132 | Anti-pattern signals | Needs qualification: static time calls, async void events, blocking boundaries, and licensing require context. Package watchlist facts delegated to parent source audit; not independently verified here |
| review.md:134-154 | Finding schema/example | Pass useful evidence fields; fixed exact format is a local output preference, subordinate to user/repo requirements |

## Every fenced block in the assigned files

Block numbers count every fenced block within the named file in document order (including text diagrams).

| File/block/line | Classification | Disposition |
|---|---|---|
| architecture 1:15 | Illustrative decision tree | Read; sensible conditional flow |
| architecture 2:43 | Illustrative layout | Read; optional layout |
| architecture 3:56 | Illustrative call flow | Read; optional patterns |
| architecture 4:119 | Positive interface | Compiles in Architecture proof |
| architecture 5:130 | Deliberate poor-abstraction counterexample | Compiles, but not scored as positive architecture |
| architecture 6:148 | Positive mapping | Compiles and From behavior check passes; EF SQL not run |
| architecture 7:183 | Positive DA registration | Compiles; supporting options included |
| architecture 8:190 | Positive options class | Compiles; invalid/valid source-generated validation check |
| architecture 9:207 | Positive validator registration/class | Compiles and validator accepts valid/rejects missing connection string |
| architecture 10:224 | Positive cross-field registration | Compiles; supporting StorageOptions added |
| csharp 1:38 | Positive primary constructor | Compiles; CS9113 unused clock |
| csharp 2:56 | Positive member fragment | Compiles in Named class; CS9264 null initialization |
| csharp 3:72 | Positive extensions | Compiles; Pending.IsOpen observed true |
| csharp 4:108 | Positive top-level collections | Compiles |
| csharp 5:230 | Positive JSON context | Compiles; real generated context serializes OrderResponse |
| csharp 6:242 | Positive JSON options registration | Compiles |
| observability 1:21 | Positive OTel registration | Compiles and registration construction runs with actual pinned packages |
| observability 2:38 | Positive metrics/traces | Compiles; S4 semantic issue |
| observability 3:61 | Positive generated logging | Compiles real LoggerMessage generator |
| observability 4:86 | Positive health mapping | Source-checked; AddNpgSql package/probe runtime Unverified |
| observability 5:107 | Positive writer/member fragment | Compiles and reproduced exception leak |
| observability 6:134 | Positive redaction registration | Source-checked incomplete; compile/runtime Unverified (missing redaction package) |
| hosted 1:21 | Positive periodic worker | Compiles with minimal handler; real loop execution not run |
| hosted 2:60 | Positive queue | Compiles; capacity/backpressure/cancel observed |
| hosted 3:80 | Positive queue worker | Compiles with WorkItem/IItemProcessor support; delivery semantics source-inspected |
| hosted 4:115 | Positive shutdown config | Compiles |
| performance 1:11 | Illustrative measurement loop | Read; sensible method |
| performance 2:115 | Illustrative benchmark fragment | API source-checked; omitted parser implementation and missing BDN package mean compile/run Unverified |
| security 1:32 | Positive fallback policy | Compiles with real framework authorization |
| security 2:47 | Positive GET | Fails unchanged; explicit-return control compiles |
| security 3:72 | Positive handler | Compiles; owner allowed / stranger denied |
| security 4:101 | Mixed deliberate unsafe and positive DTO/PUT | Deliberate `...` counterexample excluded. Positive PUT fails; explicit-return control compiles. DTO declaration moved below statements as normal snippet assembly support |
| review | No fenced code | All sections read |

## Reproducible proof artifacts and results

All proof artifacts are under `/tmp/dotnet-source-audit-proofs/`. No repository deps or skill files were edited.

For each directory, build command was `dotnet build <dir>/<name>.csproj --nologo`, with log redirected to that directory's `build.log`. Runs used `dotnet run --project <dir>/<name>.csproj --no-build` and `run.log`. These are small assert-based diagnostic executables, not xUnit/MSTest suites; do not report them as framework test counts.

| Proof group | Build | Runtime evidence |
|---|---|---|
| CSharp | Exit 0, 2 sample warnings (CS9113, CS9264), 0 errors | Exit 0; field trim, extension, collections compile, camelCase JSON |
| Architecture | Exit 0, 0 warnings/errors | Exit 0; missing/valid config validation and DTO mapping |
| Hosted | Exit 0, 0 warnings/errors | Exit 0; queue blocks at capacity and cancelled enqueue throws |
| Observability | Exit 0, 0 warnings/errors | Exit 0; health writer visibly exposes secret marker |
| SecurityHandler | Exit 0, 1 harness-only ASP0000 warning, 0 errors | Exit 0; owner allowed and stranger denied |
| OpenTelemetry | Exit 0, 0 warnings/errors | Exit 0; builder registration constructed, no collector/export verification |
| SecurityGet | Exit 1, 0 warnings, CS1593 | Not run (does not build) |
| SecurityPut | Exit 1, 0 warnings, CS1593 | Not run (does not build) |
| SecurityGetFixed | Exit 0, 0 warnings/errors | Corrective control only |
| SecurityPutFixed | Exit 0, 0 warnings/errors | Corrective control only |

The OTel proof initially used a project named OpenTelemetry without an explicit PackageId, causing a harness-only NU1108 self-package cycle. Corrected the harness identity to TelemetryProof, after which it restored/builds. This was not a skill defect. Pinned packages: OpenTelemetry.Extensions.Hosting 1.19.1, Instrumentation.AspNetCore 1.19.0, Instrumentation.Http 1.19.0, Instrumentation.Runtime 1.19.0, Exporter.OpenTelemetryProtocol 1.19.1. Supporting using directives, domain DTOs/interfaces, and package refs were added; positive snippet logic was preserved unless explicitly marked corrective control.

## Primary-source index

- C#14/API version facts: https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14 and https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning
- JSON generation: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
- Options/lifetimes: https://learn.microsoft.com/en-us/dotnet/core/extensions/options
- Options generator/binding AOT caveat: https://learn.microsoft.com/en-us/dotnet/core/extensions/options-validation-generator
- Scoped workers: https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service
- Hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0
- Channels: https://learn.microsoft.com/en-us/dotnet/core/extensions/channels
- Structured logging/redaction: https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation and https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction
- Metrics cardinality: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation
- Health output/protection: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0
- Resource authorization/registration: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0
- Frozen cost/fit: https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2?view=net-10.0
- SearchValues version: https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries
- HybridCache scope: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0
- EF benchmark counterexample: https://learn.microsoft.com/en-us/ef/core/performance/performance-diagnosis
- BDN diagnostics: https://benchmarkdotnet.org/articles/configs/diagnosers.html

Sources fetched via available web tool during this pass; source-only qualification is distinct from end-to-end verification. Versioned docs can include multiple-version sections; local compile/runtime evidence is the stronger version-specific check here.

Not checked: full Native AOT publish, external OTel exporter/collector, real PostgreSQL health probe, redaction runtime, BDN execution, complete browser/SSRF/file-security implementations, and end-to-end worker delivery/shutdown. These remain unverified; no overall compliance claim is justified.
