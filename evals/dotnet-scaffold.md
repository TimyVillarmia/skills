# .NET scaffold skill evaluations

These cases are representative workflow and behavior checks for `.agents/skills/dotnet-scaffold`.

## Positive activation

The `dotnet-scaffold` skill should be relevant for:

1. "Create a new .NET Web API solution for a small application."
2. "Add a worker project to this existing .NET solution."
3. "Scaffold a new Orders feature in this existing .NET API."
4. "Add a complete Product vertical slice with endpoint, validation, persistence, and tests."

Expected behavior:

- Inspect repository instructions, SDK/framework versions, architecture, dependencies, and representative existing features first.
- Choose the smallest coherent project or feature shape.
- Reuse established repository conventions.
- Use the `dotnet` skill for general .NET engineering guidance when available rather than duplicating it.
- Verify generated code rather than treating file generation as completion.

## Existing-project behavior

> "Add a Customers feature to this existing API. The project uses controllers, MediatR, and FluentValidation."

Expected behavior:

- Preserve the existing architecture and dependencies.
- Do not replace controllers with Minimal APIs or introduce a different mediator merely because personal defaults prefer them.
- Generate only the components required by the request.

## Aspire composition

> "Add a new service to my Aspire application."

Expected behavior:

- Use the official Aspire Agent Skill for Aspire-specific workflow and current Aspire APIs when available.
- Keep this skill responsible for deciding what needs to be scaffolded and for general .NET scaffolding concerns.
- Do not duplicate Aspire-specific documentation in this skill.

## Negative activation

The `dotnet-scaffold` skill should not be the primary skill for:

1. "Review this existing C# pull request without adding anything."
2. "Optimize this EF Core query."
3. "Explain how ASP.NET Core authentication works."
4. "Fix this Python project."

## Completeness

For a generated feature, check that applicable pieces are wired together and verified:

- project/feature structure
- API surface
- application/use-case logic
- validation
- DTO/contracts
- mapping
- persistence
- dependency injection/registration
- tests
- configuration/OpenAPI where applicable
- restore/build/tests or the appropriate verification subset
- no unintended dependencies or changes
