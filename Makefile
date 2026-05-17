SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c

.DEFAULT_GOAL := help

DOTNET ?= dotnet
SOLUTION ?= headless-framework.slnx
CONFIGURATION ?= Release
ARTIFACTS_DIR ?= artifacts
PACKAGES_DIR ?= $(ARTIFACTS_DIR)/packages-results
TEST_RESULTS_DIR ?= $(ARTIFACTS_DIR)/test-results
COVERAGE_DIR ?= $(ARTIFACTS_DIR)/coverage
PROJECT ?=
TEST_PROJECT ?=
TEST_FILTER ?=
MSBUILD_ARGS ?=

.PHONY: help
help: ## Show available commands.
	@awk 'BEGIN {FS = ":.*##"; printf "\nCommands:\n"} /^[a-zA-Z0-9_.-]+:.*##/ { printf "  %-18s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)
	@printf "\nExamples:\n"
	@printf "  make build\n"
	@printf "  make test-project TEST_PROJECT=tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj\n"
	@printf "  make pack CONFIGURATION=Release\n\n"

.PHONY: bootstrap
bootstrap: tools restore ## Restore local tools and NuGet packages.

.PHONY: tools
tools: ## Restore repo-pinned .NET tools.
	$(DOTNET) tool restore

.PHONY: restore
restore: ## Restore NuGet packages.
	$(DOTNET) restore "$(SOLUTION)"

.PHONY: build
build: restore ## Build the solution.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: rebuild
rebuild: restore ## Build the solution without incremental compilation.
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore --no-incremental -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: build-project
build-project: restore ## Build one project: make build-project PROJECT=src/.../*.csproj
	@test -n "$(PROJECT)" || (echo "PROJECT is required. Example: make build-project PROJECT=src/Headless.Api/Headless.Api.csproj" && exit 2)
	$(DOTNET) build "$(PROJECT)" --configuration "$(CONFIGURATION)" --no-restore -v:q -nologo /clp:ErrorsOnly $(MSBUILD_ARGS)

.PHONY: format
format: tools ## Format C# code with CSharpier.
	$(DOTNET) csharpier format .

.PHONY: format-check
format-check: tools ## Check C# formatting without writing changes.
	$(DOTNET) csharpier check .

.PHONY: test
test: build ## Run all tests. Use TEST_FILTER='...' for dotnet test filters.
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --solution "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-build -v:q -nologo --results-directory "$(TEST_RESULTS_DIR)" $(if $(TEST_FILTER),--filter '$(TEST_FILTER)',)

.PHONY: test-project
test-project: ## Run one test project: make test-project TEST_PROJECT=tests/.../*.csproj
	@test -n "$(TEST_PROJECT)" || (echo "TEST_PROJECT is required. Example: make test-project TEST_PROJECT=tests/Headless.Api.Tests.Unit/Headless.Api.Tests.Unit.csproj" && exit 2)
	@mkdir -p "$(TEST_RESULTS_DIR)"
	$(DOTNET) test --project "$(TEST_PROJECT)" --configuration "$(CONFIGURATION)" -v:q -nologo --results-directory "$(TEST_RESULTS_DIR)" $(if $(TEST_FILTER),--filter '$(TEST_FILTER)',)

.PHONY: test-unit
test-unit: build ## Run every *.Tests.Unit project.
	@mkdir -p "$(TEST_RESULTS_DIR)/unit"
	@find tests -name '*.Tests.Unit.csproj' -print0 | while IFS= read -r -d '' project; do \
		echo "Testing $$project"; \
		$(DOTNET) test --project "$$project" --configuration "$(CONFIGURATION)" --no-build -v:q -nologo --results-directory "$(TEST_RESULTS_DIR)/unit" $(if $(TEST_FILTER),--filter '$(TEST_FILTER)',); \
	done

.PHONY: test-integration
test-integration: build ## Run every *.Tests.Integration project. Requires Docker/Testcontainers where applicable.
	@mkdir -p "$(TEST_RESULTS_DIR)/integration"
	@find tests -name '*.Tests.Integration.csproj' -print0 | while IFS= read -r -d '' project; do \
		echo "Testing $$project"; \
		$(DOTNET) test --project "$$project" --configuration "$(CONFIGURATION)" --no-build -v:q -nologo --results-directory "$(TEST_RESULTS_DIR)/integration" $(if $(TEST_FILTER),--filter '$(TEST_FILTER)',); \
	done

.PHONY: coverage
coverage: tools build ## Collect Cobertura coverage for the full test suite.
	@mkdir -p "$(COVERAGE_DIR)" "$(TEST_RESULTS_DIR)"
	$(DOTNET) coverage collect -f cobertura -o "$(COVERAGE_DIR)/coverage.xml" -- \
		$(DOTNET) test --solution "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-build -v:q -nologo --results-directory "$(TEST_RESULTS_DIR)"

.PHONY: coverage-html
coverage-html: coverage ## Generate an HTML coverage report.
	$(DOTNET) reportgenerator -reports:"$(COVERAGE_DIR)/coverage.xml" -targetdir:"$(COVERAGE_DIR)/report" -reporttypes:Html

.PHONY: coverage-open
coverage-open: coverage-html ## Generate report and open in browser.
	open "$(COVERAGE_DIR)/report/index.html"

.PHONY: pack
pack: restore ## Pack NuGet packages with symbols.
	@mkdir -p "$(PACKAGES_DIR)"
	$(DOTNET) pack "$(SOLUTION)" --configuration "$(CONFIGURATION)" --include-symbols --output "$(PACKAGES_DIR)" $(MSBUILD_ARGS)

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
