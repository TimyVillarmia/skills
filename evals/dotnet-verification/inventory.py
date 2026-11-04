"""Inventory source claims and code blocks without treating text matching as verification."""
import csv
import hashlib
import re
from pathlib import Path

here = Path(__file__).resolve().parent
root = here.parents[1]
skill = root / "skills/dotnet"
claims, blocks = [], []
for path in [skill / "SKILL.md", *sorted((skill / "references").glob("*.md"))]:
    lines = path.read_text().splitlines()
    section, paragraph, start, code, language = "", [], 0, None, ""
    frontmatter = path.name == "SKILL.md"
    for number, line in enumerate(lines, 1):
        if frontmatter:
            if number > 1 and line == "---":
                claims.append([str(path.relative_to(root)), 1, number, "Frontmatter",
                               " ".join(lines[:number]), "Fail", "quick_validate: description exceeds 1024 characters."])
                frontmatter = False
            continue
        fence = re.match(r"^[ \t]*```(.*)", line)
        if code is not None:
            if fence:
                content = "\n".join(code) + "\n"
                blocks.append([str(path.relative_to(root)), start, number, section, language,
                               hashlib.sha256(content.encode()).hexdigest(),
                               "Unverified", "Consult report and section reviews; inventory alone is not a pass."])
                code = None
            else:
                code.append(line)
            continue
        if paragraph and (not line.strip() or fence or line.startswith("#") or re.match(r"\s*(?:[-*]|\d+\.)\s|\|", line)):
            claims.append([str(path.relative_to(root)), start, number - 1, section,
                           " ".join(paragraph), "Unverified", "Text inventory; see claim-review.md and evidence reviews."])
            paragraph = []
        if fence:
            start, code, language = number, [], fence.group(1).strip()
        elif line.startswith("#"):
            section = line.lstrip("# ")
        elif line.strip() and line.strip() != "---":
            if not paragraph:
                start = number
            paragraph.append(line.strip())
    if paragraph:
        claims.append([str(path.relative_to(root)), start, len(lines), section, " ".join(paragraph),
                       "Unverified", "Text inventory; see claim-review.md and evidence reviews."])
    assert code is None, f"Unclosed code fence: {path}"

assert len({row[0] for row in claims}) == 15
assert len(blocks) == 92, f"Source changed: expected audited 92 blocks, got {len(blocks)}"
# Associate prose with the actual section reviews. A section verdict is not an
# independent runtime test of each sentence; the evidence column retains that scope.
ranges = []
for review in [here / "claim-review.md", here / "evidence/source-review.md"]:
    for number, line in enumerate(review.read_text().splitlines(), 1):
        match = re.match(r"\| (?:references/)?([\w-]+\.md):(\d+)-(\d+) \| (.*?) \| (.*?) \|", line)
        if not match:
            continue
        name, low, high, second, third = match.groups()
        assessment = second if review.name == "claim-review.md" else third
        verdict = next((v for v in ["Fail", "Needs qualification", "Unverified", "Pass"] if v in assessment), "Unverified")
        ranges.append((name, int(low), int(high), verdict, f"{review.relative_to(here)}:{number}: {assessment}"))
for row in claims:
    candidates = [r for r in ranges if Path(row[0]).name == r[0] and r[1] <= row[1] <= r[2]]
    if candidates:
        chosen = min(candidates, key=lambda r: r[2] - r[1])
        row[-2:] = [chosen[3], chosen[4]]

reviews = {
    "SKILL.md": "evidence/aot-review.md; claim-review.md",
    "api.md": "evidence/api-proof; evidence/testing-review.md; evidence/runtime-results.json; claim-review.md",
    "architecture.md": "evidence/source-review.md",
    "concurrency.md": "evidence/data-review.md",
    "csharp.md": "evidence/source-review.md; evidence/aot-review.md",
    "database-migrations.md": "evidence/migration-proof; evidence/postgres-results.txt; claim-review.md",
    "efcore.md": "evidence/data-review.md",
    "hosted-services.md": "evidence/source-review.md; evidence/forward-test.md",
    "messaging.md": "evidence/messaging-proof; claim-review.md",
    "multi-tenancy.md": "evidence/runtime-results.json; claim-review.md",
    "observability.md": "evidence/source-review.md",
    "performance.md": "evidence/source-review.md",
    "security.md": "evidence/source-review.md",
    "testing.md": "evidence/testing-review.md",
}
# Default compilation dispositions below refer only to the specific documented
# support/version choices in the linked reports, never universal correctness.
compiled = {"SKILL.md", "api.md", "architecture.md", "concurrency.md", "csharp.md",
            "database-migrations.md", "efcore.md", "hosted-services.md", "security.md", "testing.md"}
exceptions = {
    ("api.md", 149): ("Fail", "Compiles; runtime bypasses ProblemDetails customization/content type."),
    ("api.md", 188): ("Needs qualification", "Compiles with repository-style Result support; not generic ErrorOr compatibility."),
    ("api.md", 208): ("Fail", "Named Http package alone fails; companion API Explorer control compiles."),
    ("api.md", 284): ("Needs qualification", "Compiles; real cache runtime and distributed behavior unverified."),
    ("api.md", 322): ("Needs qualification", "Registration supported; standard unsafe POST retries reproduced separately."),
    ("concurrency.md", 38): ("Fail", "Compiles and excludes same-key writers; retains every distinct key."),
    ("concurrency.md", 105): ("Needs qualification", "SQL Server rowversion proved; xmin requires different provider mapping."),
    ("database-migrations.md", 81): ("Fail", "Compiles; actual NOT VALID statement holds a table lock."),
    ("database-migrations.md", 109): ("Unverified", "Template CLI path absent; no application migration script executed."),
    ("database-migrations.md", 116): ("Unverified", "No application migration bundle generated/deployed."),
    ("database-migrations.md", 130): ("Fail", "Original SQL updated12000 rows in one transaction, not committed batches."),
    ("efcore.md", 155): ("Needs qualification", "Illustrative catch skeleton compiles; no-conflict path only; not a complete translation."),
    ("messaging.md", 38): ("Needs qualification", "Compiles; publishing fails without missing outbox model entities."),
    ("messaging.md", 63): ("Unverified", "Publish pattern source-reviewed; exact wrapped handler not compiled/executed."),
    ("messaging.md", 92): ("Needs qualification", "Consumer compiles; boolean guard alone is not atomic effect deduplication."),
    ("messaging.md", 122): ("Unverified", "Retry API source-reviewed; exact code not executed with broker."),
    ("messaging.md", 143): ("Unverified", "Missing consumer DbContext fixture; no original harness/durable-broker run."),
    ("multi-tenancy.md", 11): ("Fail", "Compiles; accepts arbitrary header on authenticated identity missing tenant claim."),
    ("multi-tenancy.md", 65): ("Pass", "Marker/entity compiled as support for real SQL proof."),
    ("multi-tenancy.md", 82): ("Fail", "Compiles; freshB context returnsA data with shared model."),
    ("multi-tenancy.md", 114): ("Fail", "Compiles; attached foreign-row update/unresolved insert both persist."),
    ("multi-tenancy.md", 154): ("Fail", "Compiles; missing tenant mapping selects shared fallback."),
    ("multi-tenancy.md", 176): ("Unverified", "Trivial key formation reviewed; exact combined cache fragment not run."),
    ("multi-tenancy.md", 192): ("Unverified", "Tenant payload/DI trust setup absent; exact consumer not executed."),
    ("observability.md", 21): ("Pass", "Actual OTel packages compile/register; no exporter end-to-end check."),
    ("observability.md", 38): ("Needs qualification", "Compiles; unbounded customer metric labels and premature count."),
    ("observability.md", 61): ("Pass", "Actual generated LoggerMessage code compiles."),
    ("observability.md", 86): ("Unverified", "Npgsql health registration source-reviewed; probe not run."),
    ("observability.md", 107): ("Fail", "Compiles/runs; raw exception marker appears in health JSON."),
    ("observability.md", 134): ("Unverified", "Registration alone cannot guarantee redaction; exact package/runtime unverified."),
    ("performance.md", 115): ("Unverified", "Illustrative BDN fragment lacks parser support; benchmark not run."),
    ("security.md", 47): ("Fail", "Original GET failsCS1593; explicit-return control builds."),
    ("security.md", 101): ("Fail", "Positive PUT failsCS1593; explicit-return control builds; mixed unsafe example excluded."),
    ("testing.md", 36): ("Needs qualification", "JSON parses; historical SDK3.8.2 not restored; current4.5.1 exercised."),
    ("testing.md", 46): ("Needs qualification", "Project shape works with supplied CPM versions and current4.5.1."),
    ("testing.md", 71): ("Needs qualification", "xunit3.2.2 project works with supplied versions/native runner; filters differ."),
    ("testing.md", 174): ("Fail", "Respawn7 two compile errors; Respawn6 PostgreSQL string-overload exception."),
    ("testing.md", 234): ("Needs qualification", "Compiles with support; actual fixture-to-application persistence unverified."),
}
negative = {("architecture.md", 130), ("efcore.md", 85), ("messaging.md", 11)}
for row in blocks:
    name, line = Path(row[0]).name, row[1]
    row[-1] = reviews[name]
    if row[4] == "text":
        row[-2] = "Illustrative"
        row[-1] += ": diagram/decision flow, no runtime claim."
    elif (name, line) in negative:
        row[-2] = "Counterexample"
        row[-1] += ": deliberately negative, excluded from positive compilation gate."
    elif (name, line) in exceptions:
        row[-2], detail = exceptions[(name, line)]
        row[-1] += ": " + detail
    elif name in compiled:
        row[-2] = "Pass"
        row[-1] += ": compiles with documented support; consult report for runtime limits."
for name, header, rows in [
    ("claim-inventory.tsv", ["file", "start", "end", "section", "text", "verdict", "evidence"], claims),
    ("code-inventory.tsv", ["file", "start", "end", "section", "language", "sha256", "verdict", "evidence"], blocks),
]:
    with (here / name).open("w", newline="") as stream:
        writer = csv.writer(stream, delimiter="\t", lineterminator="\n")
        writer.writerow(header)
        writer.writerows(rows)
print(f"Inventoried 15 files, {len(claims)} prose units, {len(blocks)} code blocks")
