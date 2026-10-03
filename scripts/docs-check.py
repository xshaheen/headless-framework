#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Validate docs/solutions/ learnings and keep their index current.

Agents find past fixes and decisions by grepping solution frontmatter (`tags:.*redis`,
`module:.*Caching`) and by reading docs/solutions/INDEX.md. Both only work when every doc carries
the same frontmatter shape, so the schema problems this reports fail CI. Broken references (a
moved source file, a renamed doc) are reported as warnings: they go stale whenever src/ is
refactored, and blocking an unrelated rename on them would punish the wrong change.

The frontmatter contract is the field, enum, and track definitions below plus these rules: `tags` is one
inline list so a single-line grep sees every tag, `module` names a real src project, and the
concurrency/, api/ and messaging/ area directories are allowed beside the problem-type ones.

Standard library only; the parser accepts the YAML subset the schema needs and reports anything
else as an error rather than guessing.
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
SOLUTIONS = REPO_ROOT / "docs" / "solutions"
INDEX = SOLUTIONS / "INDEX.md"

BUG_TYPES = {
    "build_error": "build-errors",
    "test_failure": "test-failures",
    "runtime_error": "runtime-errors",
    "performance_issue": "performance-issues",
    "database_issue": "database-issues",
    "security_issue": "security-issues",
    "ui_bug": "ui-bugs",
    "integration_issue": "integration-issues",
    "logic_error": "logic-errors",
}
KNOWLEDGE_TYPES = {
    "best_practice": "best-practices",
    "documentation_gap": "documentation-gaps",
    "workflow_issue": "workflow-issues",
    "developer_experience": "developer-experience",
    "architecture_pattern": "architecture-patterns",
    "design_pattern": "design-patterns",
    "tooling_decision": "tooling-decisions",
    "convention": "conventions",
}
# Area directories that predate the schema's problem-type layout and group docs by subsystem.
AREA_DIRECTORIES = {"concurrency", "api", "messaging"}
SEVERITIES = {"critical", "high", "medium", "low"}
RESOLUTION_TYPES = {
    "code_fix",
    "migration",
    "config_change",
    "test_fix",
    "dependency_update",
    "environment_setup",
    "workflow_improvement",
    "documentation_update",
    "tooling_addition",
    "seed_data_update",
}
REQUIRED = ("module", "date", "problem_type", "component", "severity")
BUG_REQUIRED = ("symptoms", "root_cause", "resolution_type")
LIST_LIMITS = {"tags": (0, 8), "symptoms": (0, 5), "applies_when": (0, 5)}
KNOWN_KEYS = {
    "title",
    "category",
    "date",
    "last_updated",
    "module",
    "problem_type",
    "component",
    "severity",
    "symptoms",
    "root_cause",
    "resolution_type",
    "applies_when",
    "related_components",
    "tags",
    "retire_when",
    "dotnet_version",
}
# `module` values for learnings that belong to no single src project.
CROSS_CUTTING_MODULES = {"headless-framework", "build", "ci", "docs"}
# Planning and review artifacts are deleted once their work lands; durable docs must not cite them.
DISPOSABLE_DOCS = ("docs/plans/", "docs/brainstorms/", "docs/test-plans/", "docs/reviews/")
KEBAB = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
LINK = re.compile(r"\]\(([^)\s]+)\)")
CODE_PATH = re.compile(r"`((?:src|tests|docs|eng|demo|benchmarks|scripts)/[^`\s]+?)`")


@dataclass
class Doc:
    path: Path
    meta: dict[str, object] = field(default_factory=dict)
    inline_lists: set[str] = field(default_factory=set)
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    body: str = ""

    @property
    def rel(self) -> str:
        return self.path.relative_to(REPO_ROOT).as_posix()

    @property
    def title(self) -> str:
        if isinstance(self.meta.get("title"), str) and self.meta["title"]:
            return str(self.meta["title"])
        heading = re.search(r"^# (.+)$", self.body, re.MULTILINE)
        return heading.group(1).strip() if heading else self.path.stem


def unquote(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1]
    return value


def split_flow_list(text: str) -> list[str]:
    items, current, quote = [], "", ""
    for char in text:
        if quote:
            current += char
            if char == quote:
                quote = ""
        elif char in "\"'":
            quote = char
            current += char
        elif char == ",":
            items.append(unquote(current))
            current = ""
        else:
            current += char
    if current.strip():
        items.append(unquote(current))
    return [item for item in items if item]


def parse(path: Path) -> Doc:
    doc = Doc(path)
    text = path.read_text(encoding="utf-8")
    if not text.startswith("---\n"):
        doc.errors.append("no YAML frontmatter (the file must start with a --- line)")
        doc.body = text
        return doc
    end = text.find("\n---\n", 4)
    if end < 0:
        doc.errors.append("frontmatter is not closed by a --- line")
        return doc
    doc.body = text[end + 5 :]
    lines = text[4:end].split("\n")
    index = 0
    while index < len(lines):
        line = lines[index]
        index += 1
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        match = re.match(r"^([A-Za-z_][\w-]*):(?:\s+(.*))?$", line)
        if not match:
            doc.errors.append(f"frontmatter line not understood: {line!r}")
            continue
        key, value = match.group(1), (match.group(2) or "").strip()
        if key in doc.meta:
            doc.errors.append(f"duplicate key `{key}`")
        if value.startswith("["):
            if not value.endswith("]"):
                doc.errors.append(f"`{key}` flow list must close on the same line")
            doc.meta[key] = split_flow_list(value[1:-1])
            doc.inline_lists.add(key)
        elif value in (">", "|", ">-", "|-"):
            block: list[str] = []
            while index < len(lines) and (lines[index].startswith("  ") or not lines[index].strip()):
                block.append(lines[index].strip())
                index += 1
            doc.meta[key] = " ".join(part for part in block if part)
        elif value:
            doc.meta[key] = unquote(value)
        else:
            items: list[str] = []
            nested = False
            while index < len(lines) and (lines[index].startswith((" ", "-")) or not lines[index].strip()):
                item = lines[index].strip()
                index += 1
                if not item:
                    continue
                if item.startswith("- "):
                    items.append(unquote(item[2:]))
                else:
                    nested = True
            doc.meta[key] = {"nested": True} if nested and not items else items
    return doc


def src_modules() -> set[str]:
    return {path.name for path in (REPO_ROOT / "src").iterdir() if path.is_dir() and path.name.startswith("Headless.")}


def check_schema(doc: Doc, modules: set[str]) -> None:
    meta = doc.meta
    if not meta:
        return
    for key in meta:
        if key not in KNOWN_KEYS:
            doc.errors.append(f"unknown key `{key}` (allowed: {', '.join(sorted(KNOWN_KEYS))})")
    for key in REQUIRED:
        if not meta.get(key):
            doc.errors.append(f"missing required `{key}`")

    problem_type = str(meta.get("problem_type", ""))
    if problem_type and problem_type not in BUG_TYPES and problem_type not in KNOWLEDGE_TYPES:
        doc.errors.append(f"`problem_type: {problem_type}` is not a schema value")
    if problem_type in BUG_TYPES:
        for key in BUG_REQUIRED:
            if not meta.get(key):
                doc.errors.append(f"bug-track `{problem_type}` requires `{key}`")

    severity = meta.get("severity")
    if severity and severity not in SEVERITIES:
        doc.errors.append(f"`severity: {severity}` must be one of {', '.join(sorted(SEVERITIES))}")
    resolution = meta.get("resolution_type")
    if resolution and resolution not in RESOLUTION_TYPES:
        doc.errors.append(f"`resolution_type: {resolution}` is not a schema value")
    for key in ("date", "last_updated"):
        value = meta.get(key)
        if value and not DATE.match(str(value)):
            doc.errors.append(f"`{key}` must be YYYY-MM-DD")

    module = meta.get("module")
    if module and module not in modules and module not in CROSS_CUTTING_MODULES:
        doc.errors.append(
            f"`module: {module}` must be a src project name (Headless.<X>) or one of {', '.join(sorted(CROSS_CUTTING_MODULES))}"
        )

    for key in ("tags", "symptoms", "applies_when", "related_components"):
        value = meta.get(key)
        if value is not None and not isinstance(value, list):
            doc.errors.append(f"`{key}` must be a list")
    for key, (_, maximum) in LIST_LIMITS.items():
        value = meta.get(key)
        if isinstance(value, list) and len(value) > maximum:
            doc.errors.append(f"`{key}` has {len(value)} items; the schema allows at most {maximum}")
    tags = meta.get("tags")
    if isinstance(tags, list):
        if tags and "tags" not in doc.inline_lists:
            doc.errors.append("`tags` must be one inline list (`tags: [a, b]`) so a single-line grep finds every tag")
        for tag in tags:
            if not KEBAB.match(tag):
                doc.errors.append(f"tag `{tag}` must be lowercase kebab-case")

    # The date lives in `date`/`last_updated`; a dated filename goes stale on the first in-place
    # update, and dropping it later renames the doc and breaks every link to it.
    if re.search(r"-\d{4}-\d{2}-\d{2}$", doc.path.stem):
        doc.errors.append("filename must not end in a date; the date belongs in `date` and `last_updated`")

    directory = doc.path.parent.name
    # `category`, when present, records the directory name; anything else means the doc moved or was mis-filed.
    category = meta.get("category")
    if category and category != directory:
        doc.errors.append(f"`category: {category}` must equal the directory name `{directory}`")
    expected = BUG_TYPES.get(problem_type) or KNOWLEDGE_TYPES.get(problem_type)
    if expected and directory != expected and directory not in AREA_DIRECTORIES:
        doc.errors.append(f"`problem_type: {problem_type}` belongs in {expected}/ (or an area directory: {', '.join(sorted(AREA_DIRECTORIES))})")


def check_references(doc: Doc) -> None:
    for target in LINK.findall(doc.body):
        if re.match(r"^[a-z]+:", target) or target.startswith("#"):
            continue
        path_part = target.split("#", 1)[0]
        if not path_part:
            continue
        resolved = (doc.path.parent / path_part).resolve()
        rel = resolved.relative_to(REPO_ROOT).as_posix() if resolved.is_relative_to(REPO_ROOT) else path_part
        if rel.startswith(DISPOSABLE_DOCS):
            doc.warnings.append(f"cites a planning or review artifact, which is deleted once its work lands: {target}")
        elif not resolved.exists():
            doc.warnings.append(f"broken link: {target}")
    for cited in set(CODE_PATH.findall(doc.body)):
        path_part = re.sub(r":\d[\d,\-]*$", "", cited).rstrip(".,;)")
        if any(char in path_part for char in "*<>{}"):
            continue
        if path_part.startswith(DISPOSABLE_DOCS):
            doc.warnings.append(f"cites a planning or review artifact: `{cited}`")
        elif not (REPO_ROOT / path_part).exists():
            doc.warnings.append(f"cited path does not exist: `{cited}`")


def render_index(docs: list[Doc]) -> str:
    lines = [
        "# Solutions index",
        "",
        "<!-- Generated by `make docs-index` from each doc's frontmatter. Do not edit by hand. -->",
        "",
        "One row per learning. Search this file for a module, tag, or problem type, then open the doc.",
    ]
    by_directory: dict[str, list[Doc]] = {}
    for doc in docs:
        by_directory.setdefault(doc.path.parent.name, []).append(doc)
    for directory in sorted(by_directory):
        lines += ["", f"## {directory}", "", "| Learning | Type | Module | Tags |", "| --- | --- | --- | --- |"]
        for doc in sorted(by_directory[directory], key=lambda d: d.path.name):
            tags = doc.meta.get("tags")
            tag_text = ", ".join(tags) if isinstance(tags, list) else ""
            # Titles name generic types such as `Headless{Feature}`; escape them so the table renders as text.
            title = doc.title.replace("|", "\\|").replace("<", "&lt;")
            # An empty cell renders as "|  |", which Markdown table linters reject; "-" reads as "none".
            cells = [f"[{title}]({doc.path.relative_to(SOLUTIONS).as_posix()})", doc.meta.get("problem_type", ""), doc.meta.get("module", ""), tag_text]
            lines.append("| " + " | ".join(str(cell) or "-" for cell in cells) + " |")
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("files", nargs="*", type=Path, help="check only these docs (default: all of docs/solutions)")
    parser.add_argument("--write-index", action="store_true", help="regenerate docs/solutions/INDEX.md")
    parser.add_argument("--no-index-check", action="store_true", help="skip the INDEX.md drift check")
    parser.add_argument("--quiet-warnings", action="store_true", help="print only the warning count")
    args = parser.parse_args()

    all_paths = sorted(path for path in SOLUTIONS.rglob("*.md") if path != INDEX)
    selected = [path.resolve() for path in args.files] if args.files else all_paths
    selected = [path for path in selected if path.is_relative_to(SOLUTIONS) and path != INDEX and path.exists()]

    modules = src_modules()
    docs = [parse(path) for path in all_paths]
    by_path = {doc.path: doc for doc in docs}
    for path in selected:
        doc = by_path[path]
        check_schema(doc, modules)
        check_references(doc)

    error_count = warning_count = 0
    for path in selected:
        doc = by_path[path]
        for error in doc.errors:
            print(f"error   {doc.rel}: {error}")
        error_count += len(doc.errors)
        if not args.quiet_warnings:
            for warning in doc.warnings:
                print(f"warning {doc.rel}: {warning}")
        warning_count += len(doc.warnings)

    index_text = render_index(docs)
    if args.write_index:
        # Writing the index is independent of schema errors; `make docs-check` reports those.
        INDEX.write_text(index_text, encoding="utf-8")
        print(f"[docs-check] wrote {INDEX.relative_to(REPO_ROOT)}")
        return 0
    elif not args.no_index_check and not args.files:
        current = INDEX.read_text(encoding="utf-8") if INDEX.exists() else ""
        if current != index_text:
            print(f"error   {INDEX.relative_to(REPO_ROOT)}: out of date; run `make docs-index`")
            error_count += 1

    print(f"[docs-check] {len(selected)} doc(s): {error_count} error(s), {warning_count} warning(s)", file=sys.stderr)
    return 3 if error_count else 0


if __name__ == "__main__":
    sys.exit(main())
