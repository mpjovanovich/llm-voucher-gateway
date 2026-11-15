# Needed so that Make knows these are not actual files
.PHONY: clean down migration
.PHONY: require-model require-admin-key run-api run-llm run-web
.PHONY: api-check-completion-nostream api-check-completion-streaming api-mint-voucher
.PHONY: llm-check-life
.PHONY: deploy run-prod down-prod ps-prod logs-prod

# ########################################
# LLM CONFIG
# ########################################
# .env in this directory is the only place per-machine values are set; copy
# .env.example. Both docker compose and this file read it, so the curl helpers
# below target the same model and port as the running server.
-include .env

# VLLM_PORT is the one value with a default here, because 8000 is the port the
# vLLM process listens on inside its container rather than a per-machine
# opinion. VLLM_MODEL deliberately gets none: the compose files require it, and
# a default here would hide a missing .env behind someone else's model.
VLLM_PORT ?= 8000

# Where the Makefile's curl helpers send requests. The dev default is the
# gateway's Kestrel port; the server sets GATEWAY_URL=http://localhost:8080 in
# .env, which points the same targets at the production container.
GATEWAY_URL ?= http://localhost:5131

# Named rather than a bare `export`, which would also put a secret from .env
# (ADMIN_KEY) into the environment of every recipe. Exporting these two is what
# keeps make and compose agreeing: compose prefers a shell value over .env, so
# handing it make's value means one file decides for both.
export VLLM_MODEL VLLM_PORT

# Production is the same stack with a different compose file. These targets are
# thin wrappers that add the -f flag and nothing else, so .env stays the only
# place production values are set.
COMPOSE_PROD := docker compose -f docker-compose.prod.yml

# ########################################
# VENV CONFIG
# ########################################
WEB_DIR := src/LlmVoucherGateway.Web
VENV := $(WEB_DIR)/.venv

# Creates the venv and installs requirements only when requirements.txt is newer
$(VENV)/.installed: $(WEB_DIR)/requirements.txt
	python3 -m venv $(VENV)
	$(VENV)/bin/pip install -q -r $(WEB_DIR)/requirements.txt
	touch $@

# ########################################
# DEVELOPMENT: START/STOP/BUILD SCRIPTS
# ########################################
# These are not to be used in prod. They're for bringing up/down the components
# of the application while devs work on it.

# Removes bin and obj folders from all projects
clean:
	find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

# Stops the stack
down:
	docker compose -f docker-compose.llm.yml down

# Adds an EF Core migration: make migration name=AddSomething
# Startup applies whatever migrations exist, so there is no separate update command.
migration:
	ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add $(name) \
		--project src/LlmVoucherGateway.Api --output-dir Persistence/Migrations

# ADMIN_KEY is injected only when .env sets it. Left empty it would override
# appsettings.Development.json's dev key with nothing, breaking admin auth on a
# box that never needed a key of its own; this way .env wins when it has an
# opinion and the Development file remains the single dev default.
run-api:
	Inference__BaseUrl=http://localhost:$(VLLM_PORT) \
		$(if $(ADMIN_KEY),Admin__Keys__0=$(ADMIN_KEY),) \
		dotnet run --project src/LlmVoucherGateway.Api --launch-profile http

run-llm:
	docker compose -f docker-compose.llm.yml up -d

run-web: $(VENV)/.installed
	$(VENV)/bin/streamlit run $(WEB_DIR)/app.py

# ########################################
# CONVENIENCE SCRIPTS
# ########################################
# These are mostly work-in-progress scripts to see what's running.
# We don't have a lot of dev infrastructure in place.
count ?= 1
allowance ?= 10000
days ?= 30

# Fails with a message instead of sending a request with an empty model id
require-model:
	@test -n "$(VLLM_MODEL)" || \
		{ echo "VLLM_MODEL is not set. Copy .env.example to .env."; exit 1; }

# Fails with a message instead of sending an empty bearer token
require-admin-key:
	@test -n "$(ADMIN_KEY)" || \
		{ echo "ADMIN_KEY is not set. Set it in .env."; exit 1; }

# Requires a voucher to be minted first.
# make api-check-completion-nostream key=<voucher-key>
api-check-completion-nostream: require-model
	curl -sS -w '\n' $(GATEWAY_URL)/v1/chat/completions \
		-H "Authorization: Bearer $(key)" \
		-H "Content-Type: application/json" \
		-d '{"model":"$(VLLM_MODEL)","messages":[{"role":"user","content":"Say hello in one sentence."}]}'

# Requires a voucher to be minted first.
# make api-check-completion key=<voucher-key>
api-check-completion-streaming: require-model
	curl -sS -w '\n' $(GATEWAY_URL)/v1/chat/completions \
		-H "Authorization: Bearer $(key)" \
		-H "Content-Type: application/json" \
		-d '{"model":"$(VLLM_MODEL)","messages":[{"role":"user","content":"Say hello in one sentence."}],"stream":true}'

# make api-mint-voucher (uses defaults above)
# make api-mint-voucher count=1 allowance=10000 days=30
api-mint-voucher: require-admin-key
	curl -sS -w '\n' $(GATEWAY_URL)/admin/vouchers \
		-H "Authorization: Bearer $(ADMIN_KEY)" \
		-H "Content-Type: application/json" \
		-d '{"section":"CSCI-101","count":$(count),"allowance":$(allowance),"expiresAt":"$(shell date -u -d "+$(days) days" +%Y-%m-%dT23:59:59Z)"}'

# Hits the vLLM container directly, bypassing the gateway
llm-check-life:
	curl -sS -w '\n' http://localhost:$(VLLM_PORT)/v1/models

# ########################################
# PRODUCTION
# ########################################
# Run from the checkout on the server. See planning/vps-deployment.md.

# How every change ships: images are built on the server, there is no registry
deploy:
	git pull && $(COMPOSE_PROD) build

run-prod:
	$(COMPOSE_PROD) up -d

down-prod:
	$(COMPOSE_PROD) down

ps-prod:
	$(COMPOSE_PROD) ps

# make logs-prod service=gateway  (omit service for all of them)
logs-prod:
	$(COMPOSE_PROD) logs -f --since 1h $(service)
