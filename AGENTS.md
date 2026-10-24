# Agent routing

This repository contains portable personal AI-agent skills under `skills/`.

## Routing

- For .NET engineering work, use the `dotnet` skill.
- For creating or scaffolding .NET solutions, projects, or features, use `dotnet-scaffold` together with `dotnet`.
- Load only the references relevant to the task; do not duplicate their guidance here.

## Precedence

Repository/project-specific instructions and established conventions take precedence over global personal defaults. Explicit task requirements and security/correctness constraints take precedence over both.

## Tooling

Skills are tool-agnostic. Do not assume a specific MCP server, plugin, hook system, or coding-agent feature is available unless the current environment provides it.

<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->
