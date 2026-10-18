# Every Weaver step of this repository, runnable one at a time or all at once with `make weaver`.
# The tools come from mise.toml (weaver 0.28.0, dotnet 10.0.401, node 24.21.0): run `mise install` once.

SHELL := /bin/bash
SLN := Qyl.Telemetry.SemanticConventions.slnx
DOCS_GENERATOR := tools/Qyl.Telemetry.SemanticConventions.Analyzers.DocsGenerator
PIPELINE_TESTS := tests/Qyl.Telemetry.SemanticConventions.Pipeline.Tests

.PHONY: weaver core generate reference check-generated build docs docs-check typescript policies pins test check help

help: ## List the targets
	@grep -E '^[a-z-]+:.*## ' $(MAKEFILE_LIST) | sed 's/:.*## /\t/'

weaver: generate check-generated build docs typescript policies test ## All Weaver steps in order: generate, drift check, build, analyzer docs, TypeScript tests, live-check policies, tests

core: ## Materialise the filtered upstream core registry the qyl manifest depends on
	./scripts/fetch-core.sh

generate: ## Regenerate C#, TypeScript and the detailed Markdown references from registry/
	./scripts/generate.sh

reference: core ## Render the complete semantic references with Weaver's .j2 templates
	./scripts/generate-reference.sh

check-generated: ## Fail if regenerating from registry/ differs from what is committed
	./scripts/check-generated.sh

build: ## Restore and build the solution (Release)
	dotnet restore $(SLN)
	dotnet build $(SLN) --no-restore --configuration Release

docs: build ## Regenerate the analyzer docs, rule pages, migration catalog and editorconfigs
	dotnet run --project $(DOCS_GENERATOR) --configuration Release --no-build -- generate

docs-check: build ## Fail if the generated analyzer docs are stale
	dotnet run --project $(DOCS_GENERATOR) --configuration Release --no-build -- check

typescript: ## Run the TypeScript package tests
	npm test --prefix typescript

policies: ## Check the live-check advice policy set against the registry
	./scripts/check-live-check-policies.sh

pins: ## Report upstream refs this repository pins that differ from upstream
	uv run scripts/check-pin-freshness.py

test: build ## Script unit tests and the pipeline tests
	uv run --no-project --with pyyaml python3 -m unittest discover --start-directory tests/scripts --pattern 'test_*.py'
	dotnet run --project $(PIPELINE_TESTS)/ --configuration Release --no-build

check: ## The former CI gate, in order
	mise run check
