#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Project-reference graph for the solution: affected-project selection and layering checks.

The make targets that scope builds and tests to a change call this instead of mapping directory
names, because a name match misses every project that depends on the changed one. Standard
library only, so it runs on a bare CI runner and in git hooks without a virtual environment.

Commands:
  affected  Print the projects a change affects (changed projects plus their direct dependents, or
            every transitive dependent with --transitive) and the test projects that cover them.
  ci-scope  Decide how much of the .NET solution a CI run builds and tests: everything, the
            transitively affected set, or nothing. Prints key=value lines for $GITHUB_OUTPUT.
  layering  Check package dependency direction and public namespace names under src/, and exit 3
            on a violation.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

REPO_ROOT = Path(__file__).resolve().parents[1]
PROJECT_ROOTS = ("src", "tests", "demo", "sandboxes", "benchmarks", "test-assets")

# A change to one of these reaches every project, so the affected set becomes the whole graph.
GLOBAL_FILES = {"global.json", "dotnet-tools.json", "nuget.config"}
GLOBAL_PREFIXES = ("eng/DashboardSpa.targets",)
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
        return {"tests": "test-other", "demo": "demo", "sandboxes": "demo", "benchmarks": "benchmark"}.get(top, "other")


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


def _canonical_props(
    xml: str,
) -> tuple[list[tuple[str, tuple[tuple[str, str], ...], str]], dict[str, list[tuple[tuple[str, ...], str]]]] | None:
    """Split a central package file into its version map and everything else, or None when it does not parse.

    A <PackageVersion> that carries only Include and Version goes into the map; every other element, attribute and
    text stays in the returned sequence, so two files with the same sequence differ only in package versions. Each
    map entry keeps the Conditions of its enclosing elements: moving an entry into or out of a conditioned ItemGroup
    changes which projects can resolve the package, so it must read as a change to that package.
    """
    try:
        root = ET.fromstring(xml)
    except ET.ParseError:
        return None
    rest: list[tuple[str, tuple[tuple[str, str], ...], str]] = []
    versions: dict[str, list[tuple[tuple[str, ...], str]]] = {}

    def visit(element: ET.Element, conditions: tuple[str, ...]) -> None:
        attributes = dict(element.attrib)
        if element.tag == "PackageVersion" and set(attributes) == {"Include", "Version"} and not (element.text or "").strip():
            versions.setdefault(attributes["Include"].lower(), []).append((conditions, attributes["Version"]))
            return
        rest.append((element.tag, tuple(sorted(attributes.items())), (element.text or "").strip()))
        inner = (*conditions, attributes["Condition"]) if "Condition" in attributes else conditions
        for child in element:
            visit(child, inner)

    visit(root, ())
    return rest, {package: sorted(entries) for package, entries in versions.items()}


def package_version_changes(base: str, file: str) -> set[str] | None:
    """The package ids whose central version a change adds, removes or moves, or None when it changes anything else.

    A version bump in Directory.Packages.props reaches only the projects that resolve that package, but the file
    also holds properties, conditions and GlobalPackageReference items that reach every project under it. Anything
    besides plain <PackageVersion Include Version> entries keeps the whole-directory selection.
    """
    merge_base = git_lines("merge-base", base, "HEAD")[0]
    old = subprocess.run(["git", "show", f"{merge_base}:{file}"], cwd=REPO_ROOT, capture_output=True, text=True)
    current = REPO_ROOT / file
    if old.returncode != 0 or not current.is_file():
        return None
    before = _canonical_props(old.stdout)
    after = _canonical_props(current.read_text(encoding="utf-8"))
    if before is None or after is None or before[0] != after[0]:
        return None
    old_versions, new_versions = before[1], after[1]
    return {package for package in old_versions.keys() | new_versions.keys() if old_versions.get(package) != new_versions.get(package)}


def resolved_packages(project: Project) -> set[str] | None:
    """Every package id the project's lock file resolves, directly or transitively, or None without a lock file."""
    lock = REPO_ROOT / project.directory / "packages.lock.json"
    if not lock.is_file():
        return None
    data = json.loads(lock.read_text(encoding="utf-8"))
    return {package.lower() for frameworks in data.get("dependencies", {}).values() for package in frameworks}


def select_affected(
    base: str,
    projects: dict[str, Project],
    files: list[str] | None = None,
    *,
    transitive: bool = False,
    narrow_package_versions: bool = False,
) -> dict[str, object]:
    if files is None:
        files = changed_files(base)
    changed: set[str] = set()
    # Projects whose own files changed, as opposed to projects a build-wide file selected wholesale.
    edited: set[str] = set()
    unmapped: list[str] = []
    global_triggers: list[str] = []
    package_versions: dict[str, list[str]] = {}
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
            packages = package_version_changes(base, file) if narrow_package_versions and path.name == "Directory.Packages.props" else None
            if packages is not None:
                # A project without a lock file cannot say what it resolves, so it stays selected.
                hits = {key for key in hits if (resolved := resolved_packages(projects[key])) is None or resolved & packages}
                package_versions[file] = sorted(packages)
            else:
                global_triggers.append(file)
            changed.update(hits)
            continue
        owners = owning_projects(file, by_directory) | by_linked_input.get(file, set())
        if owners:
            changed.update(owners)
            edited.update(owners)
        else:
            unmapped.append(file)

    affected = set(changed)
    if transitive:
        # A gate must see every project that can stop compiling or behave differently, and that includes a
        # dependent's dependents: a changed signature in an Abstractions package breaks the providers' tests too.
        pending = list(changed)
        while pending:
            for dependent in projects[pending.pop()].dependents:
                if dependent not in affected:
                    affected.add(dependent)
                    pending.append(dependent)
    else:
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

    # An edited package that owns a <Package>.Tests.Integration project is one whose coverage only the integration
    # gate can judge, so that gate needs just these packages' own unit suites beside the integration run.
    by_name = {project.name: key for key, project in projects.items()}
    integration_packages = sorted(
        key for key in edited if projects[key].kind == "src" and f"{projects[key].name}.Tests.Integration" in by_name
    )
    integration_unit_tests = sorted(
        by_name[f"{projects[key].name}.Tests.Unit"] for key in integration_packages if f"{projects[key].name}.Tests.Unit" in by_name
    )

    return {
        "base": base,
        "transitive": transitive,
        "changed_files": len(files),
        "global_triggers": global_triggers,
        "package_version_changes": package_versions,
        "unmapped_files": unmapped,
        "changed_projects": sorted(changed),
        "affected_projects": sorted(affected),
        "build_projects": of_kind(affected | covering, "src", "unit", "integration", "harness", "test-other", "demo", "benchmark"),
        "unit_tests": of_kind(covering, "unit"),
        "integration_tests": of_kind(covering, "integration"),
        "integration_packages": integration_packages,
        "integration_unit_tests": integration_unit_tests,
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
        # A family's root package (Headless.X beside Headless.X.Abstractions) composes the family; its providers
        # build on it, so a reference the other way would make every consumer of the root pull a provider in.
        if project.name + ".Abstractions" in by_name:
            family = project.name
            for name in sorted(reference_names):
                if name.startswith(family + ".") and not name.endswith(".Abstractions") and name in by_name:
                    violations.append(f"{project.name} -> {name}: a family's root package must not reference its own providers")
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
# Bucket names say nothing about the scenario either: "Abstractions" or "Core" as a namespace or folder
# only means "the shared stuff", so a reader cannot tell what lives there.
BUCKET_SEGMENTS = {"Abstractions", "Common", "Contracts", "Core", "Misc"}
NAMESPACE_BASELINE = REPO_ROOT / "eng" / "namespace-baseline.txt"
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
        # Segment 0 is "Headless"; every later one, the family name included, must name a scenario.
        if namespace.startswith("Headless.") and (KIND_SEGMENTS | BUCKET_SEGMENTS) & set(namespace.split(".")[1:]):
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
        f"{namespace}: a public namespace must not be named after a kind of type or a bucket ({', '.join(sorted(KIND_SEGMENTS | BUCKET_SEGMENTS))})"
        for namespace in sorted(found - baseline)
    ]
    violations += [
        f"{namespace}: no longer exists; remove it from {NAMESPACE_BASELINE.relative_to(REPO_ROOT)}"
        for namespace in sorted(baseline - found)
    ]
    return violations


# Folders do not shape namespaces here, so they exist only to help a reader find code: a folder names the
# feature or area its files serve. A folder named after a kind of type scatters one feature across several
# folders, the same failure the kind-named namespace rule prevents.
KIND_FOLDERS = KIND_SEGMENTS | BUCKET_SEGMENTS
MAX_FOLDER_DEPTH = 2


def folder_violations() -> list[str]:
    """Source folders under src/<package>/ that are named after a kind of type or nest too deep."""
    kind: set[str] = set()
    deep: set[str] = set()
    for path in sorted((REPO_ROOT / "src").rglob("*.cs")):
        relative = path.relative_to(REPO_ROOT / "src")
        folders = relative.parts[1:-1]
        if {"obj", "bin"} & set(folders):
            continue
        for depth, folder in enumerate(folders, start=1):
            if folder in KIND_FOLDERS:
                kind.add("src/" + "/".join(relative.parts[: depth + 1]))
        if len(folders) > MAX_FOLDER_DEPTH:
            deep.add("src/" + "/".join(relative.parts[:-1]))
    violations = [
        f"{folder}: a source folder must name a feature or area, not a kind of type ({', '.join(sorted(KIND_FOLDERS))})"
        for folder in sorted(kind)
    ]
    violations += [
        f"{folder}: source folders may nest at most {MAX_FOLDER_DEPTH} levels below the package root"
        for folder in sorted(deep)
    ]
    return violations


# Paths that only document the project. CI already skips the .NET jobs for a change made only of these
# (scripts/ci-changes.sh), so they never widen the scope; any other path outside a project does.
def is_documentation(file: str) -> bool:
    path = PurePosixPath(file)
    if file.startswith(("docs/", ".github/ISSUE_TEMPLATE/")):
        return True
    return path.suffix == ".md" and path.parent.as_posix() in (".", ".github")


SPA_PROJECTS = (
    "src/Headless.Jobs.Dashboard/Headless.Jobs.Dashboard.csproj",
    "src/Headless.Messaging.Dashboard/Headless.Messaging.Dashboard.csproj",
)


def builds_spa(build: list[str], projects: dict[str, Project]) -> bool:
    """Whether building `build` compiles a dashboard, which needs its SPA output on disk.

    MSBuild builds every project a filtered project references, in the filter or not, so a dashboard that only a
    test project references (the docs-example tests reference both) is still compiled.
    """
    seen: set[str] = set()
    pending = list(build)
    while pending:
        key = pending.pop()
        if key in seen or key not in projects:
            continue
        seen.add(key)
        pending.extend(projects[key].references)
    return bool(seen & set(SPA_PROJECTS))


def diff_files(base: str, head: str) -> list[str] | None:
    """Paths changed between the merge base of BASE and HEAD, or None when the history cannot answer."""
    if not base or set(base) == {"0"}:
        return None
    try:
        merge_base = git_lines("merge-base", base, head)[0]
        result = subprocess.run(
            ["git", "-c", "core.quotePath=false", "diff", "--no-renames", "--name-only", "-z", merge_base, head, "--"],
            cwd=REPO_ROOT,
            check=True,
            capture_output=True,
        )
    except (subprocess.CalledProcessError, IndexError):
        return None
    return sorted({item for item in result.stdout.decode("utf-8").split("\0") if item})


def write_category_files(out_dir: Path, report: dict[str, object], solution: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "affected.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for category, key in CATEGORIES.items():
        if category == "affected":
            continue
        items: list[str] = report[key]  # type: ignore[assignment]
        missing = write_solution_filter(out_dir / f"{category}.slnf", solution, items)
        # The list matches its filter. A project outside the solution, such as a probe a test compiles on purpose
        # to watch it fail, is never built by the gate, so a stage that reads the list must not analyze it either.
        listed = [item for item in items if item not in missing]
        (out_dir / f"{category}.txt").write_text("".join(f"{item}\n" for item in listed), encoding="utf-8")
        for project in missing:
            print(f"[affected] not in {solution.name}, left out of {category}.slnf and {category}.txt: {project}", file=sys.stderr)


def ci_scope(event: str, base: str, head: str, out_dir: Path, solution: str, projects: dict[str, Project]) -> dict[str, str]:
    """Choose the CI scope. Only a pull request narrows it; pushes, releases and dispatches build everything.

    The narrowed scope is the transitive affected set, so a pull request gate never misses a project that the
    change breaks through a chain of references. Anything the project graph cannot attribute to a project, such
    as a workflow, a script, the Makefile, a build-wide props file or package versions, runs everything.
    """
    full = {"scope": "full", "solution": solution, "unit_solution": "", "build_projects": "all", "unit_tests": "all", "spa": "true"}

    def fall_back(reason: str) -> dict[str, str]:
        print(f"[ci-scope] full run: {reason}", file=sys.stderr)
        return full

    if event != "pull_request":
        return fall_back(f"event {event or '(none)'} always builds the whole solution")
    files = diff_files(base, head)
    if files is None:
        return fall_back(f"cannot diff {base or '(no base)'}..{head}")
    report = select_affected(base, projects, files, transitive=True)
    triggers: list[str] = report["global_triggers"]  # type: ignore[assignment]
    if triggers:
        return fall_back("build-wide file changed: " + ", ".join(triggers))
    outside = [file for file in report["unmapped_files"] if not is_documentation(file)]  # type: ignore[union-attr]
    if outside:
        return fall_back("path outside every project: " + ", ".join(outside[:10]) + (" ..." if len(outside) > 10 else ""))

    build: list[str] = report["build_projects"]  # type: ignore[assignment]
    unit: list[str] = report["unit_tests"]  # type: ignore[assignment]
    write_category_files(out_dir, report, REPO_ROOT / solution)
    print(
        f"[ci-scope] affected run: {len(report['changed_projects'])} changed, {len(build)} to build, "  # type: ignore[arg-type]
        f"{len(unit)} unit-test project(s)",
        file=sys.stderr,
    )
    return {
        "scope": "affected" if build else "empty",
        "solution": (out_dir / "build.slnf").as_posix() if build else "",
        "unit_solution": (out_dir / "unit.slnf").as_posix() if unit else "",
        "build_projects": str(len(build)),
        "unit_tests": str(len(unit)),
        "spa": "true" if builds_spa(build, projects) else "false",
    }


CATEGORIES = {
    "changed": "changed_projects",
    "affected": "affected_projects",
    "build": "build_projects",
    "unit": "unit_tests",
    "integration": "integration_tests",
    "integration-packages": "integration_packages",
    "integration-unit": "integration_unit_tests",
}


# Inputs every proof stage reads whatever the selection: the gate recipes, the proof tooling, and the build-wide files.
INPUT_KEY_REPO_PATHS = ("Makefile", "scripts", "eng", "headless-framework.slnx", *sorted(GLOBAL_FILES))


def input_key(out_dir: Path, projects: dict[str, Project], salt: str) -> str:
    """Hash what a proof of the selection in OUT_DIR depends on, so an equal key proves the same thing.

    The key covers the content of every file in the build closure (each selected project, its ProjectReference
    closure, and the files they link), the scoped build files above them, the repository-wide inputs, the branch's
    patch against the merge base (the changed-line coverage gate reads it), and the selection itself. A rebase onto a
    main that touched none of those, or an amend that changed only the message, yields the same key.
    """
    affected = json.loads((out_dir / "affected.json").read_text(encoding="utf-8"))
    pending = [
        item
        for category in ("build_projects", "unit_tests", "integration_tests", "integration_unit_tests")
        for item in affected.get(category, [])
        if item in projects
    ]
    closure: set[str] = set()
    while pending:
        key = pending.pop()
        if key not in closure:
            closure.add(key)
            pending.extend(projects[key].references)
    paths = set(INPUT_KEY_REPO_PATHS)
    for key in closure:
        project = projects[key]
        paths.add(project.directory.as_posix())
        paths.update(project.linked_inputs)
        for parent in project.directory.parents:
            paths.update((parent / name).as_posix() for name in SCOPED_BUILD_FILES)
    files = sorted(
        set(git_lines("ls-files", "--cached", "--others", "--exclude-standard", "--", *(f":(literal){p}" for p in paths)))
    )
    present = [file for file in files if (REPO_ROOT / file).is_file()]
    blobs = (
        subprocess.run(
            ["git", "hash-object", "--stdin-paths"],
            cwd=REPO_ROOT,
            input="".join(f"{file}\n" for file in present),
            check=True,
            capture_output=True,
            text=True,
        ).stdout.split()
        if present
        else []
    )
    digest = hashlib.sha256()
    digest.update(salt.encode())
    selection = {key: value for key, value in affected.items() if key != "base"}
    digest.update(json.dumps(selection, sort_keys=True).encode())
    for file, blob in zip(present, blobs, strict=True):
        digest.update(f"{file}\0{blob}\n".encode())
    # A tracked file deleted in the worktree has no blob, and its absence is part of what was proved.
    for file in sorted(set(files) - set(present)):
        digest.update(f"{file}\0deleted\n".encode())
    merge_base = git_lines("merge-base", affected["base"], "HEAD")[0]
    patch = subprocess.run(
        ["git", "diff", "--no-color", "--no-ext-diff", "-U0", merge_base],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
    ).stdout
    digest.update(patch)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    key_parser = commands.add_parser(
        "input-key", help="print a hash of every input a proof of the selection in DIR depends on"
    )
    key_parser.add_argument("--dir", required=True, type=Path, help="a directory `affected --out-dir` wrote")
    key_parser.add_argument("--salt", default="", help="settings that change what the proof checks, such as the gate and its options")

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
    affected.add_argument(
        "--transitive",
        action="store_true",
        help="select every transitive dependent of a changed project, not only its direct dependents",
    )

    scope = commands.add_parser("ci-scope", help="choose the CI build and test scope; prints key=value lines")
    scope.add_argument("--event", default="", help="GitHub event name; only pull_request narrows the scope")
    scope.add_argument("--base", default="", help="base commit of the change; empty or all zeros runs everything")
    scope.add_argument("--head", default="HEAD", help="head commit of the change")
    scope.add_argument("--out-dir", required=True, metavar="DIR", help="where the solution filters and project lists go")
    scope.add_argument("--solution", default="headless-framework.slnx", help="solution the filters point at")

    commands.add_parser("layering", help="check package dependency direction and public namespace names under src/")

    solution_filter = commands.add_parser(
        "solution-filter",
        help="write a solution filter of every solution project except those whose name ends with an excluded suffix",
    )
    solution_filter.add_argument("--out", required=True, metavar="PATH", help="where to write the .slnf")
    solution_filter.add_argument(
        "--exclude-suffix", action="append", default=[], metavar="SUFFIX", help="leave out projects named *SUFFIX; repeatable"
    )
    solution_filter.add_argument("--solution", default="headless-framework.slnx", help="solution the filter points at")

    args = parser.parse_args()

    if args.command == "solution-filter":
        solution = REPO_ROOT / args.solution
        kept = sorted(
            project
            for project in solution_projects(solution)
            if not PurePosixPath(project).stem.endswith(tuple(args.exclude_suffix))
        )
        write_solution_filter(Path(args.out), solution, kept)
        return 0

    projects = load_graph()

    if args.command == "input-key":
        print(input_key(args.dir, projects, args.salt))
        return 0

    if args.command == "layering":
        violations = layering_violations(projects) + namespace_violations() + folder_violations()
        for violation in violations:
            print(violation)
        print(f"[layering] {len(violations)} violation(s) across {sum(p.kind == 'src' for p in projects.values())} src projects", file=sys.stderr)
        return 3 if violations else 0

    if args.command == "ci-scope":
        out_dir = Path(args.out_dir)
        if out_dir.exists():
            shutil.rmtree(out_dir)
        for key, value in ci_scope(args.event, args.base, args.head, out_dir, args.solution, projects).items():
            print(f"{key}={value}")
        return 0

    files = None
    if args.files_from:
        stream = sys.stdin if args.files_from == "-" else open(args.files_from, encoding="utf-8")
        with stream:
            files = sorted({line.strip() for line in stream if line.strip()})
    # Local gates narrow a version-only central package change to the projects that resolve those packages; CI's
    # ci-scope keeps the full run for any build-wide file.
    report = select_affected(args.base, projects, files, transitive=args.transitive, narrow_package_versions=True)
    if args.out_dir:
        write_category_files(Path(args.out_dir), report, REPO_ROOT / args.solution)
        print(
            f"[affected] vs {args.base}: {len(report['changed_projects'])} changed, "  # type: ignore[arg-type]
            f"{len(report['build_projects'])} to build, {len(report['unit_tests'])} unit-test and "  # type: ignore[arg-type]
            f"{len(report['integration_tests'])} integration-test project(s)",  # type: ignore[arg-type]
            file=sys.stderr,
        )
        for label, key in (("build-wide file changed", "global_triggers"), ("not in any project", "unmapped_files")):
            for item in report[key]:  # type: ignore[union-attr]
                print(f"[affected]   {label}: {item}", file=sys.stderr)
        for file, packages in report["package_version_changes"].items():  # type: ignore[union-attr]
            print(f"[affected]   package versions changed in {file}, selected by lock file: {', '.join(packages)}", file=sys.stderr)
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
