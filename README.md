# Agent Skills

Curated, tool-agnostic AI agent skills for modern software engineering and architecture. Designed for use with Cursor, Claude Code, Antigravity, GitHub Copilot, and any LLM coding assistant supporting agent skills.

## Installation

### Via `npx skills` ([skills.sh](https://skills.sh))

Install the entire skills collection into your current workspace:

```bash
npx skills add TimyVillarmia/skills
```

Or install individual skills:

```bash
# Core .NET engineering skill (C#, EF Core, API, architecture, performance, security)
npx skills add TimyVillarmia/skills/dotnet

# .NET solution, project, and vertical slice scaffolding
npx skills add TimyVillarmia/skills/dotnet-scaffold
```

### Manual Installation

Clone or copy the desired skill directory into your project's `.agents/skills/` or `~/.agents/skills/`:

```bash
git clone git@github.com:TimyVillarmia/skills.git ~/.agents/skills/personal
```

---

## Included Skills

### 1. `dotnet`
Comprehensive engineering rules and task routing for modern .NET and C#:
- **`csharp.md`:** C# language idioms, async best practices, ValueTask, nullable references, and pattern matching.
- **`api.md`:** ASP.NET Core Minimal APIs, controllers, OpenAPI contracts, and HTTP status code semantics.
- **`efcore.md`:** EF Core query optimization, DbContext lifetimes, N+1 detection, and split queries.
- **`architecture.md`:** Vertical Slice architecture, CQRS boundaries, and domain encapsulation.
- **`performance.md`:** Allocation auditing, Span/Memory usage, and BenchmarkDotNet standards.
- **`security.md`:** Authentication, authorization, input validation, and cryptographic safety.
- **`review.md`:** Structured code review checklists for .NET pull requests.

### 2. `dotnet-scaffold`
Deterministic scaffolding for new .NET solutions, projects, and vertical slices:
- Standard solution layouts and project dependency trees.
- Feature slicing (endpoint, command/query, handler, validator, unit test).
- Solution filter (`.slnf`) and Central Package Management (`Directory.Packages.props`) alignment.

---

## Evaluations

Skill routing and trigger behavior are validated using test matrices under `evals/`:
- `trigger-queries.json`: Evaluates query classification accuracy across different intent patterns.
- `trigger-results.md`: Benchmarked activation rates and pass/fail metrics.

---

## License

MIT
