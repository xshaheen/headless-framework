SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c

.DEFAULT_GOAL := help

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
TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.*.dll
UNIT_TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.Unit.dll
INTEGRATION_TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.Integration.dll
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
QUALITY_BUILD_ARGS = --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:NoSummary $(MSBUILD_ARGS)
TEST_MAX_PARALLEL ?= 3
# Local unit-test runs only. The full unit suite (128 modules, 15k tests) measured 142 s at 3, 91 s at 6
# and 92 s at 8 parallel modules on a 14-core machine. TEST_MAX_PARALLEL stays at 3 for integration
# modules, which each start containers, and for CI's smaller runners.
UNIT_TEST_MAX_PARALLEL ?= 6
TEST_TIMEOUT ?= 15m
# Inner-loop build flags. Analyzers are over half of the compiler's CPU on this solution (9 analyzer
# packs, AnalysisMode=All, AnalysisLevel=latest-all, EnforceCodeStyleInBuild), and MinVer shells out
# to git once per src project. Neither changes the emitted IL, so a compile-only loop can skip both.
# Headless.NET.Sdk 0.4.0+ already limits ReportAnalyzer to CI, and RunAnalyzers=false makes it moot.
# Property names verified against Headless.NET.Sdk 0.4.1 (SupportMandatoryAnalyzers.targets) and
# MinVer 8.0.0 (MinVer.targets).
# MinVerSkip pins Version to the SDK default, so alternating a fast build with an analyzed one
# rewrites the generated AssemblyInfo and recompiles the graph; stay on one or the other per session.
FAST_BUILD_ARGS ?= -p:RunAnalyzers=false -p:MinVerSkip=true
# Base ref for the affected-scope targets: the branch's upstream, else origin/main. Resolved once
# per make invocation (a recursive `?=` would re-run git on every reference).
ifeq ($(origin AFFECTED_BASE),undefined)
AFFECTED_BASE := $(shell git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null || echo origin/main)
endif
# Filter targets scope to one project when TEST_PROJECT is set and fall back to the whole solution.
TEST_SCOPE_TARGET = $(if $(TEST_PROJECT),test-project,test)
# Restore is the largest fixed cost in the scoped test loop and almost never has work to do: the
# package graph moves only when Directory.Packages.props or a project's lock file moves. Assert the
# project is restored and current instead of restoring, and name the command that fixes it, so a
# missing restore is one sentence rather than a wall of CS0234/NETSDK1004.
ASSERT_RESTORED = assert_restored() { local project="$$1" dir assets input; dir="$${project%/*}"; assets="$$dir/obj/project.assets.json"; if [ ! -f "$$assets" ]; then printf 'ERROR: %s is not restored (%s is missing).\n       Run: make restore-project PROJECT=%s   (or `make bootstrap` in a fresh clone/worktree)\n' "$$project" "$$assets" "$$project" >&2; return 2; fi; for input in Directory.Packages.props "$$dir/packages.lock.json"; do if [ -f "$$input" ] && [ "$$input" -nt "$$assets" ]; then printf 'ERROR: restore is stale for %s (%s is newer than %s).\n       Run: make restore-project PROJECT=%s\n' "$$project" "$$input" "$$assets" "$$project" >&2; return 2; fi; done; }
# Collects every path this side changed, including uncommitted and untracked work. Diffing the merge
# base rather than AFFECTED_BASE itself matters when the branch is behind: a plain two-dot diff also
# reports the commits upstream has and we do not, which are not our changes and not ours to test.
# Affected-scope tooling. scripts/project-graph.py selects projects from the ProjectReference
# graph; scripts/proof.py runs each stage, keeps its log, and reduces TRX, analyzer reports,
# compiler output and coverage into artifacts/proof/<run>/summary.{json,md}. Both are stdlib Python.
PYTHON ?= python3
GRAPH = $(PYTHON) scripts/project-graph.py
PROOF = $(PYTHON) scripts/proof.py
# One timestamp per make invocation, so every stage of a target writes into the same bundle.
PROOF_RUN := $(ARTIFACTS_DIR)/proof/$(shell date -u +%Y%m%dT%H%M%SZ)
# Coverage is reported, never gated, and costs ~0.4 s per test module plus a merge (~20 s on a
# whole-solution run), so the inner loop skips it and the pre-PR verify keeps it.
AFFECTED_COVERAGE ?= false
VERIFY_COVERAGE ?= true
# `dotnet format` spends ~10 s loading a solution filter before it analyzes anything (measured: 16 s
# through a one-project filter, 6 s on the project file), but one load per project does not scale.
# Up to this many changed projects it runs per project file; above it, once over the filter.
FORMAT_PER_PROJECT_MAX ?= 2
AFFECTED_ANALYZER_STAGE = if [ -s "$$run/changed.txt" ]; then \
		Configuration="$(CONFIGURATION)" $(PROOF) run --dir "$$run" --name analyzers -- bash -c ' \
			run="$$1"; shift; worst=0; \
			if [ "$$(wc -l < "$$run/changed.txt")" -le $(FORMAT_PER_PROJECT_MAX) ]; then \
				i=0; while IFS= read -r project; do i=$$((i + 1)); \
					$(DOTNET) format analyzers "$$project" "$$@" --report "$$run/analyzers/$$i" || { rc=$$?; [ $$rc -gt $$worst ] && worst=$$rc; }; \
				done < "$$run/changed.txt"; \
			else $(DOTNET) format analyzers "$$run/changed.slnf" "$$@" --report "$$run/analyzers" || worst=$$?; fi; \
			exit $$worst' bash "$$run" --no-restore --verify-no-changes --severity "$(QUALITY_SEVERITY)" -v minimal $(if $(QUALITY_DIAGNOSTICS),--diagnostics $(QUALITY_DIAGNOSTICS),) || true; \
	fi;
AFFECTED_PREPARE = $(ASSERT_RESTORED); prepare() { \
	git rev-parse --verify -q "$(AFFECTED_BASE)^{commit}" >/dev/null || { printf 'ERROR: AFFECTED_BASE=%s does not resolve to a commit. Fetch it, or pass AFFECTED_BASE=<ref>.\n' "$(AFFECTED_BASE)" >&2; return 2; }; \
	rm -rf "$$1"; $(GRAPH) affected --base "$(AFFECTED_BASE)" --out-dir "$$1"; }; prepare
# Restore is asserted, not repeated: the solution-filter restore runs only when a selected project's
# assets are missing or older than Directory.Packages.props or its lock file.
AFFECTED_BUILD_STAGES = if [ -s "$$run/build.txt" ]; then \
		stale=0; while IFS= read -r project; do assert_restored "$$project" 2>/dev/null || stale=1; done < "$$run/build.txt"; \
		if [ $$stale -eq 1 ]; then $(PROOF) run --dir "$$run" --name restore -- $(DOTNET) restore "$$run/build.slnf" $(RESTORE_ARGS) -v:q -nologo || status=1; fi; \
		$(PROOF) run --dir "$$run" --name build -- $(DOTNET) build "$$run/build.slnf" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo $(MSBUILD_ARGS) || status=1; \
	fi;
AFFECTED_UNIT_STAGE = if [ $$status -ne 0 ]; then $(PROOF) skip --dir "$$run" --name unit-tests --reason "an earlier stage failed; --no-build would test stale binaries"; \
	elif [ -s "$$run/unit.txt" ]; then \
		coverage_args=(); \
		if [ "$$coverage" = "true" ]; then \
			settings="$$($(DOTNET) msbuild "$(COVERAGE_SETTINGS_PROJECT)" -getProperty:HeadlessCoverageSettingsPath -nologo -v:quiet)"; \
			coverage_args=(--coverage --coverage-output-format cobertura --coverage-settings "$$settings"); \
		fi; \
		$(PROOF) run --dir "$$run" --name unit-tests -- $(DOTNET) test --solution "$$run/unit.slnf" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$$run/unit-tests" --max-parallel-test-modules $(UNIT_TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER) $${coverage_args[@]+"$${coverage_args[@]}"} || status=1; \
		if [ -n "$$(find "$$run/unit-tests" -name '*.cobertura.xml' -print -quit 2>/dev/null)" ]; then \
			$(PROOF) run --dir "$$run" --name coverage-merge -- $(DOTNET) dotnet-coverage merge --nologo --output "$$run/coverage/merged.cobertura.xml" --output-format cobertura "$$run/unit-tests/**/*.cobertura.xml" || status=1; \
		fi; \
	else printf '\033[33m[affected]\033[0m no unit-test project covers the affected set; nothing ran.\n'; fi;
PROOF_REPORT = report() { cat "$$1/summary.md"; printf '\033[36mProof bundle:\033[0m %s (summary.md for the PR body, summary.json for tools)\n' "$$1"; }; report
DOTNET_OUTDATED_AUDIT_ARGS ?= --no-restore --idle-timeout $(DEPENDENCY_AUDIT_IDLE_TIMEOUT) --output "$(DEPENDENCY_AUDIT_DIR)/outdated.json" --output-format json
DEPENDENCY_SECURITY_AUDIT_ARGS ?= --timeout-seconds "$(DEPENDENCY_SECURITY_AUDIT_TIMEOUT)" --output-dir "$(DEPENDENCY_AUDIT_DIR)/security" --project "$(PROJECT)" --scan vulnerable --include-transitive --scan deprecated
NUGET_ADVISORY_AUDIT_ARGS ?= --timeout-seconds "$(NUGET_ADVISORY_AUDIT_TIMEOUT)" --output-dir "$(DEPENDENCY_AUDIT_DIR)/nuget-advisories" --scan vulnerable --include-transitive

COVERAGE_ARGS ?= -p:EnableCodeCoverage=true --coverage-output-format cobertura
CI_REPORT_ARGS ?= $(if $(GITHUB_ACTIONS),--report-gh,)
CI_TEST_ARGS ?= --report-trx
# Coverage from a second run of the same binaries (e.g. the non-UTC time-zone leg) only duplicates the first.
CI_COVERAGE ?= true

.PHONY: help
help: ## Show available commands.
	@awk 'BEGIN {FS = ":.*##"; printf "\nCommands:\n"} /^[a-zA-Z0-9_.-]+:.*##/ { printf "  %-28s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)
	@printf "\nExamples:\n"
	@printf "  make build\n"
	@printf "  make test-project TEST_PROJECT=tests/Headless.Api.Composition.Tests.Unit/Headless.Api.Composition.Tests.Unit.csproj\n"
	@printf "  make test-class CLASS='*CultureHelperTests' TEST_PROJECT=tests/Headless.Extensions.Tests.Unit/Headless.Extensions.Tests.Unit.csproj\n"
	@printf "  make verify-affected            # build + unit tests + analyzers for the change, with a proof bundle\n"
	@printf "  make test-affected\n"
	@printf "  make quality-analyzers-affected\n"
	@printf "  make bench-compare BENCH_AREA=Caching BENCH_FILTER='*Memory*' BASE=origin/main\n"
	@printf "  make coverage-json\n"
	@printf "  make pack CONFIGURATION=Release\n\n"
	@printf "Scoping notes:\n"
	@printf "  CONFIGURATION defaults to Release, matching CI; overriding it means a separate set of build outputs.\n"
	@printf "  test-class/-method/-namespace/-trait/-query are solution-wide unless you add TEST_PROJECT=<csproj>.\n"
	@printf "  build-fast/build-project-fast skip analyzers and MinVer: type-checking only, never a quality gate.\n\n"

.PHONY: bootstrap
bootstrap: tools restore hooks ## Initialize a clone/worktree: restore tools, packages, and git hooks.

.PHONY: tools
tools: ## Restore repo-pinned .NET tools.
	$(DOTNET) tool restore

.PHONY: restore
restore: ## Restore NuGet packages.
	$(DOTNET) restore "$(SOLUTION)" -p:Configuration="$(CONFIGURATION)" $(RESTORE_ARGS)

.PHONY: restore-project
restore-project: ## Restore one project; preferred for focused project work.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make restore-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	$(DOTNET) restore "$(PROJECT)" -p:Configuration="$(CONFIGURATION)" $(RESTORE_ARGS)

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

.PHONY: hook-pre-commit
hook-pre-commit: ## Git hook: format staged C# files and validate staged docs/solutions frontmatter before commit.
	@staged=(); safe=(); skipped=(); \
	while IFS= read -r file; do staged+=("$$file"); done < <(git diff --cached --name-only --diff-filter=ACMR -- '*.cs'); \
	docs=(); while IFS= read -r file; do docs+=("$$file"); done < <(git diff --cached --name-only --diff-filter=ACMR -- 'docs/solutions/*.md' ':!docs/solutions/INDEX.md'); \
	if [ "$${#docs[@]}" -gt 0 ]; then \
		$(PYTHON) scripts/docs-check.py --quiet-warnings "$${docs[@]}" || { printf '\033[31m[pre-commit]\033[0m fix the docs/solutions frontmatter above (rules: scripts/docs-check.py).\n'; exit 1; }; \
		$(PYTHON) scripts/docs-check.py --quiet-warnings >/dev/null 2>&1 || printf '\033[33m[pre-commit]\033[0m docs/solutions/INDEX.md may be stale; run `make docs-index` and stage it.\n'; \
	fi; \
	if [ "$${#staged[@]}" -eq 0 ]; then exit 0; fi; \
	for file in "$${staged[@]}"; do \
		if git diff --quiet -- "$$file"; then safe+=("$$file"); else skipped+=("$$file"); fi; \
	done; \
	if [ "$${#safe[@]}" -gt 0 ]; then $(DOTNET) csharpier format "$${safe[@]}"; git add -- "$${safe[@]}"; fi; \
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
hook-format-check: ## Git hook: CSharpier-check only the C# files changed vs upstream.
	@base=$$(git rev-parse --verify -q '@{upstream}' 2>/dev/null || git merge-base origin/main HEAD 2>/dev/null || true); \
	if [ -n "$$base" ]; then \
		files=$$(git -c core.quotePath=false diff --name-only --diff-filter=ACMR "$$base"...HEAD -- '*.cs'); \
	else \
		files=$$(git -c core.quotePath=false ls-files '*.cs'); \
	fi; \
	if [ -z "$$files" ]; then echo "[pre-push] no changed C# files to check"; exit 0; fi; \
	printf '%s\n' "$$files" | tr '\n' '\0' | xargs -0 $(DOTNET) csharpier check

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

.PHONY: build
build: restore ## Build the solution.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: rebuild
rebuild: restore ## Build the solution without incremental compilation.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: rebuild-no-restore
rebuild-no-restore: ## Build without restore or incremental compilation; use after an explicit restore.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: build-project
build-project: restore-project ## Build one project; preferred when working on a specified project.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make build-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
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
		if awk '/: (info|warning|error) [[:alnum:]]+:/ { found=1 } END { exit found ? 0 : 1 }' "$(QUALITY_FORMAT_LOG)"; then exit 2; fi; \
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
		if awk '/: (info|warning|error) [[:alnum:]]+:/ { found=1 } END { exit found ? 0 : 1 }' "$(QUALITY_FORMAT_LOG)"; then exit 2; fi; \
		if [ $$format_status -ne 0 ] && [ $$format_status -ne 2 ]; then cat "$(QUALITY_FORMAT_LOG)"; exit $$format_status; fi

# quality-analyzers rebuilds all ~430 projects --no-incremental and then formats the whole solution.
# This runs the same gate over only the projects the branch changed: an incremental build for compiler
# errors, then `dotnet format analyzers`, which runs every analyzer itself at every severity, so a
# separate --no-incremental analyzer build would repeat its work. The result is a proof bundle with every
# finding grouped by rule. An analyzer finding in an untouched project still needs `make quality-analyzers`.
.PHONY: quality-analyzers-affected
quality-analyzers-affected: ## Analyze the projects changed vs AFFECTED_BASE; writes a proof bundle under artifacts/proof/.
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-quality"; \
	run="$(PROOF_RUN)-quality"; status=0; \
	if [ ! -s "$$run/changed.txt" ]; then printf '\033[33m[quality-analyzers-affected]\033[0m no changed project vs %s; nothing analyzed.\n' "$(AFFECTED_BASE)"; exit 0; fi; \
	$(PROOF) run --dir "$$run" --name restore -- $(DOTNET) restore "$$run/changed.slnf" --locked-mode -v:q -nologo || status=1; \
	$(PROOF) run --dir "$$run" --name build -- $(DOTNET) build "$$run/changed.slnf" --configuration "$(CONFIGURATION)" --no-restore -v:minimal -nologo $(MSBUILD_ARGS) || status=1; \
	$(AFFECTED_ANALYZER_STAGE) \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

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

.PHONY: format
format: tools ## Format C# code with CSharpier.
	$(DOTNET) csharpier format .

.PHONY: format-check
format-check: tools ## Check C# formatting without writing changes.
	$(DOTNET) csharpier check .

.PHONY: test
test: build ## Build, then run all tests. Use TEST_FILTER='--filter-class X' for MTP filters.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --solution "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

.PHONY: test-fast
test-fast: ## Run all tests without restore/build. Requires existing $(CONFIGURATION) build outputs.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --solution "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

.PHONY: ci-test
ci-test: ## Run prebuilt unit tests with TRX and Cobertura coverage (CI_COVERAGE=false skips coverage). Requires existing $(CONFIGURATION) build outputs.
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
	$(DOTNET) test --test-modules "$(UNIT_TEST_MODULES)" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER) $(CI_REPORT_ARGS) $(CI_TEST_ARGS) $${coverage_args[@]+"$${coverage_args[@]}"}

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
test-modules: build ## Run prebuilt test DLLs via MTP --test-modules. Override TEST_MODULES if needed.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --test-modules "$(TEST_MODULES)" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

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

# The five filter targets below select which tests RUN; on their own they do not narrow what gets
# BUILT. Without TEST_PROJECT they delegate to `test`, which builds all ~430 projects and hands the
# whole solution to the runner (integration modules included, so Docker is required) just to execute
# the handful of matching tests. Add TEST_PROJECT=<csproj> and the same filter runs inside that one
# project instead, which is what a scoped inner loop wants.
.PHONY: test-class
test-class: ## Run tests matching CLASS (MTP --filter-class). Solution-wide unless TEST_PROJECT is set.
	@test -n "$(CLASS)" || (echo "CLASS is required. Example: make test-class CLASS='*CultureHelperTests' TEST_PROJECT=tests/Headless.Extensions.Tests.Unit/Headless.Extensions.Tests.Unit.csproj" && exit 2)
	$(MAKE) $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-class "$(CLASS)"'

.PHONY: test-method
test-method: ## Run tests matching METHOD (MTP --filter-method). Solution-wide unless TEST_PROJECT is set.
	@test -n "$(METHOD)" || (echo "METHOD is required. Example: make test-method METHOD='*utc_now_should_return_correct_utc_time'" && exit 2)
	$(MAKE) $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-method "$(METHOD)"'

.PHONY: test-namespace
test-namespace: ## Run tests matching NAMESPACE (MTP --filter-namespace). Solution-wide unless TEST_PROJECT is set.
	@test -n "$(NAMESPACE)" || (echo "NAMESPACE is required. Example: make test-namespace NAMESPACE=Headless.Api.Tests" && exit 2)
	$(MAKE) $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-namespace "$(NAMESPACE)"'

.PHONY: test-trait
test-trait: ## Run tests matching TRAIT (MTP --filter-trait). Solution-wide unless TEST_PROJECT is set.
	@test -n "$(TRAIT)" || (echo "TRAIT is required. Example: make test-trait TRAIT='Category=Unit'" && exit 2)
	$(MAKE) $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-trait "$(TRAIT)"'

.PHONY: test-query
test-query: ## Run tests matching QUERY (MTP --filter-query). Solution-wide unless TEST_PROJECT is set.
	@test -n "$(QUERY)" || (echo "QUERY is required. Example: make test-query QUERY='/Headless.Extensions.Tests.Unit/Tests.Core/CultureHelperTests/*'" && exit 2)
	$(MAKE) $(TEST_SCOPE_TARGET) TEST_FILTER='--filter-query "$(QUERY)"'

# The affected set comes from the ProjectReference graph (scripts/project-graph.py), not from
# directory names: changed projects plus their direct dependents, and every unit- or integration-test
# project that is in that set or references a member of it directly. A change to a build-wide file
# (global.json, Directory.*.props, .editorconfig, eng/analyzers) selects every project below it.
# All selected projects build in one invocation of a generated solution filter, all test modules run
# to completion (one failure no longer hides the rest), and the results land in a proof bundle.
.PHONY: affected
affected: ## Print the projects and test projects affected by changes vs AFFECTED_BASE (JSON).
	@$(GRAPH) affected --base "$(AFFECTED_BASE)"

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
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

.PHONY: test-affected-integration
test-affected-integration: ## Build the affected set, then run its *.Tests.Integration projects (needs Docker).
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-integration"; \
	run="$(PROOF_RUN)-integration"; status=0; \
	$(AFFECTED_BUILD_STAGES) \
	if [ $$status -ne 0 ]; then $(PROOF) skip --dir "$$run" --name integration-tests --reason "an earlier stage failed; --no-build would test stale binaries"; \
	elif [ -s "$$run/integration.txt" ]; then \
		$(PROOF) run --dir "$$run" --name integration-tests -- $(DOTNET) test --solution "$$run/integration.slnf" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$$run/integration-tests" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER) || status=1; \
	fi; \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

.PHONY: verify-affected
verify-affected: ## Build, unit-test (with coverage), and analyze the affected set; one proof bundle for the PR body.
	@$(AFFECTED_PREPARE) "$(PROOF_RUN)-verify"; \
	run="$(PROOF_RUN)-verify"; status=0; coverage="$(VERIFY_COVERAGE)"; \
	$(AFFECTED_BUILD_STAGES) \
	$(AFFECTED_UNIT_STAGE) \
	$(AFFECTED_ANALYZER_STAGE) \
	$(PROOF) summarize --dir "$$run" > /dev/null || status=1; \
	$(PROOF_REPORT) "$$run"; exit $$status

.PHONY: check-layering
check-layering: ## Check package dependency direction under src/ (Abstractions and Core packages).
	@$(GRAPH) layering

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

.PHONY: coverage
coverage: tools build ## Collect Cobertura coverage via MTP's in-process coverage extension. TEST_MAX_PARALLEL caps concurrent modules (default 3).
	@mkdir -p "$(COVERAGE_DIR)" "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --solution "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-build \
		--results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) \
		$(TEST_ARGS) $(TEST_FILTER) $(COVERAGE_ARGS)

.PHONY: coverage-html
coverage-html: coverage ## Generate HTML coverage report plus Summary.json.
	$(DOTNET) reportgenerator -reports:"$(TEST_RESULTS_DIR)/**/*.cobertura.xml" -targetdir:"$(COVERAGE_REPORT_DIR)" -reporttypes:"$(COVERAGE_REPORT_TYPES)"

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

.PHONY: docs-check
docs-check: ## Validate docs/solutions frontmatter (fails) and references (warns), and check INDEX.md is current.
	@$(PYTHON) scripts/docs-check.py

.PHONY: docs-index
docs-index: ## Regenerate docs/solutions/INDEX.md from each learning's frontmatter.
	@$(PYTHON) scripts/docs-check.py --write-index --quiet-warnings

# Benchmarks report evidence; nothing gates on them. BENCH_AREA names a project under benchmarks/
# (Api.Idempotency, Blobs, Caching, Jobs, Messaging, Serializer). The JSON export is what
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
	working_tree_ref() { git stash create 2>/dev/null || git rev-parse HEAD; }

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
