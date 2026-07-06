SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c

.DEFAULT_GOAL := help

DOTNET ?= dotnet
NPM ?= npm
SOLUTION ?= headless-framework.slnx
JOBS_DASHBOARD_DIR ?= src/Headless.Jobs.Dashboard/wwwroot
MESSAGING_DASHBOARD_DIR ?= src/Headless.Messaging.Dashboard/wwwroot
CONFIGURATION ?= Release
ARTIFACTS_DIR ?= artifacts
PACKAGES_DIR ?= $(ARTIFACTS_DIR)/packages-results
TEST_RESULTS_DIR ?= $(ARTIFACTS_DIR)/test-results
COVERAGE_DIR ?= $(ARTIFACTS_DIR)/coverage
COVERAGE_REPORT_DIR ?= $(COVERAGE_DIR)/report
COVERAGE_REPORT_TYPES ?= Html;JsonSummary
PROJECT ?=
TEST_PROJECT ?=
TEST_FILTER ?=
TEST_ARGS ?= --no-progress
TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.*.dll
UNIT_TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.Unit.dll
INTEGRATION_TEST_MODULES ?= tests/**/bin/$(CONFIGURATION)/**/*.Tests.Integration.dll
MSBUILD_ARGS ?=
QUALITY_SEVERITY ?= info
QUALITY_DIAGNOSTICS ?=
QUALITY_FORMAT_ARGS = --no-restore --verify-no-changes --severity "$(QUALITY_SEVERITY)" -v minimal $(if $(QUALITY_DIAGNOSTICS),--diagnostics $(QUALITY_DIAGNOSTICS),)
QUALITY_BUILD_ARGS = --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:NoSummary $(MSBUILD_ARGS)
TEST_MAX_PARALLEL ?= 3
TEST_TIMEOUT ?= 15m

COVERAGE_ARGS ?= -p:EnableCodeCoverage=true --coverage-output-format cobertura
CI_TEST_ARGS ?= --report-trx --coverage --coverage-output-format cobertura

.PHONY: help
help: ## Show available commands.
	@awk 'BEGIN {FS = ":.*##"; printf "\nCommands:\n"} /^[a-zA-Z0-9_.-]+:.*##/ { printf "  %-28s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)
	@printf "\nExamples:\n"
	@printf "  make build\n"
	@printf "  make test-project TEST_PROJECT=tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj\n"
	@printf "  make test-class CLASS='*ClockTests'\n"
	@printf "  make quality-analyzers-project PROJECT=src/Headless.Api/Headless.Api.csproj\n"
	@printf "  make coverage-json\n"
	@printf "  make pack CONFIGURATION=Release\n\n"

.PHONY: bootstrap
bootstrap: tools restore hooks ## Initialize a clone/worktree: restore tools, packages, and git hooks.

.PHONY: tools
tools: ## Restore repo-pinned .NET tools.
	$(DOTNET) tool restore

.PHONY: restore
restore: ## Restore NuGet packages.
	$(DOTNET) restore "$(SOLUTION)" -p:Configuration="$(CONFIGURATION)"

.PHONY: restore-project
restore-project: ## Restore one project; preferred for focused project work.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make restore-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	$(DOTNET) restore "$(PROJECT)" -p:Configuration="$(CONFIGURATION)"

.PHONY: hooks
hooks: ## Point git at the committed hooks (per clone/worktree).
	git config core.hooksPath .husky

.PHONY: hook-pre-commit
hook-pre-commit: ## Git hook: format staged C# files before commit.
	@staged=(); safe=(); skipped=(); \
	while IFS= read -r file; do staged+=("$$file"); done < <(git diff --cached --name-only --diff-filter=ACMR -- '*.cs'); \
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
hook-pre-push: hook-pre-push-message hook-format-check hook-build ## Git hook: format-check changed files + incremental build before push.

.PHONY: hook-pre-push-message
hook-pre-push-message:
	@printf '\033[36m[pre-push]\033[0m format-check (changed) + incremental build; CI runs the full clean WAE build (skip: --no-verify)...\n'

# Fast local push gate. Mirrors CI's Release posture (analyzer warnings stay warnings; nullable +
# MSBuild + error-severity rules still fail) but builds incrementally over warm outputs instead of
# the full --no-incremental rebuild CI runs. Incremental can skip up-to-date projects, so an
# error-severity analyzer hit in an untouched project is caught by CI, not here. Assumes `make
# bootstrap` already restored tools/packages (no restore/tool-restore in the hot path).
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

.PHONY: hook-build
hook-build: ## Git hook: incremental solution build over warm outputs (no restore, no clean).
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: ci-build
ci-build: format-check rebuild ci-test pack-built ## CI: check formatting, clean-build, test with coverage, then pack already-built projects.

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

.PHONY: quality-analyzers
quality-analyzers: ## Report build warnings/errors and analyzer suggestions without writing changes.
	@mkdir -p "$(ARTIFACTS_DIR)"
	@$(DOTNET) restore "$(SOLUTION)" -v:q -nologo
	@if ! $(DOTNET) build "$(SOLUTION)" $(QUALITY_BUILD_ARGS) 2>&1 | tee "$(ARTIFACTS_DIR)/quality-analyzers.log" | awk '/(^|: )(warning|error) [A-Z]+[0-9]+:/'; then \
		echo "Build failed. Full output:"; cat "$(ARTIFACTS_DIR)/quality-analyzers.log"; exit 1; \
	fi
	@Configuration="$(CONFIGURATION)" $(DOTNET) format analyzers "$(SOLUTION)" $(QUALITY_FORMAT_ARGS)

.PHONY: quality-analyzers-project
quality-analyzers-project: ## Report build warnings/errors and analyzer suggestions for PROJECT.
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make quality-analyzers-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	@mkdir -p "$(ARTIFACTS_DIR)"
	@$(DOTNET) restore "$(PROJECT)" -v:q -nologo
	@if ! $(DOTNET) build "$(PROJECT)" $(QUALITY_BUILD_ARGS) 2>&1 | tee "$(ARTIFACTS_DIR)/quality-analyzers-project.log" | awk '/(^|: )(warning|error) [A-Z]+[0-9]+:/'; then \
		echo "Build failed. Full output:"; cat "$(ARTIFACTS_DIR)/quality-analyzers-project.log"; exit 1; \
	fi
	@Configuration="$(CONFIGURATION)" $(DOTNET) format analyzers "$(PROJECT)" $(QUALITY_FORMAT_ARGS)

.PHONY: dashboards
dashboards: dashboard-jobs dashboard-messaging ## Rebuild every SPA dashboard (npm ci + vite build into wwwroot/dist).

# Internal: fail early with a clear message when Node/npm is missing.
.PHONY: _node-check
_node-check:
	@command -v $(NPM) >/dev/null 2>&1 || { echo "ERROR: '$(NPM)' not found on PATH. Node 22+ is required to build the dashboards. Install from https://nodejs.org (LTS)."; exit 1; }

.PHONY: dashboard-jobs
dashboard-jobs: _node-check ## Rebuild the Jobs dashboard SPA (npm ci + vite build into wwwroot/dist).
	cd "$(JOBS_DASHBOARD_DIR)" && $(NPM) ci && $(NPM) run build

.PHONY: dashboard-messaging
dashboard-messaging: _node-check ## Rebuild the Messaging dashboard SPA (npm ci + vite build into wwwroot/dist).
	cd "$(MESSAGING_DASHBOARD_DIR)" && $(NPM) ci && $(NPM) run build

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
ci-test: ## Run prebuilt unit tests with CI coverage output. Requires existing $(CONFIGURATION) build outputs.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --test-modules "$(UNIT_TEST_MODULES)" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER) $(CI_TEST_ARGS)

.PHONY: test-modules
test-modules: build ## Run prebuilt test DLLs via MTP --test-modules. Override TEST_MODULES if needed.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --test-modules "$(TEST_MODULES)" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

.PHONY: test-project
test-project: ## Run one test project: make test-project TEST_PROJECT=tests/.../*.csproj
	@test -n "$(TEST_PROJECT)" || (echo "TEST_PROJECT is required. Example: make test-project TEST_PROJECT=tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj" && exit 2)
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" --results-directory "$(TEST_RESULTS_DIR)" $(TEST_ARGS) $(TEST_FILTER)

.PHONY: test-project-fast
test-project-fast: ## Run one prebuilt test project without restore/build.
	@test -n "$(TEST_PROJECT)" || (echo "TEST_PROJECT is required. Example: make test-project-fast TEST_PROJECT=tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj" && exit 2)
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" --no-build --no-restore --results-directory "$(TEST_RESULTS_DIR)" $(TEST_ARGS) $(TEST_FILTER)

.PHONY: test-class
test-class: ## Run tests matching CLASS with MTP --filter-class.
	@test -n "$(CLASS)" || (echo "CLASS is required. Example: make test-class CLASS='*ClockTests'" && exit 2)
	$(MAKE) test TEST_FILTER='--filter-class "$(CLASS)"'

.PHONY: test-method
test-method: ## Run tests matching METHOD with MTP --filter-method.
	@test -n "$(METHOD)" || (echo "METHOD is required. Example: make test-method METHOD='*utc_now_should_return_correct_utc_time'" && exit 2)
	$(MAKE) test TEST_FILTER='--filter-method "$(METHOD)"'

.PHONY: test-namespace
test-namespace: ## Run tests matching NAMESPACE with MTP --filter-namespace.
	@test -n "$(NAMESPACE)" || (echo "NAMESPACE is required. Example: make test-namespace NAMESPACE=Headless.Api.Tests" && exit 2)
	$(MAKE) test TEST_FILTER='--filter-namespace "$(NAMESPACE)"'

.PHONY: test-trait
test-trait: ## Run tests matching TRAIT with MTP --filter-trait.
	@test -n "$(TRAIT)" || (echo "TRAIT is required. Example: make test-trait TRAIT='Category=Unit'" && exit 2)
	$(MAKE) test TEST_FILTER='--filter-trait "$(TRAIT)"'

.PHONY: test-query
test-query: ## Run tests matching QUERY with MTP --filter-query.
	@test -n "$(QUERY)" || (echo "QUERY is required. Example: make test-query QUERY='/Headless.Core.Tests.Unit/Tests.Abstractions/ClockTests/*'" && exit 2)
	$(MAKE) test TEST_FILTER='--filter-query "$(QUERY)"'

.PHONY: test-timeout
test-timeout: ## Run all tests with an explicit MTP timeout. SDK defaults still provide TRX and dumps.
	$(MAKE) test TEST_ARGS='$(TEST_ARGS) --timeout $(TEST_TIMEOUT)'

.PHONY: test-unit
test-unit: build ## Run every *.Tests.Unit module in parallel (honors TEST_MAX_PARALLEL).
	@mkdir -p "$(TEST_RESULTS_DIR)/unit"
	$(DOTNET) test --test-modules "$(UNIT_TEST_MODULES)" --root-directory "$(CURDIR)" --results-directory "$(TEST_RESULTS_DIR)/unit" --max-parallel-test-modules $(TEST_MAX_PARALLEL) $(TEST_ARGS) $(TEST_FILTER)

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

.PHONY: pack
pack: restore ## Pack NuGet packages with symbols.
	@mkdir -p "$(PACKAGES_DIR)"
	$(DOTNET) pack "$(SOLUTION)" --configuration "$(CONFIGURATION)" --include-symbols --output "$(PACKAGES_DIR)" $(MSBUILD_ARGS)

.PHONY: pack-built
pack-built: ## Pack already-built source projects without restore/build; used by CI.
	@mkdir -p "$(PACKAGES_DIR)"
	@for csproj in src/*/*.csproj; do \
		$(DOTNET) pack "$$csproj" --configuration "$(CONFIGURATION)" --no-restore --no-build --include-symbols --output "$(PACKAGES_DIR)"; \
	done

.PHONY: pack-sbom
pack-sbom: restore ## Pack NuGet packages with symbols and GenerateSBOM=true.
	@mkdir -p "$(PACKAGES_DIR)"
	$(DOTNET) pack "$(SOLUTION)" --configuration "$(CONFIGURATION)" --include-symbols --output "$(PACKAGES_DIR)" /p:GenerateSBOM=true $(MSBUILD_ARGS)

.PHONY: outdated
outdated: tools ## Check outdated NuGet dependencies.
	$(DOTNET) outdated "$(SOLUTION)"

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
