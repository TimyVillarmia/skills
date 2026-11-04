# Native AOT endpoint and JSON proof

Date: 2026-11-04. SDK 10.0.302, linux-x64; final runtime/ILCompiler packs 10.0.13. Default Native AOT targets automatically selected installed GCC/ld when clang was absent. No skill or repository files modified.

## Result

Native publish **passed**, followed by **six passing assert-based smoke checks** against the native executable. This establishes that the documented static MapGroup pattern and csharp JsonSerializerContext can work together with explicitly AOT-compatible supporting code. It does not establish “100%” safety for arbitrary handlers, serializers, libraries, or runtime dependencies.

Publish: `dotnet publish /tmp/dotnet-aot-proof/AotProof.csproj -c Release -r linux-x64 --no-restore --nologo`, exit 0. Final full output is `publish-final.log`. Zero C# errors, no IL/AOT/trimming diagnostics. One unique NU1903 high-severity vulnerability warning for Microsoft.DotNet.ILCompiler 10.0.13 remains; it was not suppressed. The earlier cached 10.0.10 pack reported the same advisory. Do not present this as a clean zero-warning publish or recommend deploying this exact compiler pack.

Smoke: `python3 /tmp/dotnet-aot-proof/smoke.py`, exit 0; output `smoke.log`, native server output `native.log`. Starts on an OS-selected loopback port, shuts down its own process afterward, uses ephemeral data-protection keys.

1. Anonymous request rejected by the unchanged route group's RequireAuthorization: 401.
2. Authenticated collection endpoint serializes generated metadata: 200.
3. Original typed GetOrderById body returns actual injected service data: 200.
4. Original typed GetOrderById not-found branch: 404.
5. Supporting POST endpoint binds generated request metadata, returns Created JSON and Location: 201.
6. Original csharp JsonSerializerContext options and ConfigureHttpJsonOptions registration work; camelCase fields observed from the native binary.

The executable begins with ELF magic and `file` identifies native x86-64 ELF. `ldd` shows libc/libm/native loader dependencies, not a managed dotnet host.

## Supporting additions, explicitly separated from audited source

- Project enables PublishAot, targets net10.0 with RuntimeFrameworkVersion 10.0.13, and InvariantGlobalization for a self-contained test.
- Used WebApplication.CreateSlimBuilder and explicitly registered authentication, authorization, and a singleton IOrderService implementation.
- Main SKILL.md OrderEndpoints MapGroup and GetOrderById body copied unchanged. Added GetOrders/CreateOrder methods referred to but absent in the excerpt. Added OrderDto, CreateOrderRequest, OrderResponse, and IOrderService/domain implementation.
- Original csharp JSON context retained for CreateOrderRequest/OrderResponse; added JsonSerializable metadata for OrderDto and OrderDto[] because those supporting handlers return these types.
- Added a framework-only, test-specific authentication handler. It recognizes a fixed test credential only on the loopback proof server; it is not a production identity example.
- Added ephemeral DataProtection provider to keep test keys off the user profile.

## Execution limitations and failures

First sandbox publish failed MSB4216 because MSBuild could not communicate with its compiler task host. The escalated publish passed. Fetching 10.0.13 packs first failed NU1301/permission denied under sandbox; escalated restore/publish passed. First sandbox smoke failed to create a socket (permission denied); escalated loopback smoke passed. These were environment restrictions, not snippet defects.

The first cached10.0.10 publish succeeded; the final proof upgraded to10.0.13 rather than relying on the older cache. NU1903 was still emitted by that current installed runtime patch's compiler package. No benchmark was run: the skill's zero-allocation assertion remains unsupported. No full EF, mapper, OpenAPI, third-party auth, or arbitrary dependency graph was included in this narrow native proof.
