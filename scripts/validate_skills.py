#!/usr/bin/env python3
"""Validate Agent Skills under skills/ against agentskills.io and progressive-disclosure rules."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

NAME_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
FRONTMATTER_RE = re.compile(r"^---\r?\n(.*?)\r?\n---\r?\n", re.S)
REF_MENTION_RE = re.compile(r"references/([A-Za-z0-9_.-]+\.md)")
MD_LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)\)")
HEADING_RE = re.compile(r"^#{1,6}\s+(.+?)\s*$")
FENCE_RE = re.compile(r"^\s*(`{3,}|~{3,})")
INLINE_CODE_RE = re.compile(r"`[^`]+`")
LATEX_BLOCK_RE = re.compile(r"^\s*\$\$")
LATEX_INLINE_RE = re.compile(r"(?<!\$)\$(?!\$)(?:\\[A-Za-z]+|[^$\n]*\\[A-Za-z]+)[^$\n]*\$")

MAX_NAME_LEN = 64
MAX_DESC_LEN = 1024
MAX_SKILL_LINES = 500
MAX_SKILL_CHARS = 20_000
TOC_LINE_THRESHOLD = 300


def parse_frontmatter(text: str) -> tuple[dict[str, str], str] | None:
    """Extract top-level YAML frontmatter key/value pairs and remaining body."""
    match = FRONTMATTER_RE.match(text)
    if not match:
        return None

    raw = match.group(1)
    body = text[match.end() :]
    fields: dict[str, str] = {}
    lines = raw.splitlines()
    i = 0
    while i < len(lines):
        line = lines[i]
        if not line.strip() or line.lstrip().startswith("#"):
            i += 1
            continue
        if line[0].isspace():
            i += 1
            continue
        if ":" not in line:
            i += 1
            continue

        key, val = line.split(":", 1)
        key = key.strip()
        val = val.strip()

        if val in (">", ">-", "|", "|-"):
            block_lines: list[str] = []
            i += 1
            while i < len(lines) and (lines[i].startswith((" ", "\t")) or not lines[i].strip()):
                block_lines.append(lines[i].strip())
                i += 1
            joiner = " " if val.startswith(">") else "\n"
            fields[key] = joiner.join(ln for ln in block_lines if ln).strip()
            continue

        if (val.startswith('"') and val.endswith('"')) or (val.startswith("'") and val.endswith("'")):
            val = val[1:-1]
        fields[key] = val
        i += 1

    return fields, body


def github_slug(heading: str) -> str:
    """Convert a Markdown heading to a GitHub-compatible anchor slug."""
    cleaned = heading.strip()
    cleaned = re.sub(r"`([^`]*)`", r"\1", cleaned)
    cleaned = re.sub(r"[*_~]", "", cleaned)
    cleaned = cleaned.lower()
    cleaned = re.sub(r"[^\w\s-]", "", cleaned)
    cleaned = cleaned.replace(" ", "-")
    return cleaned


def collect_anchors(text: str) -> set[str]:
    """Collect GitHub-style heading anchors outside fenced code blocks."""
    anchors: set[str] = set()
    counts: dict[str, int] = {}
    for _, line in iter_non_fenced_lines(text):
        m = HEADING_RE.match(line)
        if not m:
            continue
        base = github_slug(m.group(1))
        if base in counts:
            counts[base] += 1
            anchors.add(f"{base}-{counts[base]}")
        else:
            counts[base] = 0
            anchors.add(base)
    return anchors


def iter_non_fenced_lines(text: str):
    """Yield (1-based line_no, line) for lines outside fenced code blocks."""
    fence_char: str | None = None
    fence_len = 0
    for idx, line in enumerate(text.splitlines(), 1):
        m = FENCE_RE.match(line)
        if m:
            marker = m.group(1)
            if fence_char is None:
                fence_char = marker[0]
                fence_len = len(marker)
                continue
            if marker[0] == fence_char and len(marker) >= fence_len and line.strip() == marker:
                fence_char = None
                fence_len = 0
                continue
        if fence_char is None:
            yield idx, line


def check_balanced_fences(text: str) -> int | None:
    """Return opening line number of an unclosed fenced code block, or None if balanced."""
    fence_char: str | None = None
    fence_len = 0
    opened_at: int | None = None
    for idx, line in enumerate(text.splitlines(), 1):
        m = FENCE_RE.match(line)
        if not m:
            continue
        marker = m.group(1)
        if fence_char is None:
            fence_char = marker[0]
            fence_len = len(marker)
            opened_at = idx
        elif marker[0] == fence_char and len(marker) >= fence_len and line.strip() == marker:
            fence_char = None
            fence_len = 0
            opened_at = None
    return opened_at


def validate_skill(skill_dir: Path, repo_root: Path) -> tuple[list[str], list[str], dict[str, int | str]]:
    """Validate a single skill directory. Returns (errors, warnings, stats)."""
    errors: list[str] = []
    warnings: list[str] = []
    skill_md = skill_dir / "SKILL.md"
    rel_skill = skill_md.relative_to(repo_root)

    if not skill_md.is_file():
        errors.append(f"{skill_dir.relative_to(repo_root)}: missing SKILL.md")
        return errors, warnings, {"name": skill_dir.name, "lines": 0, "tokens": 0, "refs": 0}

    text = skill_md.read_text(encoding="utf-8")
    lines = text.splitlines()
    parsed = parse_frontmatter(text)
    if parsed is None:
        errors.append(f"{rel_skill}:1: missing or malformed YAML frontmatter")
        return errors, warnings, {"name": skill_dir.name, "lines": len(lines), "tokens": len(text) // 4, "refs": 0}

    fields, _ = parsed
    name = fields.get("name", "")
    desc = fields.get("description", "")

    if not name:
        errors.append(f"{rel_skill}: missing required frontmatter 'name'")
    else:
        if len(name) > MAX_NAME_LEN:
            errors.append(f"{rel_skill}: 'name' exceeds {MAX_NAME_LEN} chars ({len(name)})")
        if not NAME_RE.match(name):
            errors.append(f"{rel_skill}: 'name' {name!r} must match ^[a-z0-9]+(-[a-z0-9]+)*$")
        if name != skill_dir.name:
            errors.append(f"{rel_skill}: 'name' {name!r} does not match directory {skill_dir.name!r}")

    if not desc:
        errors.append(f"{rel_skill}: missing required frontmatter 'description'")
    elif len(desc) > MAX_DESC_LEN:
        errors.append(f"{rel_skill}: 'description' exceeds {MAX_DESC_LEN} chars ({len(desc)})")

    if len(lines) >= MAX_SKILL_LINES:
        errors.append(f"{rel_skill}: {len(lines)} lines (must be < {MAX_SKILL_LINES})")
    if len(text) >= MAX_SKILL_CHARS:
        errors.append(f"{rel_skill}: {len(text)} chars / ~{len(text)//4} tokens (must be < {MAX_SKILL_CHARS} chars)")

    # References reachability
    refs_dir = skill_dir / "references"
    refs_on_disk = {p.name for p in refs_dir.glob("*.md")} if refs_dir.is_dir() else set()
    refs_mentioned = set(REF_MENTION_RE.findall(text))

    for orphan in sorted(refs_on_disk - refs_mentioned):
        errors.append(f"{skill_dir.relative_to(repo_root)}/references/{orphan}: not referenced in SKILL.md")
    for missing in sorted(refs_mentioned - refs_on_disk):
        errors.append(f"{rel_skill}: references missing file 'references/{missing}'")

    # Per-file checks across SKILL.md and references/*.md
    md_files = [skill_md] + (sorted(refs_dir.glob("*.md")) if refs_dir.is_dir() else [])
    anchor_cache: dict[Path, set[str]] = {}

    for md in md_files:
        md_text = md.read_text(encoding="utf-8")
        rel_md = md.relative_to(repo_root)

        unclosed = check_balanced_fences(md_text)
        if unclosed is not None:
            errors.append(f"{rel_md}:{unclosed}: unclosed fenced code block")

        if md.parent.name == "references" and len(md_text.splitlines()) > TOC_LINE_THRESHOLD:
            if "## Contents" not in md_text:
                errors.append(f"{rel_md}: >{TOC_LINE_THRESHOLD} lines ({len(md_text.splitlines())}) without '## Contents' ToC")

        for line_no, line in iter_non_fenced_lines(md_text):
            stripped_line = INLINE_CODE_RE.sub("", line)

            if LATEX_BLOCK_RE.match(stripped_line) or LATEX_INLINE_RE.search(stripped_line):
                warnings.append(f"{rel_md}:{line_no}: LaTeX math syntax in prompt Markdown; prefer plain text")

            for target in MD_LINK_RE.findall(stripped_line):
                if target.startswith(("http://", "https://", "mailto:")):
                    continue
                if "#" in target:
                    path_part, anchor = target.split("#", 1)
                else:
                    path_part, anchor = target, ""

                target_path = md if not path_part else (md.parent / path_part).resolve()
                if not target_path.exists():
                    errors.append(f"{rel_md}:{line_no}: broken relative link {target!r}")
                    continue

                if anchor and target_path.suffix == ".md":
                    if target_path not in anchor_cache:
                        anchor_cache[target_path] = collect_anchors(target_path.read_text(encoding="utf-8"))
                    if anchor not in anchor_cache[target_path]:
                        errors.append(f"{rel_md}:{line_no}: broken anchor #{anchor} in {target!r}")

    stats = {
        "name": skill_dir.name,
        "lines": len(lines),
        "tokens": len(text) // 4,
        "refs": len(refs_on_disk),
    }
    return errors, warnings, stats


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate skills under skills/ directory.")
    parser.add_argument(
        "--skills-dir",
        type=Path,
        default=Path(__file__).resolve().parent.parent / "skills",
        help="Path to the skills/ directory (default: repo skills/)",
    )
    args = parser.parse_args()
    skills_dir: Path = args.skills_dir.resolve()
    repo_root = skills_dir.parent

    if not skills_dir.is_dir():
        print(f"ERROR: skills directory not found: {skills_dir}", file=sys.stderr)
        return 1

    skill_dirs = sorted(p for p in skills_dir.iterdir() if p.is_dir() and not p.name.startswith("."))
    if not skill_dirs:
        print(f"ERROR: no skill directories found under {skills_dir}", file=sys.stderr)
        return 1

    all_errors: list[str] = []
    all_warnings: list[str] = []

    print(f"{'Skill':<20} {'Lines':>6} {'~Tokens':>8} {'Refs':>5}  Status")
    print("-" * 54)
    for sdir in skill_dirs:
        errs, warns, stats = validate_skill(sdir, repo_root)
        all_errors.extend(errs)
        all_warnings.extend(warns)
        status = "FAIL" if errs else ("WARN" if warns else "OK")
        print(f"{stats['name']:<20} {stats['lines']:>6} {stats['tokens']:>8} {stats['refs']:>5}  {status}")

    if all_warnings:
        print("\nWarnings:")
        for w in all_warnings:
            print(f"  WARN: {w}")

    if all_errors:
        print("\nErrors:")
        for e in all_errors:
            print(f"  ERROR: {e}")
        return 1

    print(f"\nPassed: {len(skill_dirs)} skill(s) validated with 0 errors.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
