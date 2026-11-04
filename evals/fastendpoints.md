# FastEndpoints Skill Evaluations

These cases are representative activation and behavior checks for `skills/fastendpoints`.

## Positive activation

The `fastendpoints` skill should be relevant for:

1. "Create a new endpoint using FastEndpoints for user registration."
2. "Refactor this controller action into a FastEndpoints REPR vertical slice."
3. "How do I configure FluentValidation in FastEndpoints to auto-return 400 ProblemDetails?"
4. "Add permission-based security to this FastEndpoints endpoint class."
5. "Write an integration test for CreateOrderEndpoint using FastEndpoints.Testing and AppFixture."
6. "How do I stream server-sent events (SSE) in FastEndpoints?"

Expected behavior:
- Apply the REPR (Request-Endpoint-Response) pattern with class-per-endpoint vertical slices.
- Configure routes, verbs, and permissions inside `Configure()`.
- Use built-in response dispatch (`SendOkAsync`, `SendCreatedAtAsync`, `SendNotFoundAsync`).
- Integrate validation via `Validator<TRequest>`.
- Use `FastEndpoints.Testing` with typed `POSTAsync`/`GETAsync` extensions instead of hardcoded route strings.

## .NET Skill Handoff

When a task involves general .NET concerns (EF Core queries, DbContext modeling, Dockerfiles, MSBuild), the agent should coordinate with the `dotnet` skill:

1. "Create a FastEndpoints endpoint that queries an Order using EF Core and handles concurrency."
   - Handled: `fastendpoints` for endpoint structure, DTOs, and validation; `dotnet` reference `efcore.md` for DbContext concurrency and tracking behavior.

## Negative activation

The `fastendpoints` skill should not be the primary skill for:

1. Pure Minimal API lambda endpoints without FastEndpoints dependencies.
2. Legacy ASP.NET Core MVC / Razor Pages / Blazor.
3. General C# language or EF Core persistence tasks with no HTTP endpoint surface.
