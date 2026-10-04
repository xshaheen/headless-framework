#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Project-reference graph for the solution: affected-project selection and layering checks.

The make targets that scope builds and tests to a change call this instead of mapping directory
names, because a name match misses every project that depends on the changed one. Standard
library only, so it runs on a bare CI runner and in git hooks without a virtual environment.

Commands:
  affected  Print the projects a change affects (changed projects plus their direct dependents)
            and the test projects that cover them.
  layering  Check package dependency direction and public namespace names under src/, and exit 3
            on a violation.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

REPO_ROOT = Path(__file__).resolve().parents[1]
PROJECT_ROOTS = ("src", "tests", "demo", "benchmarks", "test-assets")

# A change to one of these reaches every project, so the affected set becomes the whole graph.
GLOBAL_FILES = {"global.json", "dotnet-tools.json", "nuget.config"}
GLOBAL_PREFIXES = ("eng/analyzers/", "eng/DashboardSpa.targets")
# A change to one of these reaches every project in and under its directory.
SCOPED_BUILD_FILES = {"Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", ".editorconfig"}

# Packages every Abstractions package may reference. Headless.Serializer.Json is foundation by
# decision: Blobs, Features and Settings abstractions expose its JSON options in their contracts.
FOUNDATION_PACKAGES = {
    "Headless.Checks",
    "Headless.Extensions",
    "Headless.Primitives",
    "Headless.Serializer.Json",
}


@dataclass
class Project:
    name: str
    path: PurePosixPath  # repo-relative .csproj path
    references: set[str] = field(default_factory=set)
    dependents: set[str] = field(default_factory=set)
    # Files outside the project directory that the project links as items, such as the guides a docs-example
    # test compiles. A change to one changes what the project builds or tests, but no directory match finds it.
    linked_inputs: set[str] = field(default_factory=set)

    @property
    def directory(self) -> PurePosixPath:
        return self.path.parent

    @property
    def kind(self) -> str:
        top = self.path.parts[0]
        if top == "src":
            return "src"
        if self.name.endswith(".Tests.Unit"):
            return "unit"
        if self.name.endswith(".Tests.Integration"):
            return "integration"
        if self.name.endswith(".Tests.Harness"):
            return "harness"
        return {"tests": "test-other", "demo": "demo", "benchmarks": "benchmark"}.get(top, "other")


def git_lines(*args: str) -> list[str]:
    result = subprocess.run(
        ["git", "-c", "core.quotePath=false", *args],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    )
    return [line for line in result.stdout.splitlines() if line]


def read_project_xml(path: Path) -> ET.Element:
    raw = path.read_bytes()
    # One project file is saved as UTF-16; ElementTree honours the BOM when given bytes.
    return ET.fromstring(raw)


def load_graph() -> dict[str, Project]:
    tracked = git_lines("ls-files", "--", *(f"{root}/*.csproj" for root in PROJECT_ROOTS))
    untracked = git_lines("ls-files", "--others", "--exclude-standard", "--", *(f"{root}/*.csproj" for root in PROJECT_ROOTS))
    projects: dict[str, Project] = {}
    for relative in sorted(set(tracked) | set(untracked)):
        path = PurePosixPath(relative)
        projects[relative] = Project(name=path.stem, path=path)

    for key, project in projects.items():
        root = read_project_xml(REPO_ROOT / key)
        for element in root.iter():
            tag = element.tag.rsplit("}", 1)[-1]
            if tag in LINKED_ITEM_TAGS:
                _add_linked_input(project, element.get("Include"))
                continue
            if tag != "ProjectReference":
                continue
            include = element.get("Include")
            if not include or "$(" in include:
                continue
            target = (REPO_ROOT / project.directory / include.replace("\\", "/")).resolve()
            try:
                target_key = target.relative_to(REPO_ROOT).as_posix()
            except ValueError:
                continue
            if target_key in projects:
                project.references.add(target_key)
                projects[target_key].dependents.add(key)
    return projects


LINKED_ITEM_TAGS = {"None", "Content", "Compile", "EmbeddedResource", "AdditionalFiles"}


def _add_linked_input(project: Project, include: str | None) -> None:
    if not include or "$(" in include or "*" in include:
        return
    target = (REPO_ROOT / project.directory / include.replace("\\", "/")).resolve()
    try:
        target_key = PurePosixPath(target.relative_to(REPO_ROOT).as_posix())
    except ValueError:
        return
    if project.directory not in target_key.parents:
        project.linked_inputs.add(target_key.as_posix())


def changed_files(base: str) -> list[str]:
    merge_base = git_lines("merge-base", base, "HEAD")[0]
    diff = git_lines("diff", "--name-only", merge_base)
    untracked = git_lines("ls-files", "--others", "--exclude-standard")
    return sorted(set(diff) | set(untracked))


def projects_by_directory(projects: dict[str, Project]) -> dict[str, list[str]]:
    index: dict[str, list[str]] = {}
    for key, project in projects.items():
        index.setdefault(project.directory.as_posix(), []).append(key)
    return index


def owning_projects(file: str, by_directory: dict[str, list[str]]) -> set[str]:
    """Every project whose directory contains the file; nested probe projects also hit their host."""
    owners: set[str] = set()
    for parent in PurePosixPath(file).parents:
        owners.update(by_directory.get(parent.as_posix(), ()))
    return owners


def select_affected(base: str, projects: dict[str, Project], files: list[str] | None = None) -> dict[str, object]:
    if files is None:
        files = changed_files(base)
    changed: set[str] = set()
    unmapped: list[str] = []
    global_triggers: list[str] = []
    by_directory = projects_by_directory(projects)
    by_linked_input: dict[str, set[str]] = {}
    for key, project in projects.items():
        for linked in project.linked_inputs:
            by_linked_input.setdefault(linked, set()).add(key)

    for file in files:
        path = PurePosixPath(file)
        if file in GLOBAL_FILES or file.startswith(GLOBAL_PREFIXES):
            global_triggers.append(file)
            changed.update(projects)
            continue
        if path.name in SCOPED_BUILD_FILES:
            scope = path.parent
            hits = {key for key, project in projects.items() if scope == PurePosixPath(".") or scope in project.path.parents}
            global_triggers.append(file)
            changed.update(hits)
            continue
        owners = owning_projects(file, by_directory) | by_linked_input.get(file, set())
        if owners:
            changed.update(owners)
        else:
            unmapped.append(file)

    affected = set(changed)
    for key in changed:
        affected.update(projects[key].dependents)

    # A test project covers the affected set when it is in it or references a member directly.
    covering = {
        key
        for key, project in projects.items()
        if project.kind in ("unit", "integration") and (key in affected or project.references & affected)
    }

    def of_kind(keys: set[str], *kinds: str) -> list[str]:
        return sorted(key for key in keys if projects[key].kind in kinds)

    return {
        "base": base,
        "changed_files": len(files),
        "global_triggers": global_triggers,
        "unmapped_files": unmapped,
        "changed_projects": sorted(changed),
        "affected_projects": sorted(affected),
        "build_projects": of_kind(affected | covering, "src", "unit", "integration", "harness", "test-other", "demo", "benchmark"),
        "unit_tests": of_kind(covering, "unit"),
        "integration_tests": of_kind(covering, "integration"),
    }


def solution_projects(solution: Path) -> set[str]:
    root = ET.fromstring(solution.read_bytes())
    return {
        element.get("Path", "").replace("\\", "/")
        for element in root.iter()
        if element.tag.rsplit("}", 1)[-1] == "Project" and element.get("Path")
    }


def write_solution_filter(target: Path, solution: Path, projects: list[str]) -> list[str]:
    """Write a .slnf limited to PROJECTS; returns the projects the solution does not list."""
    listed = solution_projects(solution)
    included = [project for project in projects if project in listed]
    target.parent.mkdir(parents=True, exist_ok=True)
    relative_solution = Path(os.path.relpath(solution, target.parent)).as_posix()
    document = {"solution": {"path": relative_solution, "projects": included}}
    target.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return [project for project in projects if project not in listed]


def layering_violations(projects: dict[str, Project]) -> list[str]:
    violations: list[str] = []
    by_name = {project.name: project for project in projects.values()}
    for project in projects.values():
        if project.kind != "src":
            continue
        reference_names = {projects[ref].name for ref in project.references}
        for ref_key in sorted(project.references):
            ref = projects[ref_key]
            if ref.kind != "src":
                violations.append(f"{project.name} -> {ref.name}: a package must not reference a {ref.kind} project")
        if project.name.endswith(".Abstractions"):
            for name in sorted(reference_names):
                if not name.endswith(".Abstractions") and name not in FOUNDATION_PACKAGES:
                    violations.append(
                        f"{project.name} -> {name}: an Abstractions package may reference only Abstractions or foundation packages"
                    )
        if project.name.endswith(".Core"):
            family = project.name.removesuffix(".Core")
            for name in sorted(reference_names):
                if name.startswith(family + ".") and not name.endswith(".Abstractions") and name in by_name:
                    violations.append(f"{project.name} -> {name}: a Core package must not reference its own family's providers")
    return violations


# A public namespace groups types by the scenario that uses them, so a segment that names a kind of
# type makes one scenario need several imports. Namespaces that hold only non-public types are
# implementation detail and exempt.
KIND_SEGMENTS = {
    "Base",
    "Constants",
    "Dtos",
    "Entities",
    "Enums",
    "Exceptions",
    "Extensions",
    "Helpers",
    "Interfaces",
    "Models",
    "Utilities",
    "Utils",
}
NAMESPACE_BASELINE = REPO_ROOT / "scripts" / "namespace-baseline.txt"
_NAMESPACE = re.compile(r"^namespace\s+([\w.]+)\s*[;{]", re.MULTILINE)
_PUBLIC_TYPE = re.compile(
    r"^\s*public\s+(?:(?:static|sealed|abstract|partial|readonly|ref|unsafe|record)\s+)*"
    r"(?:class|interface|struct|enum|delegate|record)\b",
    re.MULTILINE,
)


def public_kind_namespaces() -> set[str]:
    """Namespaces under src/ that declare a public type and name a kind of type after the family root."""
    found: set[str] = set()
    for path in sorted((REPO_ROOT / "src").rglob("*.cs")):
        if {"obj", "bin"} & set(path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        match = _NAMESPACE.search(text)
        if not match or not _PUBLIC_TYPE.search(text):
            continue
        namespace = match.group(1)
        # Segments 0 and 1 are "Headless" and the family name, which may legitimately be "Extensions".
        if namespace.startswith("Headless.") and KIND_SEGMENTS & set(namespace.split(".")[2:]):
            found.add(namespace)
    return found


def namespace_violations() -> list[str]:
    """Kind-named public namespaces, ratcheted against a baseline that may only shrink."""
    baseline = set()
    if NAMESPACE_BASELINE.exists():
        for line in NAMESPACE_BASELINE.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line and not line.startswith("#"):
                baseline.add(line)
    found = public_kind_namespaces()
    violations = [
        f"{namespace}: a public namespace must not be named after a kind of type ({', '.join(sorted(KIND_SEGMENTS))})"
        for namespace in sorted(found - baseline)
    ]
    violations += [
        f"{namespace}: no longer exists; remove it from {NAMESPACE_BASELINE.relative_to(REPO_ROOT)}"
        for namespace in sorted(baseline - found)
    ]
    return violations


CATEGORIES = {
    "changed": "changed_projects",
    "affected": "affected_projects",
    "build": "build_projects",
    "unit": "unit_tests",
    "integration": "integration_tests",
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    affected = commands.add_parser("affected", help="list projects affected by changes since BASE")
    affected.add_argument("--base", required=True, help="git ref to diff against (merge-base with HEAD)")
    affected.add_argument(
        "--files-from",
        metavar="FILE",
        help="read changed paths from FILE ('-' for stdin) instead of diffing against BASE",
    )
    affected.add_argument(
        "--slnf",
        metavar="PATH",
        help="also write a solution filter for the --list category (not json) to PATH",
    )
    affected.add_argument("--solution", default="headless-framework.slnx", help="solution the filter points at")
    affected.add_argument(
        "--out-dir",
        metavar="DIR",
        help="write affected.json plus <category>.txt and <category>.slnf for build, changed, unit and integration to DIR",
    )
    affected.add_argument(
        "--list",
        choices=("json", "changed", "affected", "build", "unit", "integration"),
        default="json",
        help="print one category as newline-separated paths instead of the JSON report",
    )

    commands.add_parser("layering", help="check package dependency direction and public namespace names under src/")

    args = parser.parse_args()
    projects = load_graph()

    if args.command == "layering":
        violations = layering_violations(projects) + namespace_violations()
        for violation in violations:
            print(violation)
        print(f"[layering] {len(violations)} violation(s) across {sum(p.kind == 'src' for p in projects.values())} src projects", file=sys.stderr)
        return 3 if violations else 0

    files = None
    if args.files_from:
        stream = sys.stdin if args.files_from == "-" else open(args.files_from, encoding="utf-8")
        with stream:
            files = sorted({line.strip() for line in stream if line.strip()})
    report = select_affected(args.base, projects, files)
    if args.out_dir:
        out_dir = Path(args.out_dir)
        out_dir.mkdir(parents=True, exist_ok=True)
        (out_dir / "affected.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        for category, key in CATEGORIES.items():
            if category == "affected":
                continue
            items: list[str] = report[key]  # type: ignore[assignment]
            (out_dir / f"{category}.txt").write_text("".join(f"{item}\n" for item in items), encoding="utf-8")
            missing = write_solution_filter(out_dir / f"{category}.slnf", REPO_ROOT / args.solution, items)
            for project in missing:
                print(f"[affected] not in {args.solution}, left out of {category}.slnf: {project}", file=sys.stderr)
        print(
            f"[affected] vs {args.base}: {len(report['changed_projects'])} changed, "  # type: ignore[arg-type]
            f"{len(report['build_projects'])} to build, {len(report['unit_tests'])} unit-test and "  # type: ignore[arg-type]
            f"{len(report['integration_tests'])} integration-test project(s)",  # type: ignore[arg-type]
            file=sys.stderr,
        )
        for label, key in (("build-wide file changed", "global_triggers"), ("not in any project", "unmapped_files")):
            for item in report[key]:  # type: ignore[union-attr]
                print(f"[affected]   {label}: {item}", file=sys.stderr)
        return 0
    if args.list == "json":
        json.dump(report, sys.stdout, indent=2)
        print()
    else:
        items = report[CATEGORIES[args.list]]  # type: ignore[assignment]
        for item in items:
            print(item)
        if args.slnf:
            missing = write_solution_filter(Path(args.slnf), REPO_ROOT / args.solution, items)
            for project in missing:
                print(f"[affected] not in {args.solution}, skipped from the filter: {project}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
