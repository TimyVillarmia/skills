# .NET skill evaluations

These cases are representative activation and behavior checks for `.agents/skills/dotnet`.

## Positive activation

The `dotnet` skill should be relevant for:

1. "Fix this ASP.NET Core API endpoint returning the wrong status code."
2. "Optimize this EF Core query that is producing too many database calls."
3. "Review this C# service for correctness and maintainability."
4. "Add authorization to this .NET API endpoint."
5. "Debug this .NET background service that stops processing after an exception."
6. "Decide whether this new .NET application should use CQRS or a simpler application structure."

Expected behavior:

- Inspect repository instructions and existing conventions before changing code.
- Classify the task and read only relevant references.
- Preserve existing architecture and dependencies unless a change is justified.
- Validate the result proportionally to risk.

## Aspire handoff

The `dotnet` skill should remain relevant for the general engineering aspects of these tasks, but should defer Aspire-specific workflow and current API details to the official Aspire Agent Skill when available:

1. "Add a Redis resource to my Aspire AppHost."
2. "Configure PostgreSQL in my .NET Aspire application."
3. "Update the Aspire AppHost to add a new service and connect it to the API."

Expected behavior:

- Do not reproduce a second Aspire-specific knowledge base inside the .NET skill.
- Use the official Aspire skill for Aspire-specific workflows and APIs.
- Continue applying general .NET engineering guidance around architecture, APIs, persistence, security, testing, and dependencies.

## Negative activation

The `dotnet` skill should not be the primary skill for tasks that are clearly unrelated to .NET/C# engineering:

1. "Fix this Python FastAPI endpoint."
2. "Refactor this React component."
3. "Configure an Nginx reverse proxy."
4. "Write a Bash script to rotate logs."

## Existing-project behavior

For a project that already uses a different architecture, mediator, mapper, Result library, or endpoint organization:

> "Add a new endpoint to this existing .NET API. It uses MediatR and controllers everywhere else."

Expected behavior:

- Follow the existing project architecture and dependencies.
- Do not rewrite controllers into Minimal APIs or introduce a new architecture merely because the skill prefers them for greenfield work.
- Make the smallest change consistent with the repository.

## Greenfield behavior

For a genuinely new application:

> "Create a new .NET API for a small application with several independent business operations."

Expected behavior:

- Establish requirements and boundaries before selecting architecture.
- Personal defaults may be considered when they provide concrete value.
- Do not add dependencies or architecture merely because a template or preference exists.

## Security and operational regression checks

Compile examples with the target SDK and actual packages, then check these observable outcomes:

- Tenant writes: attach another tenant's row ID with the caller's tenant value. Both synchronous and asynchronous updates/deletes must fail without changing the foreign row. Own-tenant writes must succeed; unresolved contexts must reject writes. Keep the same cached EF model while alternating tenants.
- HTTP resilience: a transiently failing POST is attempted once under the sample policy; a GET retains standard retries.
- Consumer outbox: deliver the same message ID twice within the detection window against real persistence. The configured receive endpoint must commit one consumer database effect.
- PostgreSQL concurrency: compile the timestamp mapping against the installed Npgsql provider and verify a stale update is rejected.
- Public probes: place secret markers in check names, descriptions, data and exceptions. The response must contain only aggregate status.
- Foreign-key validation: commit the NOT VALID migration before validation. A separate writer must continue while the validation transaction holds its locks.
- Focused tests: run the documented MSTest and xUnit commands in their actual SDK modes and confirm the intended nonzero selected test set.
