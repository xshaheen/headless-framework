# Strict mode lives in SHELL itself: macOS ships GNU Make 3.81, which ignores .SHELLFLAGS, so a
# .SHELLFLAGS line left local recipes without -e, -u, and pipefail while CI's make 4 applied them.
SHELL := /bin/bash -eu -o pipefail

.DEFAULT_GOAL := help
# Every target is phony and none is built from a pattern, so make's built-in implicit-rule search is pure cost.
MAKEFLAGS += -r
.SUFFIXES:

# Where the CLI writes its runtime files (the sandbox's pid, log, state, password, and up.rev): CLI_DIR, untracked and
# per checkout under <repo-root>/.local/cli/<checkout>/ when a .wiki/ sits at or above the repo, else a stable
# per-checkout folder under $TMPDIR (CI, a fresh clone). Build outputs that ship or that the docs name (packages,
# test results, coverage, proof bundles) stay under ARTIFACTS_DIR. The helpers below are the project CLI's
# shared paths block; each value is memoized on first use, so help and doctor never fork for them.
ITEM ?=
REPO_ROOT = $(eval REPO_ROOT := $(or $(shell git rev-parse --show-toplevel 2>/dev/null),$(CURDIR)))$(REPO_ROOT)
CLI_KEY = $(eval CLI_KEY := $(shell $(cli_key)))$(CLI_KEY)
WIKI_ROOT = $(eval WIKI_ROOT := $(shell $(find_wiki)))$(WIKI_ROOT)
WORK_ITEM = $(eval WORK_ITEM := $(shell $(find_item)))$(WORK_ITEM)
CLI_TMP_DIR = $(eval CLI_TMP_DIR := $(shell $(cli_tmp_dir)))$(CLI_TMP_DIR)
CLI_DIR ?= $(if $(WIKI_ROOT),$(REPO_ROOT)/.local/cli/$(CLI_KEY),$(CLI_TMP_DIR))
EVIDENCE_DIR ?= $(if $(WIKI_ROOT),$(REPO_ROOT)/.local/work/$(WORK_ITEM)/verifications)

# The nearest .wiki/ at or above REPO_ROOT, checking $HOME itself and nothing above it; empty when none.
define find_wiki
d='$(REPO_ROOT)'; h=$$(cd "$$HOME" 2>/dev/null && pwd -P || true); while :; do if [ -d "$$d/.wiki" ]; then printf '%s' "$$d/.wiki"; break; fi; if [ "$$d" = "$$h" ] || [ "$$d" = / ]; then break; fi; d=$$(dirname "$$d"); done
endef

# The work item: ITEM=, else the branch after its last /, else adhoc on a detached HEAD.
define find_item
i='$(ITEM)'; if [ -z "$$i" ]; then b=$$(git symbolic-ref -q --short HEAD 2>/dev/null || true); i=$${b##*/}; fi; printf '%s' "$${i:-adhoc}"
endef

# The checkout's folder name and a checksum of its full path.
define cli_key
r='$(REPO_ROOT)'; printf '%s-%s' "$${r##*/}" "$$(printf '%s' "$$r" | cksum | cut -d ' ' -f 1)"
endef

# Stable per checkout so up, status, and down in separate makes agree; said on stderr so a JSON=1
# report stays clean.
define cli_tmp_dir
t="$${TMPDIR:-/tmp}"; d="$${t%/}/cli-$(CLI_KEY)"; echo "make: no .wiki/ at or above $(REPO_ROOT); runtime files go to $$d. Run x-project-init to keep them in the repo's .local/." >&2; printf '%s' "$$d"
endef

# The first recipe line of a capture target: exit 1 naming x-project-init when the variable is empty.
need_wiki = [ -n "$($(1))" ] || { echo "$@: no .wiki/ at or above $(REPO_ROOT), and captures are kept only in a project with a wiki. Run x-project-init to create it." >&2; exit 1; }

DOTNET ?= dotnet
NPM ?= npm
SOLUTION ?= headless-framework.slnx
JOBS_DASHBOARD_DIR ?= src/Headless.Jobs.Dashboard/wwwroot
MESSAGING_DASHBOARD_DIR ?= src/Headless.Messaging.Dashboard/wwwroot
MESSAGING_COMPATIBILITY_DIR ?= tests/Headless.Messaging.PackageReference.Tests.Unit/Probes/Compatibility
CONFIGURATION ?= Release
ARTIFACTS_DIR ?= artifacts
PACKAGES_DIR ?= $(ARTIFACTS_DIR)/packages-results
PACK_LOG_DIR ?= $(ARTIFACTS_DIR)/pack-logs
PACKAGE_MANIFEST ?= eng/expected-packages.txt
PACKAGE_VERSION ?= $(shell $(DOTNET) msbuild src/Headless.Checks/Headless.Checks.csproj -nologo -target:MinVer -getProperty:PackageVersion)
EXPECTED_PACKAGE_VERSION ?= $(shell if [ -f "$(PACKAGES_DIR)/package-version.txt" ]; then sed -n '1p' "$(PACKAGES_DIR)/package-version.txt"; else printf '%s\n' "$(PACKAGE_VERSION)"; fi)
EXPECTED_REPOSITORY_URL ?= https://github.com/xshaheen/headless-framework.git
EXPECTED_REPOSITORY_COMMIT ?= $(shell git rev-parse HEAD)
TEST_RESULTS_DIR ?= $(ARTIFACTS_DIR)/test-results
COVERAGE_DIR ?= $(ARTIFACTS_DIR)/coverage
COVERAGE_REPORT_DIR ?= $(COVERAGE_DIR)/report
COVERAGE_REPORT_TYPES ?= Html;JsonSummary
DEPENDENCY_AUDIT_DIR ?= $(ARTIFACTS_DIR)/dependency-audit
PROJECT ?=
TEST_PROJECT ?=
TEST_FILTER ?=
TEST_ARGS ?= --no-progress
COVERAGE_SETTINGS_PROJECT ?= tests/Headless.Domain.Tests.Unit/Headless.Domain.Tests.Unit.csproj
TEST_MODULES ?= $(UNIT_TEST_MODULES) $(INTEGRATION_TEST_MODULES)
UNIT_TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.Unit.dll
INTEGRATION_TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.Integration.dll
# The chaos and soak tier (*.Tests.Chaos) runs for minutes against real containers, so it runs only on purpose:
# through test-messaging-chaos or the scheduled run. The whole-solution test targets run this solution filter
# instead of the bare solution, so they build that tier with everything else but never execute it.
CHAOS_TEST_SUFFIX ?= .Tests.Chaos
NO_CHAOS_SOLUTION ?= $(ARTIFACTS_DIR)/solution-filters/headless-framework.no-chaos.slnf
WRITE_NO_CHAOS_SOLUTION = $(GRAPH) solution-filter --exclude-suffix "$(CHAOS_TEST_SUFFIX)" --out "$(NO_CHAOS_SOLUTION)" --solution "$(SOLUTION)"
MESSAGING_CHAOS_PROJECT ?= tests/Headless.Messaging.Tests.Chaos/Headless.Messaging.Tests.Chaos.csproj
MESSAGING_CHAOS_RESULTS_DIR ?= $(TEST_RESULTS_DIR)/messaging-chaos
# The messaging integration tier (messaging-integration.yml): every broker, broker-plus-database, and relational
# storage integration suite, listed once in scripts/messaging-integration-scope.py. The Azure Service Bus project runs
# for its emulator profile, which skips unless HEADLESS_ACCEPT_SERVICEBUS_EMULATOR_EULA=Y accepts the emulator's
# license; its real-namespace tests need a credential and skip here, running in their own dispatch-only workflow.
MESSAGING_INTEGRATION_PROJECTS ?= $(shell $(PYTHON) -I scripts/messaging-integration-scope.py projects)
MESSAGING_INTEGRATION_RESULTS_DIR ?= $(TEST_RESULTS_DIR)/messaging-integration
MESSAGING_INTEGRATION_REPORT ?= true
MESSAGING_DRIFT_REPORT_ROOT ?= $(MESSAGING_INTEGRATION_RESULTS_DIR)
# Providers whose real-broker leaves the integration tier does not run; the drift report does not wait for their cells.
MESSAGING_DRIFT_EXCLUDED_PROVIDERS ?= Azure Service Bus
MESSAGING_QUARANTINE_DIR ?= $(TEST_RESULTS_DIR)/messaging-quarantine
MESSAGING_UNIT_PROJECT ?= tests/Headless.Messaging.Tests.Unit/Headless.Messaging.Tests.Unit.csproj
MESSAGING_CONFORMANCE_PROVIDERS ?= Aws Kafka Nats Pulsar RabbitMq Redis
MESSAGING_CONFORMANCE_EVIDENCE_PROJECTS ?= $(foreach provider,$(MESSAGING_CONFORMANCE_PROVIDERS),tests/Headless.Messaging.$(provider).Tests.Integration/Headless.Messaging.$(provider).Tests.Integration.csproj)
MESSAGING_CONFORMANCE_EVIDENCE_MODULES ?= $(foreach provider,$(MESSAGING_CONFORMANCE_PROVIDERS),tests/Headless.Messaging.$(provider).Tests.Integration/bin/$(CONFIGURATION)/net10.0/Headless.Messaging.$(provider).Tests.Integration.dll)
# `build` type-checks (vue-tsc) and bundles; CI's solution build passes `build-only` because the separate
# Dashboard jobs already type-check, lint, and unit-test the SPAs.
DASHBOARD_BUILD_SCRIPT ?= build
MSBUILD_ARGS ?=
# Static-graph restore evaluates each project once through MSBuild's project graph instead of recursively walking
# references per project; a forced solution restore measured ~5s versus ~8s locally.
RESTORE_ARGS ?= -p:RestoreUseStaticGraphEvaluation=true
DEPENDENCY_AUDIT_IDLE_TIMEOUT ?= 120
DEPENDENCY_SECURITY_AUDIT_TIMEOUT ?= 120
NUGET_ADVISORY_AUDIT_TIMEOUT ?= 90
PACK_PARALLELISM ?= 4
QUALITY_SEVERITY ?= hidden
QUALITY_DIAGNOSTICS ?=
QUALITY_REPORT_DIR ?= $(ARTIFACTS_DIR)/quality-analyzers-report
QUALITY_FORMAT_LOG ?= $(ARTIFACTS_DIR)/quality-analyzers-format.log
QUALITY_FORMAT_ARGS = --no-restore --verify-no-changes --severity "$(QUALITY_SEVERITY)" -v minimal --report "$(QUALITY_REPORT_DIR)" $(if $(QUALITY_DIAGNOSTICS),--diagnostics $(QUALITY_DIAGNOSTICS),)
# The fix path always runs at `hidden`: `dotnet format` matches IDE rules only at that severity, even
# for sites the report lists as info. QUALITY_DIAGNOSTICS is what bounds the blast radius, not severity.
QUALITY_FIX_SEVERITY ?= hidden
QUALITY_FIX_ARGS = --no-restore --severity "$(QUALITY_FIX_SEVERITY)" -v minimal --diagnostics $(QUALITY_DIAGNOSTICS)
# HeadlessReferenceTrimmer turns on ReferenceTrimmer and, through Directory.Build.props, builds under artifacts/reftrim
# with its own lock file, so the run leaves the regular outputs and the committed packages.lock.json files untouched.
REFERENCES_LOG ?= $(ARTIFACTS_DIR)/quality-references.log
REFERENCES_REPORT ?= $(ARTIFACTS_DIR)/quality-references.txt
REFERENCES_BUILD_ARGS = --configuration "$(CONFIGURATION)" -v:q -nologo /clp:ErrorsOnly -p:HeadlessReferenceTrimmer=true "-flp:warningsonly;logfile=$(REFERENCES_LOG)" $(MSBUILD_ARGS)
QUALITY_BUILD_ARGS = --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:NoSummary $(MSBUILD_ARGS)
TEST_MAX_PARALLEL ?= 3
# Local unit-test runs only. The full unit suite (128 modules, 15k tests) measured 142 s at 3, 91 s at 6
# and 92 s at 8 parallel modules on a 14-core machine. TEST_MAX_PARALLEL stays at 3 for integration
# modules, which each start containers, and for CI's smaller runners.
UNIT_TEST_MAX_PARALLEL ?= 6
# Per test module. The affected test stages pass it too (unless TEST_ARGS already carries a --timeout); the longest
# module in recent proof runs took about 7 min under load. It only cancels the session's token, so a test blocked
# where it never observes cancellation runs past it: a broker suite was still running 7 min into a 4m --timeout.
TEST_TIMEOUT ?= 15m
AFFECTED_TIMEOUT_ARGS = $(if $(findstring --timeout,$(TEST_ARGS)),,--timeout $(TEST_TIMEOUT))
# The hang bound that does hold: the Test SDK's dumps run the test host under a controller process that dumps and kills
# its whole process tree once no test has progressed for 10 min. The SDK turns them on only in CI, so a local hang,
# often in an unattended agent run, held its container slot until something outside killed it. Every target that runs
# dotnet test inherits this; EnableTestDumps=false opts out for a debugger session. CI sets the same value, so the SDK
# adds the options once.
export EnableTestDumps ?= true
# Inner-loop build flags. Analyzers are over half of the compiler's CPU on this solution (9 analyzer
# packs, AnalysisMode=All, AnalysisLevel=latest-all, EnforceCodeStyleInBuild), and MinVer shells out
# to git once per src project. Neither changes the emitted IL, so a compile-only loop can skip both.
# Headless.NET.Sdk 0.4.0+ already limits ReportAnalyzer to CI, and RunAnalyzers=false makes it moot.
# Property names verified against Headless.NET.Sdk 0.4.1 (SupportMandatoryAnalyzers.targets) and
# MinVer 8.0.0 (MinVer.targets).
# MinVerSkip pins Version to the SDK default, so alternating a fast build with an analyzed one
# rewrites the generated AssemblyInfo and recompiles the graph; stay on one or the other per session.
FAST_BUILD_ARGS ?= -p:RunAnalyzers=false -p:MinVerSkip=true
# Base ref for the affected-scope targets: the branch's upstream when it is another branch (a stacked
# branch tracking its parent), else origin/main. A branch's own pushed copy is never the base: once the
# branch is pushed, diffing against it selects nothing, and every affected gate then passes with zero
# tests. Resolved once per make invocation (a recursive `?=` would re-run git on every reference).
ifeq ($(origin AFFECTED_BASE),undefined)
AFFECTED_UPSTREAM := $(shell git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null)
AFFECTED_OWN_COPY := origin/$(shell git rev-parse --abbrev-ref HEAD 2>/dev/null)
AFFECTED_BASE := $(or $(filter-out $(AFFECTED_OWN_COPY),$(AFFECTED_UPSTREAM)),origin/main)
endif
# The affected set takes only the direct dependents of a changed project, so the local loop stays short; pull-request
# CI still runs every transitive dependent. AFFECTED_TRANSITIVE=true widens a local run to match CI.
AFFECTED_TRANSITIVE ?= false
AFFECTED_SCOPE_ARGS = $(if $(filter true,$(AFFECTED_TRANSITIVE)),--transitive,)
# Filter targets scope to one project when TEST_PROJECT is set and fall back to the whole solution.
TEST_SCOPE_TARGET = $(if $(TEST_PROJECT),test-project,test)
# repeated <cmd...>: run the command REPEAT times (a flake loop) and report how many runs failed; with REPEAT=1 it
# runs once and keeps the command's own exit status. Every run builds incrementally, a no-op after the first.
REPEAT ?= 1
REPEATED = repeated() { \
	local i=0 failed=""; if [ "$(REPEAT)" -eq 1 ]; then "$$@"; return; fi; \
	while [ $$i -lt $(REPEAT) ]; do i=$$((i + 1)); echo "[repeat] run $$i of $(REPEAT)"; "$$@" || failed="$$failed $$i"; done; \
	if [ -n "$$failed" ]; then echo "[repeat] failed run(s):$$failed of $(REPEAT)" >&2; return 3; fi; \
	echo "[repeat] all $(REPEAT) runs passed"; }; repeated
# Restore is the largest fixed cost in the scoped test loop and almost never has work to do: the
# package graph moves only when Directory.Packages.props, the project file, or its lock file moves.
# Assert the project is restored and current instead of restoring, and name the command that fixes
# it, so a missing restore is one sentence rather than a wall of CS0234/NETSDK1004.
ASSERT_RESTORED = assert_restored() { local project="$$1" dir assets input; dir="$${project%/*}"; assets="$$dir/obj/project.assets.json"; if [ ! -f "$$assets" ]; then printf 'ERROR: %s is not restored (%s is missing).\n       Run: make restore-project PROJECT=%s   (or `make bootstrap` in a fresh clone/worktree)\n' "$$project" "$$assets" "$$project" >&2; return 2; fi; for input in Directory.Packages.props "$$project" "$$dir/packages.lock.json"; do if [ -f "$$input" ] && [ "$$input" -nt "$$assets" ]; then printf 'ERROR: restore is stale for %s (%s is newer than %s).\n       Run: make restore-project PROJECT=%s\n' "$$project" "$$input" "$$assets" "$$project" >&2; return 2; fi; done; }
# A no-op restore leaves project.assets.json untouched, so after an input's timestamp moves without a
# package change (a branch switch, an edit to an unrelated property) the assert above would call the
# project stale forever, however often it is restored. Every restore a target runs therefore stamps
# the assets files it covered; outside make, `make restore` or `make restore-project` clears the error.
MARK_RESTORED = mark_restored() { local project assets; for project in "$$@"; do assets="$${project%/*}/obj/project.assets.json"; if [ -f "$$assets" ]; then touch "$$assets"; fi; done; }
# The solution's project paths, one per line, with the separators the shell expects.
SOLUTION_PROJECTS = sed -n 's/.*<Project Path="\([^"]*\)".*/\1/p' "$(SOLUTION)" | tr '\\' '/'
# Collects every path this side changed, including uncommitted and untracked work. Diffing the merge
# base rather than AFFECTED_BASE itself matters when the branch is behind: a plain two-dot diff also
# reports the commits upstream has and we do not, which are not our changes and not ours to test.
# Affected-scope tooling. scripts/project-graph.py selects projects from the ProjectReference
# graph; scripts/proof.py runs each stage, keeps its log, and reduces TRX, analyzer reports,
# compiler output and coverage into artifacts/proof/<run>/summary.{json,md}. Both are stdlib Python.
PYTHON ?= python3
GRAPH = $(PYTHON) scripts/project-graph.py
PROOF = $(PYTHON) scripts/proof.py
# Each proof target keeps the newest PROOF_KEEP bundles of its own kind (-verify, -integration, ...), counting the one
# it starts, and deletes the older ones; nothing else ever removed a bundle, and one worktree had 52 of them (22 GB).
# test-failed reads the newest -verify, -test, or -integration bundle, which is always kept. PROOF_KEEP=0 keeps every
# bundle.
PROOF_KEEP ?= 3
# One timestamp per make invocation, so every stage of a target writes into the same bundle.
PROOF_RUN := $(ARTIFACTS_DIR)/proof/$(shell date -u +%Y%m%dT%H%M%SZ)
# Coverage costs ~0.4 s per test module plus a merge (~20 s on a whole-solution run), so the inner loop skips
# it and the pre-PR targets keep it. The floors follow the testing diamond: a package with no external dependency
# is held to COVERAGE_UNIT_FLOOR from unit tests alone, and every changed line to COVERAGE_LINE_FLOOR and
# COVERAGE_BRANCH_FLOOR. verify-affected checks the packages without an integration project on unit coverage; a
# package that owns a tests/<Package>.Tests.Integration project is checked by test-affected-integration instead.
AFFECTED_COVERAGE ?= false
VERIFY_COVERAGE ?= true
INTEGRATION_COVERAGE ?= true
COVERAGE_UNIT_FLOOR ?= 60
COVERAGE_LINE_FLOOR ?= 80
COVERAGE_BRANCH_FLOOR ?= 70
# The floors are comply-or-explain: a miss is listed in the proof as a warning and the author either adds the tests or
# explains it in the PR. COVERAGE_GATE=fail turns a miss into a failed proof.
COVERAGE_GATE ?= warn
COVERAGE_FLOOR_ARGS = --unit-floor $(COVERAGE_UNIT_FLOOR) --line-floor $(COVERAGE_LINE_FLOOR) --branch-floor $(COVERAGE_BRANCH_FLOOR) --coverage-gate-mode $(COVERAGE_GATE)
# MTP coverage arguments for a test stage whose `coverage` shell variable is true, into the `coverage_args` array.
COVERAGE_ARGS_FN = coverage_args=(); \
	if [ "$$coverage" = "true" ]; then \
		settings="$$($(DOTNET) msbuild "$(COVERAGE_SETTINGS_PROJECT)" -getProperty:HeadlessCoverageSettingsPath -nologo -v:quiet)"; \
		coverage_args=(--coverage --coverage-output-format cobertura --coverage-settings "$$settings"); \
	fi;
# The proof builds pass eng/ProofErrorLog.targets, so every compile writes its diagnostics, info level included, to
# obj/<configuration>/<tfm>/analyzers.sarif. The analyzer stage reads the changed projects' logs instead of compiling
# them again under `dotnet format analyzers` (which spent ~10 s loading a solution filter before analyzing anything,
# and a 74 s median per verify-affected run); a project whose log is older than its assembly, because something built
# it since without the log, falls back to `dotnet format analyzers` on that project.
# RunAnalyzers=true because the stage trusts these logs: a build under the SDK's local RunAnalyzersDuringBuild=false
# opt-out would still write a log, empty of analyzer results, and the stage would pass on it.
PROOF_ERROR_LOG_ARGS = -p:CustomAfterMicrosoftCommonTargets="$(CURDIR)/eng/ProofErrorLog.targets" -p:RunAnalyzers=true
AFFECTED_ANALYZER_STAGE = if [ -s "$$run/changed.txt" ]; then \
		Configuration="$(CONFIGURATION)" $(PROOF) run --dir "$$run" --name analyzers -- $(PYTHON) scripts/proof.py analyzers --dir "$$run" --configuration "$(CONFIGURATION)" \
			--severity "$(QUALITY_SEVERITY)" $(if $(QUALITY_DIAGNOSTICS),--diagnostics $(QUALITY_DIAGNOSTICS),) \
			-- $(DOTNET) format analyzers --no-restore --verify-no-changes --severity "$(QUALITY_SEVERITY)" -v minimal $(if $(QUALITY_DIAGNOSTICS),--diagnostics $(QUALITY_DIAGNOSTICS),) || true; \
	fi;
AFFECTED_PREPARE = $(ASSERT_RESTORED); $(MARK_RESTORED); prepare() { \
	git rev-parse --verify -q "$(AFFECTED_BASE)^{commit}" >/dev/null || { printf 'ERROR: AFFECTED_BASE=%s does not resolve to a commit. Fetch it, or pass AFFECTED_BASE=<ref>.\n' "$(AFFECTED_BASE)" >&2; return 2; }; \
	$(PROOF) prune --root "$${1%/*}" --keep "$(PROOF_KEEP)" --starting "$$1"; \
	rm -rf "$$1"; $(GRAPH) affected --base "$(AFFECTED_BASE)" $(AFFECTED_SCOPE_ARGS) --out-dir "$$1"; }; prepare
# Restore is asserted, not repeated: the solution-filter restore runs only when a selected project's
# assets are missing or older than Directory.Packages.props or its lock file.
AFFECTED_BUILD_STAGES = if [ -s "$$run/build.txt" ]; then \
		stale=0; while IFS= read -r project; do assert_restored "$$project" 2>/dev/null || stale=1; done < "$$run/build.txt"; \
		if [ $$stale -eq 1 ]; then \
			if $(PROOF) run --dir "$$run" --name restore -- $(DOTNET) restore "$$run/build.slnf" $(RESTORE_ARGS) -v:q -nologo; then \
				while IFS= read -r project; do mark_restored "$$project"; done < "$$run/build.txt"; \
			else status=1; fi; \
		fi; \
		$(PROOF) run --dir "$$run" --name build -- $(DOTNET) build "$$run/build.slnf" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo $(PROOF_ERROR_LOG_ARGS) $(MSBUILD_ARGS) || status=1; \
	fi;
# An analyzer finding sends the change back for an edit, and that edit re-runs every suite, so the integration gate
# analyzes right after the build (it reads the build's compiler logs) and skips its suites on a finding instead of finding out from
# verify-affected after the suites already ran. The skip goes through `status`, as a failed build's does.
AFFECTED_INTEGRATION_ANALYZER_STAGE = if [ $$status -eq 0 ]; then \
		$(AFFECTED_ANALYZER_STAGE) \
		$(PROOF) analyzers-clean --dir "$$run" || { status=1; printf '\033[31m[affected]\033[0m analyzer findings in the changed projects; fix them before the suites run.\n' >&2; }; \
	fi;
# The unit stage runs the list the target names in `units` (unit by default; test-affected-integration narrows it).
AFFECTED_UNIT_STAGE = units="$${units:-unit}"; \
	if [ $$status -ne 0 ]; then $(PROOF) skip --dir "$$run" --name unit-tests --reason "an earlier stage (build or analyzers) failed; fix it first, since the fix re-runs these tests anyway"; \
	elif [ -s "$$run/$$units.txt" ]; then \
		$(COVERAGE_ARGS_FN) \
		$(PROOF) run --dir "$$run" --name unit-tests -- $(DOTNET) test --solution "$$run/$$units.slnf" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$$run/unit-tests" --max-parallel-test-modules $(UNIT_TEST_MAX_PARALLEL) $(AFFECTED_TIMEOUT_ARGS) $(TEST_ARGS) $(TEST_FILTER) $${coverage_args[@]+"$${coverage_args[@]}"} || status=1; \
	else printf '\033[33m[affected]\033[0m %s.txt selects no unit-test project; no unit tests ran.\n' "$$units"; fi;
AFFECTED_INTEGRATION_STAGE = if [ $$status -ne 0 ]; then $(PROOF) skip --dir "$$run" --name integration-tests --reason "an earlier stage (build or analyzers) failed; fix it first, since the fix re-runs these tests anyway"; \
	elif [ -s "$$run/integration.txt" ]; then \
		$(COVERAGE_ARGS_FN) \
		$(PROOF) run --dir "$$run" --name integration-tests -- $(DOTNET) test --solution "$$run/integration.slnf" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$$run/integration-tests" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(AFFECTED_TIMEOUT_ARGS) $(TEST_ARGS) $(TEST_FILTER) $${coverage_args[@]+"$${coverage_args[@]}"} || status=1; \
	else printf '\033[33m[affected]\033[0m no integration-test project covers the affected set; nothing ran.\n'; fi;
# One merged report over whichever suites ran, so the gate reads a line as covered when any test reached it. MTP
# writes each module's report at the results root and the TRX reporter copies it under _<machine>_<time>/In/, so the
# merge reads the root copies only. After a successful merge the per-module reports go: the summary reads the merged
# report, and the per-module copies were ~85% of an integration bundle (650 of 766 MB). A failed merge keeps them.
AFFECTED_COVERAGE_MERGE_STAGE = reports=(); \
	for suite in unit-tests integration-tests; do \
		if [ -n "$$(find "$$run/$$suite" -maxdepth 1 -name '*.cobertura.xml' -print -quit 2>/dev/null)" ]; then reports+=("$$run/$$suite/*.cobertura.xml"); fi; \
	done; \
	if [ $${\#reports[@]} -gt 0 ]; then \
		if $(PROOF) run --dir "$$run" --name coverage-merge -- $(DOTNET) dotnet-coverage merge --nologo --output "$$run/coverage/merged.cobertura.xml" --output-format cobertura "$${reports[@]}"; then \
			find "$$run/unit-tests" "$$run/integration-tests" -name '*.cobertura.xml' -delete 2>/dev/null || true; \
		else status=1; fi; \
	fi;
# The pre-commit hook rewrites staged C# with CSharpier, so a proof that skipped formatting proved bytes the commit
# then changed. The format stage checks the same changed files format-check-changed does, and fails the bundle
# without blocking the build or the tests. bash -c gets the function through `declare -f` because proof.py runs a
# command, not shell text.
AFFECTED_FORMAT_STAGE = $(FORMAT_CHANGED_FN); \
	$(PROOF) run --dir "$$run" --name format -- bash -c "$$(declare -f format_changed); format_changed worktree" || true;
# A change under a dashboard's wwwroot/ is a change to its Dashboard project, so the build stage already runs
# `npm run build` through eng/DashboardSpa.targets, but nothing else lints or unit-tests the SPA. This stage runs
# dashboard-<name>-test for each SPA changed_dashboards selects.
AFFECTED_DASHBOARD_STAGE = $(DASHBOARD_CHECK_FN); $(CHANGED_DASHBOARDS_FN); \
	while read -r name dir; do \
		[ -n "$$name" ] || continue; \
		$(PROOF) run --dir "$$run" --name "dashboard-$$name" -- bash -c "$$(declare -f dashboard_check); dashboard_check \"\$$@\"" bash "$$name" "$$dir" < /dev/null || true; \
	done <<< "$$(changed_dashboards)";
# A gate whose inputs match an earlier passing bundle of its kind reuses that bundle instead of running again, so a
# message-only amend or a rebase onto a main that touched nothing the selection builds costs seconds, not a gate run.
# The key (scripts/project-graph.py input-key) hashes the build closure's files, the build-wide files, the branch's
# patch, the selection, and the settings below; any change to one runs every stage. PROOF_REUSE=false always runs.
PROOF_REUSE ?= true
PROOF_KEY_SETTINGS = $(subst ",,$(subst ',,$(CONFIGURATION) $(TEST_ARGS) $(TEST_FILTER) $(TEST_TIMEOUT) $(MSBUILD_ARGS) $(COVERAGE_FLOOR_ARGS) $(VERIFY_COVERAGE) $(INTEGRATION_COVERAGE) $(QUALITY_SEVERITY) $(QUALITY_DIAGNOSTICS) $(AFFECTED_SCOPE_ARGS)))
# Sets `key` for the bundle in `run`, then exits the recipe with the reused bundle's report when one matches.
PROOF_REUSE_STAGE = key="$$($(GRAPH) input-key --dir "$$run" --salt "$${run\#\#*-} $(PROOF_KEY_SETTINGS)")" || key=""; \
	if [ "$(PROOF_REUSE)" = true ] && $(PROOF) reuse --dir "$$run" --root "$${run%/*}" --key "$$key"; then $(PROOF_REPORT) "$$run"; exit 0; fi;
PROOF_REPORT = report() { cat "$$1/summary.md"; printf '\033[36mProof bundle:\033[0m %s (summary.md for the PR body, summary.json for tools)\n' "$$1"; }; report
DOTNET_OUTDATED_AUDIT_ARGS ?= --no-restore --idle-timeout $(DEPENDENCY_AUDIT_IDLE_TIMEOUT) --output "$(DEPENDENCY_AUDIT_DIR)/outdated.json" --output-format json
DEPENDENCY_SECURITY_AUDIT_ARGS ?= --timeout-seconds "$(DEPENDENCY_SECURITY_AUDIT_TIMEOUT)" --output-dir "$(DEPENDENCY_AUDIT_DIR)/security" --project "$(PROJECT)" --scan vulnerable --include-transitive --scan deprecated
NUGET_ADVISORY_AUDIT_ARGS ?= --timeout-seconds "$(NUGET_ADVISORY_AUDIT_TIMEOUT)" --output-dir "$(DEPENDENCY_AUDIT_DIR)/nuget-advisories" --scan vulnerable --include-transitive

COVERAGE_ARGS ?= -p:EnableCodeCoverage=true --coverage-output-format cobertura
CI_REPORT_ARGS ?= $(if $(GITHUB_ACTIONS),--report-gh,)
CI_TEST_ARGS ?= --report-trx
# Coverage from a second run of the same binaries (e.g. the non-UTC time-zone leg) only duplicates the first.
CI_COVERAGE ?= true
# A pull request builds and tests only what it can affect. ci-scope writes the solution filters here, and
# ci-test runs the unit-test filter instead of every built module when CI_TEST_SOLUTION names one.
CI_SCOPE_DIR ?= $(ARTIFACTS_DIR)/ci-scope
CI_TEST_SOLUTION ?=
CI_TEST_SELECTION = $(if $(CI_TEST_SOLUTION),--solution "$(CI_TEST_SOLUTION)" --configuration "$(CONFIGURATION)" --no-build --no-restore,--test-modules "$(UNIT_TEST_MODULES)" --root-directory "$(CURDIR)")

.PHONY: help
help: ## Show available commands.
	@awk 'BEGIN {FS = ":.*##"; printf "\nCommands:\n"} /^[a-zA-Z0-9_.-]+:.*##/ { printf "  %-28s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)
	@printf "\nExamples (inner loop first):\n"
	@printf "  make test-class CLASS='*CultureHelperTests' TEST_PROJECT=tests/Headless.Extensions.Tests.Unit/Headless.Extensions.Tests.Unit.csproj   # seconds; add REPEAT=5 for a flake\n"
	@printf "  make test-project TEST_PROJECT=tests/Headless.Api.Composition.Tests.Unit/Headless.Api.Composition.Tests.Unit.csproj\n"
	@printf "  make dashboard-jobs-test       # lint, type-check, unit-test, and build one dashboard SPA\n"
	@printf "  make build-affected             # compile the change; the proof names each compiler error\n"
	@printf "  make test-affected              # before the gate: every affected unit-test project, minutes when Jobs is in it\n"
	@printf "  make verify-affected            # the pre-PR proof: format, build, unit tests, analyzers, changed dashboards\n"
	@printf "  make test-affected-integration  # agent-only, needs Docker: integration suites; gates changed lines of packages with an integration project\n"
	@printf "  make mutation PROJECT=src/Headless.Checks/Headless.Checks.csproj   # Stryker.NET mutation score; reported, never gated\n"
	@printf "  make test-failed                # after a narrow fix: only the modules the last proof failed; then verify-affected once\n"
	@printf "  make check                      # the CI gate over the affected scope: check-layering, verify-affected\n"
	@printf "  make quality-analyzers-affected\n"
	@printf "  make bench-compare BENCH_AREA=Caching BENCH_FILTER='*Memory*' BASE=origin/main\n"
	@printf "  make coverage-json\n"
	@printf "  make up && make ready && make seed SCENARIO=progress-running   # dashboard sandbox; make down stops it\n"
	@printf "  make db-q Q=\"select status, progress_percent from headless.time_jobs\"   # after make up STORE=postgres\n"
	@printf "  make pack CONFIGURATION=Release\n"
	@printf "  make doctor JSON=1              # prerequisites per capability group as the project CLI contract report\n\n"
	@printf "Scoping notes:\n"
	@printf "  CONFIGURATION defaults to Release, matching CI; overriding it means a separate set of build outputs.\n"
	@printf "  test-class/-method/-namespace/-trait/-query are solution-wide unless you add TEST_PROJECT=<csproj>.\n"
	@printf "  build-fast/build-project-fast skip analyzers and MinVer: type-checking only, never a quality gate.\n\n"

# doctor report (project CLI contract v1). stdin: one check per line, `group|component|check|status|reason|fix`
# (status: ok fail missing gated unavailable). Prints a table, or the contract JSON when JSON=1, and exits
# 4 when a tool is missing, 1 when a check failed, else 0. One awk pass sees every row, which is what lets
# it derive the six group statuses and the typed exit code; independent `cmd && echo ok` recipe lines
# have no step that sees every state and exit 0 whatever they find.
define doctor_report
awk -F'|' -v json="$(JSON)" ' \
function q(s) { gsub(/\\/, "\\\\", s); gsub(/"/, "\\\"", s); return "\"" s "\"" } \
BEGIN { n = split("run data quality observe debug invariants", G, " ") } \
{ c++; g[c] = $$1; comp[c] = $$2; chk[c] = $$3; st[c] = $$4; why[c] = $$5; fix[c] = $$6; \
  if ($$4 == "missing") code = 4; else if ($$4 == "fail" && code != 4) code = 1; \
  if ($$4 == "fail" || $$4 == "missing" || $$4 == "unavailable") { if (!($$1 in bad)) bad[$$1] = $$3 ": " $$5 } \
  else if ($$4 == "gated") { if (!($$1 in gate)) gate[$$1] = $$3 ": " $$5 } else if ($$4 == "ok") seen[$$1] = 1 } \
END { for (i = 1; i <= n; i++) { grp = G[i]; \
    if (grp in bad) { gs[grp] = "unavailable"; gr[grp] = bad[grp] } \
    else if (grp in gate) { gs[grp] = "gated"; gr[grp] = gate[grp] } \
    else if (grp in seen) { gs[grp] = "available"; gr[grp] = "" } \
    else { gs[grp] = "unavailable"; gr[grp] = "no check declared for this group" } } \
  if (json) { printf "{\"contract\": 1, \"ok\": %s, \"exit\": %d, \"groups\": {", (code ? "false" : "true"), code; \
    for (i = 1; i <= n; i++) printf "%s%s: {\"status\": %s, \"reason\": %s}", (i > 1 ? ", " : ""), q(G[i]), q(gs[G[i]]), q(gr[G[i]]); \
    printf "}, \"checks\": ["; \
    for (i = 1; i <= c; i++) printf "%s{\"group\": %s, \"component\": %s, \"check\": %s, \"status\": %s, \"reason\": %s, \"fix\": %s}", \
      (i > 1 ? ", " : ""), q(g[i]), q(comp[i]), q(chk[i]), q(st[i]), q(why[i]), q(fix[i]); \
    print "]}" } \
  else { for (i = 1; i <= c; i++) printf "%-11s %-10s %-12s %-16s %s%s\n", st[i], g[i], comp[i], chk[i], why[i], (fix[i] != "" ? "  -> " fix[i] : ""); \
    for (i = 1; i <= n; i++) printf "group %-10s %s%s\n", G[i], gs[G[i]], (gr[G[i]] != "" ? ": " gr[G[i]] : "") } \
  exit code }'
endef

# tool <group> <component> <tool> <fix>: ok with the tool's version line, else missing.
define doctor_tool
tool() { if command -v "$$3" >/dev/null 2>&1; then echo "$$1|$$2|$$3|ok|$$("$$3" --version 2>/dev/null | head -n 1 | tr -d '|')|"; else echo "$$1|$$2|$$3|missing|$$3 is not on PATH|$$4"; fi; }
endef

# One row per tool dotnet-tools.json pins, proved from disk instead of `dotnet <tool> --version`, which starts the tool.
# A pinned tool runs once two things exist: the package in the
# NuGet cache (its .nupkg.metadata is written last) and its version in the SDK's local-tool resolver cache, which
# `dotnet tool restore` writes. Either missing is one `make tools` away, so the row is gated on it, never a failure.
define doctor_manifest_tools
pkgs="$${NUGET_PACKAGES:-$$HOME/.nuget/packages}"; cache="$${DOTNET_CLI_HOME:-$$HOME}/.dotnet/toolResolverCache/1"; while read -r id v; do lid=$$(printf '%s' "$$id" | tr 'A-Z' 'a-z'); if [ ! -f "$$pkgs/$$lid/$$v/.nupkg.metadata" ]; then echo "quality|dotnet|$$id|gated|$$id $$v is not in the NuGet cache|make tools"; elif ! grep -qsF "\"Version\":\"$$v\"" "$$cache/$$lid"; then echo "quality|dotnet|$$id|gated|$$id $$v is cached but dotnet tool restore has not registered it|make tools"; else echo "quality|dotnet|$$id|ok|$$v restored|"; fi; done < <(awk -F'"' '/^ *"[^"]+": *\{/ { id = $$2 } /"version": *"/ && id != "" { print id, $$4; id = "" }' dotnet-tools.json)
endef

# The repository is a library; its runtime is the dashboard sandbox (`make up`). `run` needs the SDK, curl, and the
# Node the dashboard SPAs build with. `data` is the sandbox PostgreSQL, so Docker gates it rather than failing
# doctor: STORE=memory and every quality gate run without it. The quality group still has no Docker row: the
# integration suites are opt-in (CI runs none) and test-integration's help line names the requirement. The Docker
# probe is bounded to three seconds (perl's alarm, which macOS ships) so a hung daemon cannot stall doctor. npm is probed by PATH only because a wrapper such as Socket's rejects
# --version. Every probe is a PATH lookup, a --version, or a file test, so doctor answers in about a
# second with no build, restore, or network call.
.PHONY: doctor
doctor: ## Check prerequisites per capability group with a fix for each; JSON=1 prints the project CLI contract report.
	@{ $(doctor_tool); \
	  if ! command -v $(DOTNET) >/dev/null 2>&1; then echo "quality|dotnet|sdk|missing|$(DOTNET) is not on PATH|https://dot.net"; \
	  elif v="$$($(DOTNET) --version 2>/dev/null)"; then echo "quality|dotnet|sdk|ok|$$v|"; \
	  else echo "quality|dotnet|sdk|fail|the SDK global.json pins is not installed|install the SDK global.json names: https://dot.net"; fi; \
	  if [ -f "$(SOLUTION)" ]; then echo "quality|dotnet|solution|ok|$(SOLUTION)|"; else echo "quality|dotnet|solution|fail|$(SOLUTION) not found|make SOLUTION=<path>.slnx"; fi; \
	  $(doctor_manifest_tools); \
	  tool quality scripts $(PYTHON) 'brew install python3'; \
	  if ! command -v node >/dev/null 2>&1; then echo "quality|dashboards|node|missing|node is not on PATH; the dashboard SPAs build with npm (Node 22+)|https://nodejs.org"; \
	  else v="$$(node --version 2>/dev/null)"; major="$${v#v}"; major="$${major%%.*}"; \
	    if [ "$${major:-0}" -ge 22 ]; then echo "quality|dashboards|node|ok|$$v|"; else echo "quality|dashboards|node|fail|$$v is below the Node 22 the dashboard SPAs need|https://nodejs.org"; fi; fi; \
	  if command -v $(NPM) >/dev/null 2>&1; then echo "quality|dashboards|npm|ok|$$(command -v $(NPM))|"; else echo "quality|dashboards|npm|missing|$(NPM) is not on PATH|https://nodejs.org"; fi; \
	  p="$$(git config core.hooksPath || true)"; p="$${p/#\~/$$HOME}"; \
	  if [ "$$p" = .githooks ] || { [ -n "$$p" ] && grep -qs '\.githooks/' "$$p/pre-commit" "$$p/pre-push"; }; then echo "quality|git|hooks|ok|$$p runs .githooks|"; else echo "quality|git|hooks|gated|core.hooksPath does not run .githooks|make hooks"; fi; \
	  if command -v $(PYTHON) >/dev/null 2>&1 && [ -f scripts/project-graph.py ]; then echo "invariants|layering|check-layering|ok|make check-layering|"; else echo "invariants|layering|check-layering|unavailable|needs $(PYTHON) and scripts/project-graph.py|brew install python3"; fi; \
	  if [ -f "$(SANDBOX_PROJECT)" ]; then echo "run|sandbox|project|ok|$(SANDBOX_PROJECT)|"; else echo "run|sandbox|project|fail|$(SANDBOX_PROJECT) not found|make SANDBOX_PROJECT=<path>.csproj"; fi; \
	  if command -v $(DOTNET) >/dev/null 2>&1; then echo "run|sandbox|dotnet|ok|$$(command -v $(DOTNET))|"; else echo "run|sandbox|dotnet|missing|$(DOTNET) is not on PATH|https://dot.net"; fi; \
	  if command -v node >/dev/null 2>&1; then echo "run|sandbox|node|ok|$$(node --version 2>/dev/null)|"; else echo "run|sandbox|node|missing|the sandbox build embeds the dashboard SPAs, built with Node 22+|https://nodejs.org"; fi; \
	  tool run sandbox curl 'brew install curl'; \
	  if ! command -v docker >/dev/null 2>&1; then echo "data|sandbox|docker|gated|docker is not installed; only STORE=postgres and db-q need it|https://docs.docker.com/get-docker/"; \
	  elif perl -e 'alarm 3; exec @ARGV' docker info >/dev/null 2>&1; then echo "data|sandbox|docker|ok|daemon reachable|"; \
	  else echo "data|sandbox|docker|gated|docker daemon not reachable; only STORE=postgres and db-q need it|start Docker Desktop"; fi; \
	  echo "observe|sandbox|logs|ok|make logs reads $(SANDBOX_LOG)|"; \
	} | $(doctor_report)

# The five filter targets below select which tests RUN; on their own they do not narrow what gets
# BUILT. Without TEST_PROJECT they delegate to `test`, which builds all ~430 projects and hands the
# whole solution to the runner (integration modules included, so Docker is required) just to execute
# the handful of matching tests. Add TEST_PROJECT=<csproj> and the same filter runs inside that one
# project instead, which is what a scoped inner loop wants.
.PHONY: test-class
test-class: ## The inner loop: run tests matching CLASS (MTP --filter-class) inside TEST_PROJECT, else solution-wide; REPEAT=N runs it N times and counts the failed runs.
	@test -n "$(CLASS)" || (echo "CLASS is required. Example: make test-class CLASS='*CultureHelperTests' TEST_PROJECT=tests/Headless.Extensions.Tests.Unit/Headless.Extensions.Tests.Unit.csproj" && exit 2)
	@case "$(REPEAT)" in ''|*[!0-9]*|0) echo "REPEAT must be a positive whole number. Example: make test-class CLASS='*CultureHelperTests' TEST_PROJECT=<csproj> REPEAT=5" >&2; exit 2;; esac
	@$(REPEATED) $(MAKE) --no-print-directory $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-class "$(CLASS)"'

.PHONY: test-method
test-method: ## Run tests matching METHOD (MTP --filter-method). Solution-wide unless TEST_PROJECT is set; REPEAT=N runs it N times.
	@test -n "$(METHOD)" || (echo "METHOD is required. Example: make test-method METHOD='*utc_now_should_return_correct_utc_time'" && exit 2)
	@case "$(REPEAT)" in ''|*[!0-9]*|0) echo "REPEAT must be a positive whole number." >&2; exit 2;; esac
	@$(REPEATED) $(MAKE) --no-print-directory $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-method "$(METHOD)"'

.PHONY: test-namespace
test-namespace: ## Run tests matching NAMESPACE (MTP --filter-namespace). Solution-wide unless TEST_PROJECT is set; REPEAT=N runs it N times.
	@test -n "$(NAMESPACE)" || (echo "NAMESPACE is required. Example: make test-namespace NAMESPACE=Headless.Api.Tests" && exit 2)
	@case "$(REPEAT)" in ''|*[!0-9]*|0) echo "REPEAT must be a positive whole number." >&2; exit 2;; esac
	@$(REPEATED) $(MAKE) --no-print-directory $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-namespace "$(NAMESPACE)"'

.PHONY: test-trait
test-trait: ## Run tests matching TRAIT (MTP --filter-trait). Solution-wide unless TEST_PROJECT is set; REPEAT=N runs it N times.
	@test -n "$(TRAIT)" || (echo "TRAIT is required. Example: make test-trait TRAIT='Category=Unit'" && exit 2)
	@case "$(REPEAT)" in ''|*[!0-9]*|0) echo "REPEAT must be a positive whole number." >&2; exit 2;; esac
	@$(REPEATED) $(MAKE) --no-print-directory $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-trait "$(TRAIT)"'

.PHONY: test-query
test-query: ## Run tests matching QUERY (MTP --filter-query). Solution-wide unless TEST_PROJECT is set; REPEAT=N runs it N times.
	@test -n "$(QUERY)" || (echo "QUERY is required. Example: make test-query QUERY='/Headless.Extensions.Tests.Unit/Tests.Core/CultureHelperTests/*'" && exit 2)
	@case "$(REPEAT)" in ''|*[!0-9]*|0) echo "REPEAT must be a positive whole number." >&2; exit 2;; esac
	@$(REPEATED) $(MAKE) --no-print-directory $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-query "$(QUERY)"'

# A narrow fix after a failed verify-affected proves itself on the modules that failed, in seconds to a minute,
# instead of the whole gate again (one Jobs module alone takes ~4 min). It reads the latest -verify, -test, or
# -integration bundle's summary.json; the other failed stages are named with the target that repeats them. The final
# proof is still one full run of the gate that failed.
.PHONY: test-failed
test-failed: ## Re-run only the test projects whose modules failed in the last verify-affected, test-affected, or test-affected-integration proof (PROOF_BUNDLE=<dir> picks another).
	@projects="$$($(PROOF) failed --root "$(ARTIFACTS_DIR)/proof" $(if $(PROOF_BUNDLE),--dir "$(PROOF_BUNDLE)",))"; \
	if [ -z "$$projects" ]; then exit 0; fi; failed=""; \
	for project in $$projects; do \
		$(MAKE) --no-print-directory test-project TEST_PROJECT="$$project" || failed="$$failed $$project"; \
	done; \
	if [ -n "$$failed" ]; then printf '[test-failed] still failing:%s\n' "$$failed" >&2; exit 3; fi; \
	echo "[test-failed] every previously failed module passes. Finish with: make verify-affected"

.PHONY: bootstrap
bootstrap: tools restore hooks ## Initialize a clone/worktree: restore tools, packages, and git hooks.

.PHONY: tools
tools: ## Restore repo-pinned .NET tools.
	$(DOTNET) tool restore

.PHONY: restore
# CI restores in locked mode: a plain restore rewrites a lock file that no longer matches its project's
# references, so drift would pass CI and surface later on an unrelated change. Local restores still update
# lock files; commit them with the reference change.
ifneq ($(CI),)
RESTORE_LOCK_ARGS ?= --locked-mode
endif

restore: ## Restore NuGet packages (locked mode when CI is set).
	$(DOTNET) restore "$(SOLUTION)" -p:Configuration="$(CONFIGURATION)" $(RESTORE_ARGS) $(RESTORE_LOCK_ARGS)
	@$(MARK_RESTORED); $(SOLUTION_PROJECTS) | while IFS= read -r project; do mark_restored "$$project"; done

.PHONY: restore-project
RESTORE_PROJECT = $(DOTNET) restore "$(PROJECT)" -p:Configuration="$(CONFIGURATION)" $(RESTORE_ARGS) $(RESTORE_LOCK_ARGS)

restore-project: ## Restore one project; preferred for focused project work.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make restore-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	$(RESTORE_PROJECT)
	@$(MARK_RESTORED); mark_restored "$(PROJECT)"

.PHONY: hooks
# A repository core.hooksPath replaces the global one, so on a machine whose global hooks already
# dispatch to .githooks (secret scanning and similar checks live there) the repository override is
# removed instead; everywhere else this worktree points at .githooks directly, and the shared setting
# is the fallback for a worktree created without `make hooks`, which would otherwise run no hooks, silently.
hooks: ## Wire the committed hooks in .githooks: through global hooks that dispatch to them, else directly.
	@global="$$(git config --global core.hooksPath || true)"; global="$${global/#\~/$$HOME}"; \
	if [ -n "$$global" ] && grep -qs '\.githooks/' "$$global/pre-commit" "$$global/pre-push"; then \
		git config --worktree --unset core.hooksPath 2>/dev/null || true; \
		git config --local --unset core.hooksPath 2>/dev/null || true; \
		echo "[hooks] global hooks in $$global run .githooks; repository override removed"; \
	else \
		git config --local extensions.worktreeConfig true; \
		git config --worktree core.hooksPath .githooks; \
		git config --local core.hooksPath .githooks; \
		echo "[hooks] core.hooksPath set to .githooks for this worktree"; \
	fi

# CSharpier's "Formatted N files" is the count it processed, not the count it changed, and an agent once read
# "Formatted 30 files" as 30 rewrites and re-ran a build to re-prove them. A file is formatted only when its
# working tree matched the index, so any diff left afterwards is CSharpier's: those files are re-staged and
# listed, and a commit that needed no formatting prints nothing.
.PHONY: hook-pre-commit
hook-pre-commit: ## Git hook: format staged C# files before commit; lists only the files it changed.
	@staged=(); safe=(); skipped=(); changed=(); \
	while IFS= read -r file; do staged+=("$$file"); done < <(git diff --cached --name-only --diff-filter=ACMR -- '*.cs'); \
	if [ "$${#staged[@]}" -eq 0 ]; then exit 0; fi; \
	for file in "$${staged[@]}"; do \
		if git diff --quiet -- "$$file"; then safe+=("$$file"); else skipped+=("$$file"); fi; \
	done; \
	if [ "$${#safe[@]}" -gt 0 ]; then \
		out="$$($(DOTNET) csharpier format "$${safe[@]}" 2>&1)" || { printf '%s\n' "$$out" >&2; echo "[pre-commit] CSharpier failed; nothing was committed" >&2; exit 1; }; \
		while IFS= read -r -d '' file; do changed+=("$$file"); done < <(git -c core.quotePath=false diff -z --name-only -- "$${safe[@]}"); \
		if [ "$${#changed[@]}" -gt 0 ]; then \
			git add -- "$${changed[@]}"; \
			printf '[pre-commit] CSharpier reformatted %d staged file(s) and re-staged them:\n' "$${#changed[@]}"; \
			printf '  %s\n' "$${changed[@]}"; \
		fi; \
	fi; \
	if [ "$${#skipped[@]}" -gt 0 ]; then \
		printf '\033[33m[pre-commit]\033[0m skipped auto-format for %d partially-staged file(s) (formatting the whole file would commit unstaged hunks):\n' "$${#skipped[@]}"; \
		printf '  %s\n' "$${skipped[@]}"; \
		printf 'Stage the whole file, or run: dotnet csharpier format <file>\n'; \
	fi

.PHONY: hook-pre-push
hook-pre-push: hook-pre-push-message hook-format-check ## Git hook: CSharpier-check the changed C# files before push. No build; CI compiles.

.PHONY: hook-pre-push-message
hook-pre-push-message:
	@printf '\033[36m[pre-push]\033[0m format-check on changed files only. NOT checked here: compilation, analyzers, tests — CI does that (skip: --no-verify)...\n'

# Fast local push gate: formatting only. The hook used to build the whole solution --no-restore,
# which cost ~90s per push and failed outright after a merge brought in a new package reference,
# because a build cannot add the missing assets. CI already compiles every project twice (`build`
# with -p:RunAnalyzers=false, `analyzers` with the full analyzer set), both --no-incremental and
# both treating warnings as errors, then runs the unit suite, so the hook was re-proving what the
# merge gate proves anyway. Run `make hook-build` by hand when you want that solution-wide compile
# before pushing. Assumes `make bootstrap` already restored tools (no tool-restore in the hot path).
.PHONY: hook-format-check
hook-format-check: ## Git hook: CSharpier-check the C# files committed since AFFECTED_BASE (a stacked branch's parent, else origin/main).
	@$(FORMAT_CHANGED) committed

# CSharpier costs ~60 ms per path argument and ~0.3 s per directory scan (measured: 80 files 4.5 s as
# paths, 0.67 s as their 6 project folders; 400 files 20.7 s against 1.8 s; the whole repository 5.8 s).
# So the changed files' project folders are scanned once and the report is filtered back to the changed
# files. An unformatted file the change did not touch is listed as a warning, not a failure: it is not
# this change's drift, and CI's whole-repository format-check still fails on it. A non-zero exit that
# names no file (a CSharpier crash) fails. `committed` checks what a push publishes; `worktree` adds
# staged, unstaged, and untracked files, for the local gate. With no merge base the whole tree is checked.
# Past ~100 folders one repository scan is cheaper (2087 files in 345 folders: 14.6 s against 5.8 s).
FORMAT_CHANGED_MAX_DIRS ?= 100
FORMAT_CHANGED_FN = format_changed() { \
	local mode="$$1" base file dir out rc=0 arc=0; local -a files=() dirs=(); \
	base="$$(git merge-base "$(AFFECTED_BASE)" HEAD 2>/dev/null || true)"; \
	if [ -z "$$base" ]; then echo "[format] no merge base with $(AFFECTED_BASE); checking every C\# file"; $(DOTNET) csharpier check .; return; fi; \
	while IFS= read -r -d '' file; do if [ -f "$$file" ]; then files+=("$$file"); fi; done < <( \
		if [ "$$mode" = worktree ]; then \
			git -c core.quotePath=false diff -z --name-only --diff-filter=ACMR "$$base" -- '*.cs'; \
			git -c core.quotePath=false ls-files -z --others --exclude-standard -- '*.cs'; \
		else git -c core.quotePath=false diff -z --name-only --diff-filter=ACMR "$$base"...HEAD -- '*.cs'; fi); \
	if [ "$${\#files[@]}" -eq 0 ]; then echo "[format] no changed C\# files to check"; return 0; fi; \
	while IFS= read -r dir; do dirs+=("$$dir"); done < <(printf '%s\n' "$${files[@]}" | awk -F/ '{ print (NF >= 3 ? $$1 "/" $$2 : $$0) }' | sort -u); \
	if [ "$${\#dirs[@]}" -gt $(FORMAT_CHANGED_MAX_DIRS) ]; then dirs=(.); fi; \
	out="$$($(DOTNET) csharpier check "$${dirs[@]}" 2>&1)" || rc=$$?; \
	awk 'NR == FNR { changed["./" $$0] = 1; next } \
		/^Error \.\// { path = $$0; sub(/^Error /, "", path); sub(/ - [^\/]*$$/, "", path); named++; mine = (path in changed); \
			if (mine) { bad++; print } else other[++others] = path; next } \
		/^  / { if (mine) print; next } \
		{ mine = 0 } \
		END { if (others) { printf "[format] warning: %d unchanged file(s) in the scanned folders are not formatted (not part of this change):\n", others; for (i = 1; i <= others; i++) print "  " other[i] } \
			if (bad) { printf "[format] %d changed file(s) are not formatted or do not parse. Run: make format (a syntax error needs a code fix first)\n", bad; exit 3 } \
			if (named == 0 && rc != 0) exit 1 }' rc="$$rc" <(printf '%s\n' "$${files[@]}") <(printf '%s\n' "$$out") || arc=$$?; \
	if [ $$arc -eq 1 ]; then printf '%s\n' "$$out" >&2; echo "[format] csharpier exited $$rc without naming a file" >&2; return $$rc; fi; \
	if [ $$arc -ne 0 ]; then return $$arc; fi; \
	printf '[format] %d changed C\# file(s) formatted (%d folder(s) scanned)\n' "$${\#files[@]}" "$${\#dirs[@]}"; }
FORMAT_CHANGED = $(FORMAT_CHANGED_FN); format_changed

.PHONY: format-check-changed
format-check-changed: ## Check C# formatting of the files changed vs AFFECTED_BASE, committed or not; the local gate's format stage.
	@$(FORMAT_CHANGED) worktree

# Manual pre-push sanity build; no longer wired into the hook. Warning posture is NOT "warnings stay
# warnings": Headless.NET.Sdk turns CodeAnalysisTreatWarningsAsErrors and MSBuildTreatWarningsAsErrors
# on whenever it detects an agent CLI (SupportDetectLlmContext.props reads CLAUDECODE and friends;
# SupportGeneral.targets applies the gate), so under Claude Code, Codex, Copilot CLI and the rest this
# build fails on any analyzer warning, exactly like CI. A human shell keeps them as warnings. Set
# HeadlessIsLlmContext=false to build with the human posture. Incremental over warm outputs, so an
# up-to-date project is skipped entirely and its analyzers do not run; `make rebuild` or
# `make quality-analyzers` (both --no-incremental) is what actually re-checks everything.
.PHONY: hook-build
hook-build: ## Manual: incremental solution build over warm outputs (no restore, no clean). Agent shells fail on analyzer warnings.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: ci-build
ci-build: format-check rebuild ci-test pack-built verify-packages ## CI: check formatting, clean-build, test with coverage, then pack and verify already-built projects.

# Asserts restore instead of running it: a no-op solution restore costs ~5 s of a ~19 s warm build. A
# package change now needs `make restore` first, which the error names. rebuild keeps its restore, so
# CI's clean builds are unchanged.
.PHONY: build
build: ## Build the solution; asserts restore (run `make restore` after a package change).
	@$(ASSERT_RESTORED); stale=0; first=""; \
	while IFS= read -r project; do assert_restored "$$project" 2>/dev/null || { stale=$$((stale + 1)); [ -n "$$first" ] || first="$$project"; }; done < <($(SOLUTION_PROJECTS)); \
	if [ $$stale -gt 0 ]; then printf 'ERROR: %d solution project(s) are not restored or are stale, first %s.\n       Run: make restore   (or `make bootstrap` in a fresh clone/worktree)\n' "$$stale" "$$first" >&2; exit 1; fi
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: rebuild
rebuild: restore ## Build the solution without incremental compilation.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: rebuild-no-restore
rebuild-no-restore: ## Build without restore or incremental compilation; use after an explicit restore.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: build-project
# Restores in its own recipe rather than through a restore-project prerequisite, so a missing PROJECT
# is reported with this target's example instead of restore-project's.
build-project: ## Build one project; preferred when working on a specified project.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make build-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	$(RESTORE_PROJECT)
	@$(MARK_RESTORED); mark_restored "$(PROJECT)"
	$(DOTNET) build "$(PROJECT)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: build-project-no-restore
build-project-no-restore: ## Build one project without restore; use after restore-project.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make build-project-no-restore PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	$(DOTNET) build "$(PROJECT)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: build-fast
build-fast: ## NOT A QUALITY GATE. Type-check the solution with analyzers and MinVer off; needs a prior `make restore`.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(FAST_BUILD_ARGS) $(MSBUILD_ARGS)

.PHONY: build-project-fast
build-project-fast: ## NOT A QUALITY GATE. Type-check one project with analyzers and MinVer off; needs a prior restore.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make build-project-fast PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	$(DOTNET) build "$(PROJECT)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(FAST_BUILD_ARGS) $(MSBUILD_ARGS)

.PHONY: quality-analyzers
quality-analyzers: ## Report build warnings/errors and analyzer suggestions without writing changes.
	@mkdir -p "$(ARTIFACTS_DIR)"
	@$(DOTNET) restore "$(SOLUTION)" --locked-mode -v:q -nologo
	@if ! $(DOTNET) build "$(SOLUTION)" $(QUALITY_BUILD_ARGS) 2>&1 | tee "$(ARTIFACTS_DIR)/quality-analyzers.log" | awk '/(^|: )(warning|error) [A-Z]+[0-9]+:/'; then \
		echo "Build failed. Full output:"; cat "$(ARTIFACTS_DIR)/quality-analyzers.log"; exit 1; \
	fi
	@format_status=0; \
		Configuration="$(CONFIGURATION)" $(DOTNET) format analyzers "$(SOLUTION)" $(QUALITY_FORMAT_ARGS) > "$(QUALITY_FORMAT_LOG)" 2>&1 || format_status=$$?; \
		awk '!/: hidden [[:alnum:]]+:/' "$(QUALITY_FORMAT_LOG)"; \
		if awk '/: (info|warning|error) [[:alnum:]]+:/ { found=1 } END { exit found ? 0 : 1 }' "$(QUALITY_FORMAT_LOG)"; then exit 3; fi; \
		if [ $$format_status -ne 0 ] && [ $$format_status -ne 2 ]; then cat "$(QUALITY_FORMAT_LOG)"; exit $$format_status; fi

.PHONY: quality-analyzers-project
quality-analyzers-project: ## Report build warnings/errors and analyzer suggestions for PROJECT.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make quality-analyzers-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	@mkdir -p "$(ARTIFACTS_DIR)"
	@$(DOTNET) restore "$(PROJECT)" --locked-mode -v:q -nologo
	@if ! $(DOTNET) build "$(PROJECT)" $(QUALITY_BUILD_ARGS) 2>&1 | tee "$(ARTIFACTS_DIR)/quality-analyzers-project.log" | awk '/(^|: )(warning|error) [A-Z]+[0-9]+:/'; then \
		echo "Build failed. Full output:"; cat "$(ARTIFACTS_DIR)/quality-analyzers-project.log"; exit 1; \
	fi
	@format_status=0; \
		Configuration="$(CONFIGURATION)" $(DOTNET) format analyzers "$(PROJECT)" $(QUALITY_FORMAT_ARGS) > "$(QUALITY_FORMAT_LOG)" 2>&1 || format_status=$$?; \
		awk '!/: hidden [[:alnum:]]+:/' "$(QUALITY_FORMAT_LOG)"; \
		if awk '/: (info|warning|error) [[:alnum:]]+:/ { found=1 } END { exit found ? 0 : 1 }' "$(QUALITY_FORMAT_LOG)"; then exit 3; fi; \
		if [ $$format_status -ne 0 ] && [ $$format_status -ne 2 ]; then cat "$(QUALITY_FORMAT_LOG)"; exit $$format_status; fi

# The contract's name for the analyzer gate. It is quality-analyzers: a --no-incremental rebuild of every project, then
# `dotnet format analyzers` over the solution, exit 3 on findings. quality-analyzers-affected is the scoped form for a
# branch, and the one AGENTS.md points agents at.
.PHONY: lint
lint: quality-analyzers ## Alias of quality-analyzers: whole-solution analyzer gate, exit 3 on findings (slow; quality-analyzers-affected scopes it to the branch).

# quality-analyzers rebuilds all ~430 projects --no-incremental and then formats the whole solution.
# This runs the same gate over only the projects the branch changed: one incremental build that reports compiler
# errors and writes each compile's analyzer findings, every severity, to analyzers.sarif (PROOF_ERROR_LOG_ARGS), then
# an analyzer stage that reads those logs instead of analyzing again; a project with a missing or stale log falls back
# to `dotnet format analyzers`. The result is a proof bundle with every finding grouped by rule. An analyzer finding
# in an untouched project still needs `make quality-analyzers`.
.PHONY: quality-analyzers-affected
quality-analyzers-affected: ## Analyze the projects changed vs AFFECTED_BASE; writes a proof bundle under artifacts/proof/.
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-quality"; \
	run="$(PROOF_RUN)-quality"; status=0; \
	if [ ! -s "$$run/changed.txt" ]; then printf '\033[33m[quality-analyzers-affected]\033[0m no changed project vs %s; nothing analyzed.\n' "$(AFFECTED_BASE)"; exit 0; fi; \
	$(PROOF) run --dir "$$run" --name restore -- $(DOTNET) restore "$$run/changed.slnf" --locked-mode -v:q -nologo || status=1; \
	$(PROOF) run --dir "$$run" --name build -- $(DOTNET) build "$$run/changed.slnf" --configuration "$(CONFIGURATION)" --no-restore -v:minimal -nologo $(PROOF_ERROR_LOG_ARGS) $(MSBUILD_ARGS) || status=1; \
	$(AFFECTED_ANALYZER_STAGE) \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

.PHONY: quality-references
quality-references: ## Report package and project references nothing uses (ReferenceTrimmer) in PROJECT, else the solution; exits 3 on findings.
	@mkdir -p "$(ARTIFACTS_DIR)"; target="$(if $(PROJECT),$(PROJECT),$(SOLUTION))"; \
	$(DOTNET) build "$$target" $(REFERENCES_BUILD_ARGS) || { echo "quality-references: the build failed, so no reference was analyzed. Run: make build" >&2; exit 1; }; \
	ROOT="$(CURDIR)/" perl -ne 'if (/warning (RT000[0-3]): (.*) \[(.*)\]$$/) { my ($$code, $$what, $$project) = ($$1, $$2, $$3); $$project =~ s/^\Q$$ENV{ROOT}\E//; print "$$code\t$$project\t$$what\n" }' "$(REFERENCES_LOG)" | sort -u > "$(REFERENCES_REPORT)"; \
	count=$$(wc -l < "$(REFERENCES_REPORT)" | tr -d ' '); \
	if [ "$$count" = 0 ]; then printf '\033[32m[quality-references]\033[0m no removable reference in %s.\n' "$$target"; exit 0; fi; \
	column -t -s "$$(printf '\t')" "$(REFERENCES_REPORT)"; \
	printf '\033[33m[quality-references]\033[0m %s finding(s); remove each reference, or mark a deliberate one TreatAsUsed="true" with the reason. Report: %s\n' "$$count" "$(REFERENCES_REPORT)"; exit 3

.PHONY: quality-fix
quality-fix: ## Apply analyzer fixes for QUALITY_DIAGNOSTICS, then reformat. Rebuild afterwards to verify.
	@test -n "$(QUALITY_DIAGNOSTICS)" || (echo "QUALITY_DIAGNOSTICS is required. Example: make quality-fix QUALITY_DIAGNOSTICS='MA0002 MA0006'" && exit 2)
	Configuration="$(CONFIGURATION)" $(DOTNET) format analyzers "$(SOLUTION)" $(QUALITY_FIX_ARGS)
	Configuration="$(CONFIGURATION)" $(DOTNET) format style "$(SOLUTION)" $(QUALITY_FIX_ARGS)
	$(DOTNET) csharpier format .

.PHONY: quality-fix-project
quality-fix-project: ## Apply analyzer fixes for QUALITY_DIAGNOSTICS in PROJECT, then reformat.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make quality-fix-project PROJECT=src/Headless.Api/Headless.Api.csproj QUALITY_DIAGNOSTICS=MA0002" && exit 2)
	@test -n "$(QUALITY_DIAGNOSTICS)" || (echo "QUALITY_DIAGNOSTICS is required. Example: make quality-fix-project PROJECT=src/Headless.Api/Headless.Api.csproj QUALITY_DIAGNOSTICS=MA0002" && exit 2)
	Configuration="$(CONFIGURATION)" $(DOTNET) format analyzers "$(PROJECT)" $(QUALITY_FIX_ARGS)
	Configuration="$(CONFIGURATION)" $(DOTNET) format style "$(PROJECT)" $(QUALITY_FIX_ARGS)
	$(DOTNET) csharpier format .

.PHONY: dashboards
dashboards: dashboard-jobs dashboard-messaging ## Rebuild every SPA dashboard (npm ci + vite build into wwwroot/dist).

# Internal: fail early with a clear message when Node/npm is missing.
.PHONY: _node-check
_node-check:
	@command -v $(NPM) >/dev/null 2>&1 || { echo "ERROR: '$(NPM)' not found on PATH. Node 22+ is required to build the dashboards. Install from https://nodejs.org (LTS)."; exit 1; }

.PHONY: dashboard-jobs
dashboard-jobs: _node-check ## Rebuild the Jobs dashboard SPA (npm ci + vite build into wwwroot/dist).
	cd "$(JOBS_DASHBOARD_DIR)" && $(NPM) ci --no-audit --no-fund && $(NPM) run $(DASHBOARD_BUILD_SCRIPT)

.PHONY: dashboard-messaging
dashboard-messaging: _node-check ## Rebuild the Messaging dashboard SPA (npm ci + vite build into wwwroot/dist).
	cd "$(MESSAGING_DASHBOARD_DIR)" && $(NPM) ci --no-audit --no-fund && $(NPM) run $(DASHBOARD_BUILD_SCRIPT)

# The SPA gates CI's dashboard job runs, through each SPA's own package scripts. `type-check` plus `build-only` is
# what `npm run build` runs in parallel; they run apart here so a failure names its stage. The other scripts stay
# out of the CLI on purpose:
# cli-exclude: script:headless-*-dashboard/dev -- a Vite dev server for hands-on SPA work; make up serves the built dashboards
# cli-exclude: script:headless-*-dashboard/preview -- serves a finished bundle; make up drives the dashboards end to end
# cli-exclude: script:headless-*-dashboard/test:unit:watch -- an interactive watcher; dashboard-<name>-test runs the suite once
# cli-exclude: script:headless-*-dashboard/lint -- eslint --fix writes files; the gate runs lint:check
# cli-exclude: script:headless-*-dashboard/format -- prettier --write over src/, which no gate checks
# dashboard_check <name> <dir>: install from the lockfile only when node_modules is missing or older than it (the
# rule eng/DashboardSpa.targets uses), then run every gate script so one failure does not hide the next. Scripts
# run through `npm run`, never npx: a wrapper such as Socket's prints a banner over npx output and once hid an
# eslint result. Exits 3 when a script failed, 4 when npm is missing, 1 when the install failed.
DASHBOARD_CHECK_FN = dashboard_check() { \
	local name="$$1" dir="$$2" script failed=""; \
	command -v $(NPM) >/dev/null 2>&1 || { echo "ERROR: $(NPM) is not on PATH; the dashboard SPAs need Node 22+. Install from https://nodejs.org" >&2; return 4; }; \
	if [ ! -f "$$dir/node_modules/.package-lock.json" ] || [ "$$dir/package-lock.json" -nt "$$dir/node_modules/.package-lock.json" ]; then \
		(cd "$$dir" && $(NPM) ci --no-audit --no-fund) || { echo "[dashboard-$$name] npm ci failed" >&2; return 1; }; \
	fi; \
	(cd "$$dir" && $(NPM) run lint:check) || failed="$$failed lint:check"; \
	(cd "$$dir" && $(NPM) run type-check) || failed="$$failed type-check"; \
	(cd "$$dir" && $(NPM) run test:unit) || failed="$$failed test:unit"; \
	(cd "$$dir" && $(NPM) run build-only) || failed="$$failed build-only"; \
	if [ -n "$$failed" ]; then echo "[dashboard-$$name] failed:$$failed" >&2; return 3; fi; \
	echo "[dashboard-$$name] passed: lint:check type-check test:unit build-only"; }

.PHONY: dashboard-jobs-test
dashboard-jobs-test: ## Lint, type-check, unit-test, and build the Jobs dashboard SPA (CI's Jobs dashboard job).
	@$(DASHBOARD_CHECK_FN); dashboard_check jobs "$(JOBS_DASHBOARD_DIR)"

.PHONY: dashboard-messaging-test
dashboard-messaging-test: ## Lint, type-check, unit-test, and build the Messaging dashboard SPA (CI's Messaging dashboard job).
	@$(DASHBOARD_CHECK_FN); dashboard_check messaging "$(MESSAGING_DASHBOARD_DIR)"

.PHONY: dashboards-test
dashboards-test: ## Run both dashboard SPAs' CI gates (dashboard-jobs-test, dashboard-messaging-test); both run, every failure is reported.
	@$(DASHBOARD_CHECK_FN); rc=0; \
	dashboard_check jobs "$(JOBS_DASHBOARD_DIR)" || rc=$$?; \
	dashboard_check messaging "$(MESSAGING_DASHBOARD_DIR)" || { code=$$?; [ $$code -le $$rc ] || rc=$$code; }; \
	exit $$rc

# changed_dashboards prints "<name> <dir>" for each dashboard SPA this side changed vs the merge base with
# AFFECTED_BASE, committed or not, so a backend-only change pays nothing; with no merge base it prints both. It
# diffs the merge base for the same reason FORMAT_CHANGED does.
CHANGED_DASHBOARDS_FN = changed_dashboards() { \
	local base spa dir; base="$$(git merge-base "$(AFFECTED_BASE)" HEAD 2>/dev/null || true)"; \
	for spa in "jobs $(JOBS_DASHBOARD_DIR)" "messaging $(MESSAGING_DASHBOARD_DIR)"; do dir="$${spa\#* }"; \
		if [ -z "$$base" ] || [ -n "$$(git diff --name-only "$$base" -- "$$dir"; git ls-files --others --exclude-standard -- "$$dir")" ]; then echo "$$spa"; fi; \
	done; }

.PHONY: dashboards-test-affected
dashboards-test-affected: ## Run dashboard-<name>-test for each SPA changed vs AFFECTED_BASE (committed or not); skips unchanged SPAs.
	@$(DASHBOARD_CHECK_FN); $(CHANGED_DASHBOARDS_FN); rc=0; spas="$$(changed_dashboards)"; \
	if [ -z "$$spas" ]; then echo "[dashboards] no dashboard SPA changed vs $(AFFECTED_BASE); nothing ran"; exit 0; fi; \
	while read -r name dir; do dashboard_check "$$name" "$$dir" < /dev/null || { code=$$?; [ $$code -le $$rc ] || rc=$$code; }; done <<< "$$spas"; \
	exit $$rc

# ---- Dashboard sandbox ---------------------------------------------------------------------------------------------
# The repository ships no app, but the Jobs and Messaging dashboards are UI that has to be driven to be tested.
# sandboxes/Headless.Dashboards.Sandbox hosts both on loopback with named scenario fixtures, and these targets own
# its lifecycle: `up` builds and starts it in the background, `ready` waits for it, `seed` creates dashboard states,
# `down` stops only what `up` started. STORE=memory (default) needs nothing beyond the SDK; STORE=postgres also
# starts sandboxes/compose.yaml so stored state survives a restart and `db-q` can read it. The process's pid, its log,
# its state, the revision `up` started from (up.rev), and the generated messaging-dashboard password live in CLI_DIR,
# which is untracked and per checkout, so one worktree's `down` never stops another worktree's sandbox.
SANDBOX_PROJECT ?= sandboxes/Headless.Dashboards.Sandbox/Headless.Dashboards.Sandbox.csproj
SANDBOX_DLL ?= sandboxes/Headless.Dashboards.Sandbox/bin/$(CONFIGURATION)/net10.0/Headless.Dashboards.Sandbox.dll
SANDBOX_PORT ?= 5300
SANDBOX_URL ?= http://127.0.0.1:$(SANDBOX_PORT)
SANDBOX_DIR ?= $(CLI_DIR)
SANDBOX_PID ?= $(SANDBOX_DIR)/sandbox.pid
SANDBOX_LOG ?= $(SANDBOX_DIR)/sandbox.log
SANDBOX_STATE ?= $(SANDBOX_DIR)/sandbox.state
SANDBOX_ENV ?= $(SANDBOX_DIR)/sandbox.env
SANDBOX_REV ?= $(SANDBOX_DIR)/up.rev
SANDBOX_COMPOSE ?= sandboxes/compose.yaml
# One compose project per checkout, so sandboxes in two worktrees never share a database.
SANDBOX_COMPOSE_PROJECT ?= headless-sandbox-$(shell printf '%s' "$(CURDIR)" | cksum | cut -d' ' -f1)
SANDBOX_PG_PORT ?= 55432
SANDBOX_PG_DATABASE ?= headless_sandbox
# What `ready` and `status` read to call the sandbox usable rather than merely answering. Nothing is seeded at startup
# (scenarios are posted on demand), so there is no seeded record to fetch; the Messaging dashboard's stats read goes
# through the message storage, the in-memory store or the PostgreSQL tables the host creates as it starts, so it
# fails until that storage answers. It takes the Messaging dashboard's Basic auth, which sandbox.env sets; the Jobs
# dashboard's API was not used because its auth mode varies with Sandbox__JobsDashboardAuth.
SANDBOX_USABLE_PATH ?= /messaging/api/stats
SANDBOX_USER ?= sandbox
STORE ?= memory
TIMEOUT ?= 120
LINES ?= 200
# TAIL is the project CLI's name for the line count; LINES, the older name here, still works.
TAIL ?= $(LINES)
SCENARIO ?=
Q ?=
JSON ?=
SANDBOX_COMPOSE_CMD = SANDBOX_COMPOSE_PROJECT="$(SANDBOX_COMPOSE_PROJECT)" SANDBOX_PG_PORT="$(SANDBOX_PG_PORT)" docker compose -f "$(SANDBOX_COMPOSE)"

# Every host port `up` binds, by variable name: the sandbox's own, and with STORE=postgres the PostgreSQL port compose
# publishes. Another worktree may hold either default, so `up` refuses a busy one before it starts anything and prints
# the overrides to rerun with; pass the same overrides to ready, status, seed, and down.
UP_PORTS ?= SANDBOX_PORT
ifeq ($(STORE),postgres)
UP_PORTS += SANDBOX_PG_PORT
endif
# VAR=value for each, as an environment prefix on the command up starts.
UP_ENV = $(foreach v,$(UP_PORTS),$(v)=$($(v)))

# The first recipe line of up (and of dev, for its own port): $(call ports_free,<target>,<VARS>).
# Busy is a connect to 127.0.0.1 or an lsof listener (lsof alone misses a socket another user holds;
# a connect alone misses one bound to another address). For each busy port it names the holder and the
# next free port above it, then prints the whole override set to rerun with and exits 1.
define ports_free
busy() { (exec 3<>"/dev/tcp/127.0.0.1/$$1") 2>/dev/null || { command -v lsof >/dev/null 2>&1 && [ -n "$$(lsof -nP -iTCP:"$$1" -sTCP:LISTEN -t 2>/dev/null || true)" ]; }; }; used=" $(foreach v,$(2),$($(v))) "; args=; bad=0; for kv in $(foreach v,$(2),$(v)=$($(v))); do v=$${kv%%=*}; p=$${kv#*=}; if busy "$$p"; then bad=1; who=$$(lsof -nP -iTCP:"$$p" -sTCP:LISTEN 2>/dev/null | awk 'NR == 2 {print " (" $$1 ", pid " $$2 ")"}' || true); c=$$((p + 1)); while [ "$$c" -lt $$((p + 200)) ] && { case "$$used" in *" $$c "*) true ;; *) busy "$$c" ;; esac; }; do c=$$((c + 1)); done; used="$$used$$c "; echo "$(1): port $$p ($$v) is in use by another process$$who." >&2; p=$$c; fi; args="$$args $$v=$$p"; done; if [ "$$bad" = 1 ]; then echo "Run: make $(1)$$args" >&2; exit 1; fi
endef

.PHONY: up
up: ## Build and start the dashboard sandbox in the background (STORE=memory|postgres, SANDBOX_PORT=5300); prints the dashboard URLs.
	@$(call ports_free,up,$(UP_PORTS))
	@set -e; mkdir -p "$(SANDBOX_DIR)"; \
	if [ -f "$(SANDBOX_PID)" ] && kill -0 "$$(cat "$(SANDBOX_PID)")" 2>/dev/null; then \
	  echo "Sandbox already running (pid $$(cat "$(SANDBOX_PID)"), $$(cat "$(SANDBOX_STATE)" 2>/dev/null)). Run: make down"; exit 0; fi; \
	case "$(STORE)" in memory|postgres) ;; *) echo "STORE must be memory or postgres. Example: make up STORE=postgres"; exit 2;; esac; \
	if [ ! -f "$(SANDBOX_ENV)" ]; then umask 077; \
	  printf 'Sandbox__MessagingDashboardPassword=%s\n' "$$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 24)" > "$(SANDBOX_ENV)"; fi; \
	if [ "$(STORE)" = postgres ]; then \
	  command -v docker >/dev/null 2>&1 || { echo "STORE=postgres needs Docker. Install it or run: make up STORE=memory"; exit 4; }; \
	  $(SANDBOX_COMPOSE_CMD) up -d --wait; fi; \
	$(DOTNET) build "$(SANDBOX_PROJECT)" -c "$(CONFIGURATION)" -nologo -v:q -clp:ErrorsOnly -clp:NoSummary; \
	git rev-parse HEAD > "$(SANDBOX_REV)"; \
	set -a; . "$(SANDBOX_ENV)"; set +a; \
	export ASPNETCORE_URLS="$(SANDBOX_URL)" ASPNETCORE_ENVIRONMENT=Development Sandbox__Store="$(STORE)"; \
	if [ "$(STORE)" = postgres ]; then \
	  export Sandbox__PostgresConnectionString="Host=127.0.0.1;Port=$(SANDBOX_PG_PORT);Username=postgres;Database=$(SANDBOX_PG_DATABASE)"; fi; \
	nohup $(DOTNET) "$(SANDBOX_DLL)" > "$(SANDBOX_LOG)" 2>&1 & echo $$! > "$(SANDBOX_PID)"; \
	printf 'store=%s url=%s compose=%s\n' "$(STORE)" "$(SANDBOX_URL)" "$$([ "$(STORE)" = postgres ] && echo "$(SANDBOX_COMPOSE_PROJECT)" || echo none)" > "$(SANDBOX_STATE)"; \
	echo "Sandbox starting (pid $$(cat "$(SANDBOX_PID)"), STORE=$(STORE))."; \
	echo "  Jobs dashboard:      $(SANDBOX_URL)/jobs/dashboard"; \
	if [ -n "$${Sandbox__MessagingDashboardPassword:-}" ]; then \
	  echo "  Messaging dashboard: $(SANDBOX_URL)/messaging (Basic auth: user sandbox, password in $(SANDBOX_ENV))"; \
	else \
	  echo "  Messaging dashboard: $(SANDBOX_URL)/messaging (no auth: the password in $(SANDBOX_ENV) is empty, so inbox and scheduled operations return 403; delete the file and rerun make up for Basic auth)"; fi; \
	echo "  Scenarios:           make seed SCENARIO=<name>; list them at $(SANDBOX_URL)/sandbox/scenarios"; \
	echo "Next: make ready $(UP_ENV)"

.PHONY: ready
ready: ## Wait until the sandbox and both dashboards answer, then until a read through the Messaging store succeeds (TIMEOUT=120 seconds); names what is not usable.
	@url="$$(sed -n 's/.*url=\([^ ]*\).*/\1/p' "$(SANDBOX_STATE)" 2>/dev/null)"; url="$${url:-$(SANDBOX_URL)}"; \
	deadline=$$(( $$(date +%s) + $(TIMEOUT) )); \
	for path in /healthz /jobs/dashboard/ /messaging/; do \
	  until curl -fsS -o /dev/null "$$url$$path" 2>/dev/null; do \
	    if [ -f "$(SANDBOX_PID)" ] && ! kill -0 "$$(cat "$(SANDBOX_PID)")" 2>/dev/null; then \
	      echo "Sandbox exited before $$path answered. Last log lines:"; tail -n 20 "$(SANDBOX_LOG)" 2>/dev/null || true; exit 1; fi; \
	    if [ $$(date +%s) -ge $$deadline ]; then \
	      echo "$$url$$path did not answer within $(TIMEOUT)s. Last log lines:"; tail -n 20 "$(SANDBOX_LOG)" 2>/dev/null || true; exit 1; fi; \
	    sleep 1; done; \
	done; \
	pw="$$(sed -n 's/^Sandbox__MessagingDashboardPassword=//p' "$(SANDBOX_ENV)" 2>/dev/null || true)"; cfg=; \
	if [ -n "$$pw" ]; then cfg="user = \"$(SANDBOX_USER):$$pw\""; fi; \
	until [ -n "$$(printf '%s\n' "$$cfg" | curl -fsS -m 5 -K - "$$url$(SANDBOX_USABLE_PATH)" 2>/dev/null)" ]; do \
	  if [ -f "$(SANDBOX_PID)" ] && ! kill -0 "$$(cat "$(SANDBOX_PID)")" 2>/dev/null; then \
	    echo "Sandbox exited before its Messaging store answered $(SANDBOX_USABLE_PATH). Last log lines:"; tail -n 20 "$(SANDBOX_LOG)" 2>/dev/null || true; exit 1; fi; \
	  if [ $$(date +%s) -ge $$deadline ]; then \
	    echo "The dashboards answer, but $$url$(SANDBOX_USABLE_PATH) (a read through the Messaging store) did not succeed within $(TIMEOUT)s, so the sandbox is not usable. Last log lines:"; tail -n 20 "$(SANDBOX_LOG)" 2>/dev/null || true; exit 1; fi; \
	  sleep 1; done; \
	echo "Sandbox ready at $$url (usable: $(SANDBOX_USABLE_PATH) read the Messaging store)"

.PHONY: down
down: ## Stop the sandbox `up` started, and its PostgreSQL when it started one (data kept; see sandbox-reset).
	@if [ -f "$(SANDBOX_PID)" ]; then pid="$$(cat "$(SANDBOX_PID)")"; \
	  if kill -0 "$$pid" 2>/dev/null; then kill "$$pid"; \
	    for i in 1 2 3 4 5 6 7 8 9 10; do kill -0 "$$pid" 2>/dev/null || break; sleep 1; done; \
	    kill -0 "$$pid" 2>/dev/null && kill -9 "$$pid" || true; echo "Stopped sandbox (pid $$pid)."; \
	  else echo "Sandbox pid $$pid was not running."; fi; rm -f "$(SANDBOX_PID)"; \
	else echo "No sandbox started by make up in this checkout."; fi; \
	compose="$$(sed -n 's/.*compose=\([^ ]*\).*/\1/p' "$(SANDBOX_STATE)" 2>/dev/null)"; \
	if [ -n "$$compose" ] && [ "$$compose" != none ]; then SANDBOX_COMPOSE_PROJECT="$$compose" SANDBOX_PG_PORT="$(SANDBOX_PG_PORT)" docker compose -f "$(SANDBOX_COMPOSE)" down; fi; \
	rm -f "$(SANDBOX_STATE)" "$(SANDBOX_REV)"

# status report (project CLI contract v1). stdin: one service per line, `name|owner|pid|port|built_from|ready|usable`.
# Table, or the contract JSON when JSON=1. Always exits 0. It compares built_from with HEAD and derives drivable and
# the reason that blocks it, so a caller drives only an instance this checkout started from the current revision.
define status_report
awk -F'|' -v json="$(JSON)" -v head="$$(git rev-parse --verify -q HEAD 2>/dev/null || echo unknown)" ' \
function q(s) { gsub(/\\/, "\\\\", s); gsub(/"/, "\\\"", s); return "\"" s "\"" } \
function num(s) { return (s ~ /^[0-9]+$$/) ? s : "null" } \
function yes(s) { return (s == "1" || s == "true") } \
{ n++; nm[n] = $$1; own[n] = $$2; pid[n] = $$3; port[n] = $$4; built[n] = $$5; rd[n] = yes($$6); us[n] = yes($$7); \
  st[n] = (own[n] == "ours" && built[n] != head); \
  if (reason == "") { \
    if (own[n] == "foreign") reason = nm[n] ": port " port[n] " is held by an instance this checkout did not start; stop it or move this stack to a free port"; \
    else if (own[n] == "none") reason = nm[n] ": not running. Run: make up && make ready"; \
    else if (st[n]) reason = nm[n] ": built from " (built[n] == "" ? "an unrecorded revision" : substr(built[n], 1, 12)) ", HEAD is " substr(head, 1, 12) ". Run: make down && make up && make ready"; \
    else if (!us[n]) reason = nm[n] ": " (rd[n] ? "answers but is not usable (migrations or seed missing)" : "not ready") ". Run: make ready" } } \
END { drivable = (n > 0 && reason == ""); if (n == 0) reason = "no service declared"; \
  if (json) { printf "{\"contract\": 1, \"services\": ["; \
    for (i = 1; i <= n; i++) printf "%s{\"name\": %s, \"owner\": %s, \"pid\": %s, \"port\": %s, \"built_from\": %s, \"head\": %s, \"stale\": %s, \"ready\": %s, \"usable\": %s}", \
      (i > 1 ? ", " : ""), q(nm[i]), q(own[i]), num(pid[i]), num(port[i]), (built[i] == "" ? "null" : q(built[i])), q(head), (st[i] ? "true" : "false"), (rd[i] ? "true" : "false"), (us[i] ? "true" : "false"); \
    printf "], \"drivable\": %s, \"reason\": %s}\n", (drivable ? "true" : "false"), q(reason) } \
  else { for (i = 1; i <= n; i++) printf "%-12s %-8s pid %-7s port %-6s built %-12s %s%s %s\n", nm[i], own[i], (pid[i] == "" ? "-" : pid[i]), (port[i] == "" ? "-" : port[i]), \
      (built[i] == "" ? "-" : substr(built[i], 1, 12)), (st[i] ? "stale " : ""), (rd[i] ? "ready" : "not-ready"), (us[i] ? "usable" : "not-usable"); \
    printf "drivable: %s%s\n", (drivable ? "yes" : "no"), (reason != "" ? " (" reason ")" : "") } }'
endef

# One row per service a drive needs. The sandbox is ours when the pid file `up` wrote names a live process, foreign
# when its port answers with no live pid file of this checkout's behind it, and none when nothing answers; the URL and
# port are the ones `up` recorded in the state file, else SANDBOX_URL. `ready` is /healthz answering and `usable` the
# same Messaging store read `ready` waits for, probed only for our own instance so this checkout's password never goes
# to another worktree's process. A PostgreSQL row joins when `up` started the compose database (STORE=postgres): ours
# when this checkout's compose project has the container, ready and usable when its healthcheck reports healthy.
# Every probe is bounded (curl -m, perl's alarm around docker), and nothing is written.
.PHONY: status
status: ## Is this checkout's sandbox running, built from HEAD, and usable? JSON=1 prints the project CLI contract report.
	@{ rev=$$(cat "$(SANDBOX_REV)" 2>/dev/null || true); state="$$(cat "$(SANDBOX_STATE)" 2>/dev/null || true)"; \
	  url="$$(printf '%s' "$$state" | sed -n 's/.*url=\([^ ]*\).*/\1/p')"; url="$${url:-$(SANDBOX_URL)}"; port="$${url##*:}"; \
	  compose="$$(printf '%s' "$$state" | sed -n 's/.*compose=\([^ ]*\).*/\1/p')"; \
	  pid=$$(cat "$(SANDBOX_PID)" 2>/dev/null || true); owner=none; built=; ready=0; usable=0; \
	  if [ -n "$$pid" ] && kill -0 "$$pid" 2>/dev/null; then owner=ours; built=$$rev; \
	  elif (exec 3<>"/dev/tcp/127.0.0.1/$$port") 2>/dev/null; then owner=foreign; pid=$$(lsof -nP -iTCP:"$$port" -sTCP:LISTEN -t 2>/dev/null | head -n 1 || true); \
	  else pid=; fi; \
	  if [ "$$owner" != none ] && curl -fsS -m 2 -o /dev/null "$$url/healthz" 2>/dev/null; then ready=1; \
	    if [ "$$owner" = ours ]; then pw="$$(sed -n 's/^Sandbox__MessagingDashboardPassword=//p' "$(SANDBOX_ENV)" 2>/dev/null || true)"; cfg=; \
	      if [ -n "$$pw" ]; then cfg="user = \"$(SANDBOX_USER):$$pw\""; fi; \
	      [ -z "$$(printf '%s\n' "$$cfg" | curl -fsS -m 5 -K - "$$url$(SANDBOX_USABLE_PATH)" 2>/dev/null)" ] || usable=1; fi; fi; \
	  echo "sandbox|$$owner|$$pid|$$port|$$built|$$ready|$$usable"; \
	  if { [ -n "$$compose" ] && [ "$$compose" != none ]; } || [ "$(STORE)" = postgres ]; then \
	    project="$$compose"; if [ -z "$$project" ] || [ "$$project" = none ]; then project="$(SANDBOX_COMPOSE_PROJECT)"; fi; cid=; \
	    if command -v docker >/dev/null 2>&1; then cid=$$(SANDBOX_PG_PORT="$(SANDBOX_PG_PORT)" perl -e 'alarm shift; exec @ARGV' 5 docker compose -p "$$project" -f "$(SANDBOX_COMPOSE)" ps -q postgres 2>/dev/null | head -n 1 || true); fi; \
	    if [ -n "$$cid" ]; then health=$$(perl -e 'alarm shift; exec @ARGV' 5 docker inspect -f '{{.State.Health.Status}}' "$$cid" 2>/dev/null || true); \
	      ok=0; [ "$$health" != healthy ] || ok=1; echo "postgres|ours||$(SANDBOX_PG_PORT)|$$rev|$$ok|$$ok"; \
	    elif (exec 3<>"/dev/tcp/127.0.0.1/$(SANDBOX_PG_PORT)") 2>/dev/null; then echo "postgres|foreign||$(SANDBOX_PG_PORT)||1|0"; \
	    else echo "postgres|none||$(SANDBOX_PG_PORT)||0|0"; fi; \
	  fi; } | $(status_report)

.PHONY: seed
seed: ## Create dashboard states in the running sandbox: make seed SCENARIO=progress-running (SCENARIO=all runs every one).
	@test -n "$(SCENARIO)" || { echo "SCENARIO is required. Example: make seed SCENARIO=progress-running (list: curl $(SANDBOX_URL)/sandbox/scenarios)"; exit 2; }
	@url="$$(sed -n 's/.*url=\([^ ]*\).*/\1/p' "$(SANDBOX_STATE)" 2>/dev/null)"; url="$${url:-$(SANDBOX_URL)}"; \
	curl -sS --fail-with-body -X POST "$$url/sandbox/scenarios/$(SCENARIO)" || { echo; echo "Seeding failed. Is the sandbox up? Run: make status"; exit 1; }; echo

.PHONY: db-q
db-q: ## Read-only SQL against the sandbox PostgreSQL (STORE=postgres): make db-q Q="select status, progress_percent from headless.time_jobs" (WRITE=1 allows writes).
	@test -n "$(Q)" || { echo 'Q is required. Example: make db-q Q="select status, progress_percent from headless.time_jobs"'; exit 2; }
	@compose="$$(sed -n 's/.*compose=\([^ ]*\).*/\1/p' "$(SANDBOX_STATE)" 2>/dev/null)"; \
	if [ -z "$$compose" ] || [ "$$compose" = none ]; then echo "No sandbox PostgreSQL is running. Run: make up STORE=postgres"; exit 1; fi; \
	pgoptions="-c default_transaction_read_only=on"; [ "$(WRITE)" = 1 ] && pgoptions=""; \
	SANDBOX_COMPOSE_PROJECT="$$compose" SANDBOX_PG_PORT="$(SANDBOX_PG_PORT)" docker compose -f "$(SANDBOX_COMPOSE)" exec -T -e PGOPTIONS="$$pgoptions" postgres \
	  psql -U postgres -d "$(SANDBOX_PG_DATABASE)" -v ON_ERROR_STOP=1 -X -P pager=off -c "$(Q)"

.PHONY: logs
logs: ## Print the last TAIL lines of the sandbox log (TAIL=200; LINES= also works); FOLLOW=1 keeps following it (unbounded).
	@test -f "$(SANDBOX_LOG)" || { echo "No sandbox log yet. Run: make up"; exit 1; }
	@if [ "$(FOLLOW)" = 1 ]; then tail -n "$(TAIL)" -f "$(SANDBOX_LOG)"; else tail -n "$(TAIL)" "$(SANDBOX_LOG)"; fi

.PHONY: sandbox-reset
sandbox-reset: ## Stop the sandbox and delete its PostgreSQL volume and generated password; requires CONFIRM=1.
	@test "$(CONFIRM)" = 1 || { echo "sandbox-reset deletes the sandbox database volume and password. Re-run: make sandbox-reset CONFIRM=1"; exit 2; }
	@if [ -f "$(SANDBOX_PID)" ]; then kill "$$(cat "$(SANDBOX_PID)")" 2>/dev/null || true; rm -f "$(SANDBOX_PID)"; fi
	@if command -v docker >/dev/null 2>&1; then $(SANDBOX_COMPOSE_CMD) down -v; fi
	@rm -f "$(SANDBOX_STATE)" "$(SANDBOX_ENV)" "$(SANDBOX_LOG)" "$(SANDBOX_REV)"; echo "Sandbox reset."

# Tool restore is not in the formatting path: `make bootstrap` restores the pinned tools once, and doctor reports a
# missing one as gated on `make tools`. CI is the exception: its format job starts from a runner with no restored
# tools and calls format-check alone, so with CI set format-check restores them first.
.PHONY: format
format: ## Format C# code with CSharpier.
	$(DOTNET) csharpier format .

.PHONY: format-check
format-check: ## Check C# formatting without writing changes.
	$(DOTNET) csharpier check .

.PHONY: test
test: build ## Build, then run all tests except the chaos tier. Use TEST_FILTER='--filter-class X' for MTP filters.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	@$(WRITE_NO_CHAOS_SOLUTION)
	$(DOTNET) test --solution "$(NO_CHAOS_SOLUTION)" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

.PHONY: test-fast
test-fast: ## Run all tests except the chaos tier without restore/build. Requires existing $(CONFIGURATION) build outputs.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	@$(WRITE_NO_CHAOS_SOLUTION)
	$(DOTNET) test --solution "$(NO_CHAOS_SOLUTION)" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

.PHONY: ci-test
ci-test: ## Run prebuilt unit tests with TRX and Cobertura coverage (CI_COVERAGE=false skips coverage; CI_TEST_SOLUTION=<slnf> limits the modules). Requires existing $(CONFIGURATION) build outputs.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	@coverage_args=(); \
	if [[ "$(CI_COVERAGE)" == "true" ]]; then \
		coverage_settings="$$($(DOTNET) msbuild "$(COVERAGE_SETTINGS_PROJECT)" -getProperty:HeadlessCoverageSettingsPath -nologo -v:quiet)"; \
		if [[ -z "$$coverage_settings" || ! -f "$$coverage_settings" ]]; then \
			echo "Unable to resolve HeadlessCoverageSettingsPath from $(COVERAGE_SETTINGS_PROJECT)." >&2; \
			exit 1; \
		fi; \
		coverage_args=(--coverage --coverage-output-format cobertura --coverage-settings "$$coverage_settings"); \
	fi; \
	$(DOTNET) test $(CI_TEST_SELECTION) --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER) $(CI_REPORT_ARGS) $(CI_TEST_ARGS) $${coverage_args[@]+"$${coverage_args[@]}"}

# Only a pull request narrows the scope, to the changed projects and every transitive dependent plus their
# tests; pushes, releases, dispatches, and any change the project graph cannot attribute to a project (a
# workflow, a script, this Makefile, a build-wide props file, package versions) build everything. The
# key=value lines are written for $$GITHUB_OUTPUT: scope (full, affected or empty), solution (what to
# rebuild), unit_solution (the CI_TEST_SOLUTION for ci-test), and spa (whether a dashboard SPA is built).
.PHONY: ci-scope
ci-scope: ## CI: choose the build and test scope for GITHUB_EVENT_NAME, BASE_SHA and HEAD_SHA; prints key=value lines.
	@$(GRAPH) ci-scope --event "$${GITHUB_EVENT_NAME:-}" --base "$${BASE_SHA:-}" --head "$${HEAD_SHA:-HEAD}" --out-dir "$(CI_SCOPE_DIR)" --solution "$(SOLUTION)"

.PHONY: ci-messaging-conformance-evidence
ci-messaging-conformance-evidence: ## Execute every supported local-broker messaging conformance scenario (Azure uses its protected workflow).
	@mkdir -p "$(TEST_RESULTS_DIR)/messaging-conformance-evidence"
	@set -eu; for module in $(MESSAGING_CONFORMANCE_EVIDENCE_MODULES); do \
		$(DOTNET) test --test-modules "$$module" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)/messaging-conformance-evidence" --max-parallel-test-modules 1 $(TEST_ARGS) $(CI_REPORT_ARGS) --filter-class '*ProviderConformanceEvidenceTests'; \
	done

.PHONY: build-messaging-conformance-evidence
build-messaging-conformance-evidence: ## Restore and build only the messaging conformance-evidence test projects.
	@set -e; for project in $(MESSAGING_CONFORMANCE_EVIDENCE_PROJECTS); do \
		$(DOTNET) build "$$project" --configuration "$(CONFIGURATION)" -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS); \
	done

.PHONY: test-modules
test-modules: build ## Run prebuilt unit and integration test DLLs via MTP --test-modules, one run per glob. Override TEST_MODULES (space-separated globs) if needed.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	@set -f; status=0; for modules in $(TEST_MODULES); do \
		$(DOTNET) test --test-modules "$$modules" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER) || status=1; \
	done; exit $$status

.PHONY: test-project
test-project: ## Run one test project (builds it, asserts restore instead of restoring): make test-project TEST_PROJECT=tests/.../*.csproj
	@test -n "$(TEST_PROJECT)" || (echo "TEST_PROJECT is required. Example: make test-project TEST_PROJECT=tests/Headless.Api.Composition.Tests.Unit/Headless.Api.Composition.Tests.Unit.csproj" && exit 2)
	@mkdir -p "$(TEST_RESULTS_DIR)"
	@$(ASSERT_RESTORED); assert_restored "$(TEST_PROJECT)"
	$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" --no-restore --results-directory "$(TEST_RESULTS_DIR)" $(TEST_ARGS) $(TEST_FILTER)

.PHONY: test-project-fast
test-project-fast: ## Run one prebuilt test project without restore/build.
	@test -n "$(TEST_PROJECT)" || (echo "TEST_PROJECT is required. Example: make test-project-fast TEST_PROJECT=tests/Headless.Api.Composition.Tests.Unit/Headless.Api.Composition.Tests.Unit.csproj" && exit 2)
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$(TEST_RESULTS_DIR)" $(TEST_ARGS) $(TEST_FILTER)

# The affected set comes from the ProjectReference graph (scripts/project-graph.py), not from
# directory names: changed projects plus their direct dependents (every transitive one with
# AFFECTED_TRANSITIVE=true), and every unit- or integration-test project that is in that set or
# references a member of it directly. A change to a build-wide file (global.json, Directory.*.props,
# .editorconfig, eng/DashboardSpa.targets) selects every project below it.
# All selected projects build in one invocation of a generated solution filter, all test modules run
# to completion (one failure no longer hides the rest), and the results land in a proof bundle.
.PHONY: affected
affected: ## Print the projects and test projects affected by changes vs AFFECTED_BASE (JSON).
	@$(GRAPH) affected --base "$(AFFECTED_BASE)" $(AFFECTED_SCOPE_ARGS)

.PHONY: build-affected
build-affected: ## Build the affected projects (changed + direct dependents + their tests) in one solution-filter build.
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-build"; \
	run="$(PROOF_RUN)-build"; status=0; \
	$(AFFECTED_BUILD_STAGES) \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

.PHONY: test-affected
test-affected: ## Build the affected set, then run its *.Tests.Unit projects; writes a proof bundle under artifacts/proof/.
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-test"; \
	run="$(PROOF_RUN)-test"; status=0; coverage="$(AFFECTED_COVERAGE)"; \
	$(AFFECTED_BUILD_STAGES) \
	$(AFFECTED_UNIT_STAGE) \
	$(AFFECTED_COVERAGE_MERGE_STAGE) \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

.PHONY: test-affected-integration
# Agent-run and local only: CI runs no integration suite. verify-affected already gates the packages without an
# integration project, so this target judges only the edited packages that own one: it runs their unit suites beside
# the affected integration suites, both with coverage, and holds those packages' changed lines to the floors. With no
# such package edited (a build-wide change, say) it runs the integration suites without coverage, which costs less.
test-affected-integration: ## Build the affected set, run its integration tests, and gate the changed lines of edited packages that own an integration project on merged unit + integration coverage (needs Docker; local only).
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-integration"; \
	run="$(PROOF_RUN)-integration"; status=0; units=integration-unit; coverage="$(INTEGRATION_COVERAGE)"; \
	$(PROOF_REUSE_STAGE) \
	if [ ! -s "$$run/integration-packages.txt" ]; then coverage=false; fi; \
	$(AFFECTED_BUILD_STAGES) \
	$(AFFECTED_INTEGRATION_ANALYZER_STAGE) \
	$(AFFECTED_UNIT_STAGE) \
	$(AFFECTED_INTEGRATION_STAGE) \
	$(AFFECTED_COVERAGE_MERGE_STAGE) \
	$(PROOF) summarize --dir "$$run" --input-key "$$key" $(if $(filter true,$(INTEGRATION_COVERAGE)),--coverage-gate integration $(COVERAGE_FLOOR_ARGS),) > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

.PHONY: verify-affected
verify-affected: ## Format-check the changed files, then build, unit-test (with coverage), gate unit coverage, and analyze the affected set, plus dashboard-<name>-test for a changed SPA; one proof bundle for the PR body.
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-verify"; \
	run="$(PROOF_RUN)-verify"; status=0; coverage="$(VERIFY_COVERAGE)"; \
	$(PROOF_REUSE_STAGE) \
	$(AFFECTED_FORMAT_STAGE) \
	$(AFFECTED_BUILD_STAGES) \
	$(AFFECTED_UNIT_STAGE) \
	$(AFFECTED_COVERAGE_MERGE_STAGE) \
	$(AFFECTED_ANALYZER_STAGE) \
	$(AFFECTED_DASHBOARD_STAGE) \
	$(PROOF) summarize --dir "$$run" --input-key "$$key" $(if $(filter true,$(VERIFY_COVERAGE)),--coverage-gate unit $(COVERAGE_FLOOR_ARGS),) > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

# The CI gate (project CLI contract v1), over the affected scope. CI runs check-layering, format-check, a
# clean rebuild with analyzers, quality-analyzers-affected, and the unit suite; verify-affected is the
# build, analyzer, and unit stages of that for the projects this branch changed, which is what a local run
# finishes in minutes (the whole-solution version is ci-build, far past ten minutes on ~430 projects).
# Formatting follows the same scope: verify-affected's format stage checks the changed files (~0.3-2 s
# against ~6 s for the whole repository), and CI's format-check job keeps the whole-repository check. A
# changed dashboard SPA gets its lint, type-check, unit, and build stage in verify-affected too, the same
# dashboard-<name>-test CI's dashboard job runs.
# The gates run one after another through a sub-make so a failed gate does not hide the next, and a dry
# run still only prints because the sub-make inherits -n.
CHECK_GATES ?= check-layering verify-affected

.PHONY: check
check: ## CI gate over the affected scope: check-layering, then verify-affected (format, build, tests, analyzers, changed dashboards); every failure is reported.
	@failed=""; for gate in $(CHECK_GATES); do $(MAKE) $$gate || failed="$$failed $$gate"; done; \
	if [ -n "$$failed" ]; then printf '[check] failed:%s\n' "$$failed" >&2; exit 1; fi; \
	printf '[check] passed: %s\n' "$(CHECK_GATES)"

.PHONY: check-layering
check-layering: ## Check package dependency direction under src/ (Abstractions and Core packages).
	@$(GRAPH) layering

# The contract's invariants entry point. A library has no database to hold the wiki's business-rule files
# against, so the invariants here are the repository-level rules; layering is the one scripted today and
# exits 3 on a violation, as the contract asks.
.PHONY: invariants
invariants: check-layering ## Run the repository-level invariants (check-layering); exit 3 on a violation.

.PHONY: test-timeout
test-timeout: ## Run all tests with an explicit MTP timeout. SDK defaults still provide TRX and dumps.
	$(MAKE) test TEST_ARGS='$(TEST_ARGS) --timeout $(TEST_TIMEOUT)'

.PHONY: test-unit
test-unit: build ## Run every *.Tests.Unit module in parallel (honors UNIT_TEST_MAX_PARALLEL, default 6).
	@mkdir -p "$(TEST_RESULTS_DIR)/unit"
	$(DOTNET) test --test-modules "$(UNIT_TEST_MODULES)" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)/unit" --max-parallel-test-modules $(UNIT_TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

.PHONY: test-integration
test-integration: build ## Run every *.Tests.Integration module (honors TEST_MAX_PARALLEL; needs Docker). Lower TEST_MAX_PARALLEL on memory-constrained hosts.
	@mkdir -p "$(TEST_RESULTS_DIR)/integration"
	$(DOTNET) test --test-modules "$(INTEGRATION_TEST_MODULES)" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)/integration" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

# The chaos tier is the one place multi-host delivery guarantees meet real faults, so no other target runs it: the
# affected targets never select a *.Tests.Chaos project to execute, and the whole-solution targets filter it out.
.PHONY: test-messaging-chaos
test-messaging-chaos: ## Run the messaging chaos and soak suite on PostgreSQL and SQL Server (needs Docker; minutes). HEADLESS_CHAOS_PROFILE=full runs the full sizes; HEADLESS_CHAOS_SEED=<n> replays a soak; writes summary.md to MESSAGING_CHAOS_RESULTS_DIR.
	@mkdir -p "$(MESSAGING_CHAOS_RESULTS_DIR)"
	@rm -f "$(MESSAGING_CHAOS_RESULTS_DIR)/summary.md"
	@$(ASSERT_RESTORED); assert_restored "$(MESSAGING_CHAOS_PROJECT)"
	HEADLESS_CHAOS_SUMMARY_DIR="$(abspath $(MESSAGING_CHAOS_RESULTS_DIR))" $(DOTNET) test --project "$(MESSAGING_CHAOS_PROJECT)" --configuration "$(CONFIGURATION)" --no-restore --results-directory "$(MESSAGING_CHAOS_RESULTS_DIR)" $(TEST_ARGS) $(TEST_FILTER)

# The integration tier runs each suite once, with no --retry-failed-tests, so a flaky guarantee shows as a failure
# rather than a pass on the second try. Every suite runs to completion, and the evidence tests export the manifest and
# their scenario bindings beside the TRX files so the drift report can tell a simulator that drifted from the real
# brokers apart from one provider failing. The results directory is cleared first: a stale TRX would be judged as this
# run's.
.PHONY: test-messaging-integration
test-messaging-integration: ## Run the messaging integration suites with no retries, then the drift report (needs Docker; long). MESSAGING_INTEGRATION_PROJECTS narrows the set; MESSAGING_INTEGRATION_REPORT=false skips the report.
	@$(ASSERT_RESTORED); for project in $(MESSAGING_INTEGRATION_PROJECTS); do assert_restored "$$project"; done
	@test -n "$(MESSAGING_INTEGRATION_RESULTS_DIR)" || { echo "MESSAGING_INTEGRATION_RESULTS_DIR is empty." >&2; exit 2; }; rm -rf "$(MESSAGING_INTEGRATION_RESULTS_DIR)"; mkdir -p "$(MESSAGING_INTEGRATION_RESULTS_DIR)"
	@status=0; for project in $(MESSAGING_INTEGRATION_PROJECTS); do \
		printf '\033[36m[integration]\033[0m %s\n' "$$project"; \
		HEADLESS_CONFORMANCE_EXPORT_DIR="$(abspath $(MESSAGING_INTEGRATION_RESULTS_DIR))/conformance" \
			$(DOTNET) test --project "$$project" --configuration "$(CONFIGURATION)" --no-restore --results-directory "$(MESSAGING_INTEGRATION_RESULTS_DIR)" $(TEST_ARGS) $(TEST_FILTER) || status=1; \
	done; \
	if [ "$(MESSAGING_INTEGRATION_REPORT)" = "true" ]; then $(MAKE) --no-print-directory messaging-drift-report MESSAGING_DRIFT_REPORT_ROOT="$(MESSAGING_INTEGRATION_RESULTS_DIR)" || status=1; fi; \
	exit $$status

# Prints what messaging-integration.yml would run for one event, as key=value lines; the workflow's scope job calls it.
.PHONY: messaging-integration-scope
messaging-integration-scope: ## Print the messaging integration run's selection for EVENT (pull_request|push|schedule|workflow_dispatch); a pull request or push diffs against BASE.
	@$(PYTHON) -I scripts/messaging-integration-scope.py select --event "$(EVENT)" --base "$(BASE)" $(if $(CHAOS_PROFILE),--chaos-profile "$(CHAOS_PROFILE)",)

.PHONY: messaging-drift-report
messaging-drift-report: ## Label simulator drift apart from provider failures in a messaging integration run's TRX files and conformance export under MESSAGING_DRIFT_REPORT_ROOT; writes summary.md there and exits 3 on drift.
	@$(PYTHON) -I scripts/messaging-drift-report.py --results "$(MESSAGING_DRIFT_REPORT_ROOT)" --exclude-providers "$(MESSAGING_DRIFT_EXCLUDED_PROVIDERS)" --out "$(MESSAGING_DRIFT_REPORT_ROOT)/summary.md"

# A quarantined cell skips its test until someone marks it Supported, and closing the linked issue does not do that,
# so the scheduled run asks GitHub (read-only, through gh) whether each linked issue is still open.
.PHONY: messaging-quarantine-check
messaging-quarantine-check: ## Fail when a quarantined messaging manifest cell or guarantee links a closed issue (needs an authenticated gh; read-only).
	@$(ASSERT_RESTORED); assert_restored "$(MESSAGING_UNIT_PROJECT)"
	@test -n "$(MESSAGING_QUARANTINE_DIR)" || { echo "MESSAGING_QUARANTINE_DIR is empty." >&2; exit 2; }; rm -rf "$(MESSAGING_QUARANTINE_DIR)"; mkdir -p "$(MESSAGING_QUARANTINE_DIR)"
	@HEADLESS_CONFORMANCE_EXPORT_DIR="$(abspath $(MESSAGING_QUARANTINE_DIR))" $(DOTNET) test --project "$(MESSAGING_UNIT_PROJECT)" --configuration "$(CONFIGURATION)" --no-restore --results-directory "$(MESSAGING_QUARANTINE_DIR)" $(TEST_ARGS) --filter-class '*ConformanceEvidenceExportTests' > "$(MESSAGING_QUARANTINE_DIR)/snapshot-test.log" 2>&1 || { cat "$(MESSAGING_QUARANTINE_DIR)/snapshot-test.log" >&2; exit 1; }
	@$(PYTHON) -I scripts/quarantine-check.py --snapshot "$(MESSAGING_QUARANTINE_DIR)/snapshot.json"

.PHONY: coverage
coverage: tools build ## Collect Cobertura coverage via MTP's in-process coverage extension, chaos tier excluded. TEST_MAX_PARALLEL caps concurrent modules (default 3).
	@mkdir -p "$(COVERAGE_DIR)" "$(TEST_RESULTS_DIR)"
	@$(WRITE_NO_CHAOS_SOLUTION)
	$(DOTNET) test --solution "$(NO_CHAOS_SOLUTION)" --configuration "$(CONFIGURATION)" --no-build \
		--results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) \
		$(TEST_ARGS) $(TEST_FILTER) $(COVERAGE_ARGS)

.PHONY: coverage-html
coverage-html: coverage ## Generate HTML coverage report plus Summary.json.
	$(DOTNET) reportgenerator -reports:"$(TEST_RESULTS_DIR)/**/*.cobertura.xml" -targetdir:"$(COVERAGE_REPORT_DIR)" -reporttypes:"$(COVERAGE_REPORT_TYPES)"

# Mutation testing measures whether the tests check behavior, not only whether they run the lines: Stryker.NET
# plants small faults in the package and counts the ones the unit tests catch. It runs the tests once per mutant, so
# it is a scheduled or on-demand check (mutation.yml runs it weekly), never a PR gate. The Microsoft Testing Platform
# runner is the one that sees xUnit v3 results; the VSTest runner reports a false 0% there.
MUTATION_DIR ?= $(ARTIFACTS_DIR)/mutation
MUTATION_THRESHOLD_HIGH ?= 85
MUTATION_THRESHOLD_LOW ?= 70

.PHONY: mutation
mutation: tools ## Mutation-test one package against its unit tests with Stryker.NET: make mutation PROJECT=src/Headless.Checks/Headless.Checks.csproj (SINCE=<ref> limits it to changed code). Reports the score; never gates.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make mutation PROJECT=src/Headless.Checks/Headless.Checks.csproj" && exit 2)
	@name="$$(basename "$(PROJECT)" .csproj)"; tests="$(CURDIR)/tests/$$name.Tests.Unit/$$name.Tests.Unit.csproj"; \
	test -f "$$tests" || { echo "No unit-test project at tests/$$name.Tests.Unit for $(PROJECT)." >&2; exit 2; }; \
	cd "$(dir $(PROJECT))" && $(DOTNET) dotnet-stryker --test-runner mtp --test-project "$$tests" \
		--reporter html --reporter json --reporter progress --output "$(CURDIR)/$(MUTATION_DIR)/$$name" \
		--threshold-high $(MUTATION_THRESHOLD_HIGH) --threshold-low $(MUTATION_THRESHOLD_LOW) --break-at 0 \
		$(if $(SINCE),--since:$(SINCE),)

.PHONY: coverage-json
coverage-json: coverage-html ## Generate JSON coverage summary at artifacts/coverage/report/Summary.json.
	@test -f "$(COVERAGE_REPORT_DIR)/Summary.json" || (echo "Coverage JSON summary was not generated: $(COVERAGE_REPORT_DIR)/Summary.json" && exit 1)

.PHONY: coverage-open
coverage-open: coverage-html ## Generate report and open in browser.
	@if command -v open >/dev/null 2>&1; then open "$(COVERAGE_REPORT_DIR)/index.html"; \
	elif command -v xdg-open >/dev/null 2>&1; then xdg-open "$(COVERAGE_REPORT_DIR)/index.html"; \
	else echo "Report generated. Open manually: $(COVERAGE_REPORT_DIR)/index.html"; fi

.PHONY: _pack-projects
_pack-projects:
	@mkdir -p "$(PACKAGES_DIR)"
	@rm -rf "$(PACK_LOG_DIR)"; mkdir -p "$(PACK_LOG_DIR)"
	@find src -mindepth 2 -maxdepth 2 -type f -name '*.csproj' -print0 | \
	xargs -0 -P "$(PACK_PARALLELISM)" -n1 bash -c 'project="$$1"; log="$(PACK_LOG_DIR)/$$(basename "$$project" .csproj).log"; \
		echo "Packing $$project"; \
		if ! $(DOTNET) pack "$$project" -m:1 \
			--configuration "$(CONFIGURATION)" \
			--output "$(PACKAGES_DIR)" \
			/p:GenerateSBOM=true \
			/p:SbomGenerationPackageVersion="$(PACKAGE_VERSION)" \
			$(PACK_BUILD_ARGS) \
			$(MSBUILD_ARGS) > "$$log" 2>&1; then \
			echo "FAILED: $$project"; cat "$$log"; exit 1; \
		fi' bash
	@printf '%s\n' "$(PACKAGE_VERSION)" > "$(PACKAGES_DIR)/package-version.txt"

# One `dotnet pack` process per project with a bounded number of concurrent processes. Packing all src
# projects through a single parallel MSBuild traversal (eng/pack.proj) produced 143 of 171 packages in 2m23s
# and then deadlocked: every node went idle with no error while Headless.Core — the project nearly everything
# references — was still unpacked, and the 0.14.0 release job died at its 45-minute timeout. Separate
# processes keep each pack off a shared MSBuild scheduler. Pack runs only on releases, so this does not affect
# the pull-request path.
.PHONY: pack
pack: restore verify-package-manifest ## Pack NuGet packages in bounded parallel processes.
	@mkdir -p "$(PACKAGES_DIR)"
	@$(MAKE) _pack-projects PACK_BUILD_ARGS=--no-restore

# No verify-package-manifest prerequisite: CI always follows with verify-packages, which compares the produced
# package IDs exactly against the manifest, so a pre-pack evaluation of every project would only fail earlier.
.PHONY: pack-built
pack-built: ## Pack already-built src projects in bounded parallel processes; used by CI.
	@mkdir -p "$(PACKAGES_DIR)"
	@$(MAKE) _pack-projects PACK_BUILD_ARGS='--no-restore --no-build'

.PHONY: pack-sbom
pack-sbom: pack ## Alias of pack; every package already embeds an SPDX SBOM.

.PHONY: verify-package-manifest
verify-package-manifest: ## Compare the canonical package IDs with evaluated packable src projects.
	./scripts/verify-packages.sh --projects-only --manifest "$(PACKAGE_MANIFEST)"

.PHONY: verify-packages
verify-packages: ## Verify the complete package set, nuspec metadata, and embedded SPDX SBOMs.
	./scripts/verify-packages.sh --packages-only \
		--manifest "$(PACKAGE_MANIFEST)" \
		--packages-dir "$(PACKAGES_DIR)" \
		--expected-version "$(EXPECTED_PACKAGE_VERSION)" \
		--repository-url "$(EXPECTED_REPOSITORY_URL)" \
		--repository-commit "$(EXPECTED_REPOSITORY_COMMIT)"

.PHONY: nuget-publish-preflight
nuget-publish-preflight: ## Fail when an expected package ID/version already exists on NuGet.org.
	./scripts/verify-packages.sh --packages-only --preflight-nuget \
		--manifest "$(PACKAGE_MANIFEST)" \
		--packages-dir "$(PACKAGES_DIR)" \
		--expected-version "$(EXPECTED_PACKAGE_VERSION)" \
		--repository-url "$(EXPECTED_REPOSITORY_URL)" \
		--repository-commit "$(EXPECTED_REPOSITORY_COMMIT)"

.PHONY: verify-messaging-package-compatibility
verify-messaging-package-compatibility: pack ## Verify current Messaging package family composition and public API usability.
	@set -e; \
	version=$$(sed -n '1p' "$(PACKAGES_DIR)/package-version.txt"); \
	$(DOTNET) restore "$(MESSAGING_COMPATIBILITY_DIR)/NewAllNew/NewAllNew.csproj" \
		--configfile "$(MESSAGING_COMPATIBILITY_DIR)/NewAllNew/NuGet.config" \
		-p:MessagingPackageVersion="$$version"; \
	$(DOTNET) build "$(MESSAGING_COMPATIBILITY_DIR)/NewAllNew/NewAllNew.csproj" \
		--configuration "$(CONFIGURATION)" --no-restore \
		-p:MessagingPackageVersion="$$version"

.PHONY: test-package-verifier
test-package-verifier: ## Run isolated positive and negative package-verifier fixtures.
	./tests/scripts/verify-packages-tests.sh

# Benchmarks report evidence; nothing gates on them. BENCH_AREA names a project under benchmarks/
# (Blobs, Caching, Idempotency.ResponseReplay, Jobs, Messaging, Serializer, Threading). The JSON export is what
# bench-compare reads; the default `short` job trades precision for a run an agent can wait on.
# Every run happens in a temporary worktree: BenchmarkDotNet locates its project by searching the
# repository for <name>.csproj, and the copies under .worktrees/ make that search ambiguous. The
# working tree is captured with `git stash create`, which includes uncommitted changes to tracked
# files (not untracked ones) without touching the index or the stash list.
BENCH_AREA ?=
BENCH_FILTER ?= *
BENCH_JOB ?= short
BENCH_DIR ?= $(ARTIFACTS_DIR)/benchmark-runs
BENCH_ARGS = --filter '$(BENCH_FILTER)' --job $(BENCH_JOB) --exporters json
BENCH_RUN = bench_run() { \
	local ref="$$1" out="$$2" tree rc; tree="$$(mktemp -d "$${TMPDIR:-/tmp}/headless-bench.XXXXXX")"; \
	git worktree add --detach "$$tree" "$$ref" >/dev/null || return 1; \
	printf '\033[36m[bench]\033[0m %s in %s\n' "$$ref" "$$tree"; \
	(cd "$$tree" && $(DOTNET) run --configuration Release --project "benchmarks/Headless.$(BENCH_AREA).Benchmarks" -- $(BENCH_ARGS) --artifacts "$$out") && rc=0 || rc=$$?; \
	git worktree remove --force "$$tree" >/dev/null 2>&1 || rm -rf "$$tree"; return $$rc; }; \
	working_tree_ref() { local ref; ref="$$(git stash create 2>/dev/null || true)"; if [ -n "$$ref" ]; then echo "$$ref"; else git rev-parse HEAD; fi; }

.PHONY: bench
bench: ## Run BENCH_AREA benchmarks for the working tree (BENCH_FILTER, BENCH_JOB=short); JSON under artifacts/benchmark-runs/.
	@test -n "$(BENCH_AREA)" || (echo "BENCH_AREA is required. Example: make bench BENCH_AREA=Caching BENCH_FILTER='*Memory*'" && exit 2)
	@$(BENCH_RUN); bench_run "$$(working_tree_ref)" "$(CURDIR)/$(BENCH_DIR)/$(notdir $(PROOF_RUN))-$(BENCH_AREA)"

.PHONY: bench-compare
bench-compare: ## Run BENCH_AREA benchmarks at BASE (default AFFECTED_BASE) and for the working tree; print a before/after table.
	@test -n "$(BENCH_AREA)" || (echo "BENCH_AREA is required. Example: make bench-compare BENCH_AREA=Caching BENCH_FILTER='*Memory*' BASE=origin/main" && exit 2)
	@base_ref="$(or $(BASE),$(AFFECTED_BASE))"; \
	git rev-parse --verify -q "$$base_ref^{commit}" >/dev/null || { printf 'ERROR: BASE=%s does not resolve to a commit.\n' "$$base_ref" >&2; exit 2; }; \
	out="$(CURDIR)/$(BENCH_DIR)/$(notdir $(PROOF_RUN))-$(BENCH_AREA)-compare"; \
	$(BENCH_RUN); head_ref="$$(working_tree_ref)"; \
	bench_run "$$base_ref" "$$out/base"; bench_run "$$head_ref" "$$out/head"; \
	$(PYTHON) scripts/bench-compare.py --base "$$out/base" --head "$$out/head" --base-label "$$base_ref" | tee "$$out/compare.md"

.PHONY: outdated
outdated: tools ## Check outdated NuGet dependencies.
	$(DOTNET) outdated "$(SOLUTION)"

.PHONY: dependency-audit
dependency-audit: ## Write NuGet outdated dependency JSON report without restore.
	@mkdir -p "$(DEPENDENCY_AUDIT_DIR)"
	$(DOTNET) outdated "$(SOLUTION)" $(DOTNET_OUTDATED_AUDIT_ARGS)

.PHONY: dependency-security-audit
dependency-security-audit: ## Check vulnerable/deprecated NuGet packages for PROJECT without restore; fail on timeout.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make dependency-security-audit PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	./scripts/audit-nuget-advisories.sh $(DEPENDENCY_SECURITY_AUDIT_ARGS)

.PHONY: nuget-advisory-audit
nuget-advisory-audit: ## Scan source packages for NuGet vulnerabilities with bounded per-project logs.
	./scripts/audit-nuget-advisories.sh $(NUGET_ADVISORY_AUDIT_ARGS)

.PHONY: version
version: tools ## Show MinVer-computed version.
	$(DOTNET) minver

.PHONY: list-projects
list-projects: ## List all solution projects.
	@find src demo tests -name '*.csproj' | sort

.PHONY: list-tests
list-tests: ## List all test projects.
	@find tests -name '*.csproj' | sort

.PHONY: clean
clean: ## Clean the solution.
	$(DOTNET) clean "$(SOLUTION)" --configuration "$(CONFIGURATION)" -v:q -nologo
