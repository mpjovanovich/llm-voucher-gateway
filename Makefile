# Needed so that Make knows these are not actual files
.PHONY: clean down migration
.PHONY: run-api run-llm run-web
.PHONY: api-check-completion api-mint-voucher llm-check-life

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
# START/STOP/BUILD SCRIPTS
# ########################################
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

# Gateway API on http://localhost:5131, pointed at the vLLM from up-llm
run-api:
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

# Requires a voucher to be minted first.
# make api-check-completion key=<voucher-key>
api-check-completion:
	curl -sS -w '\n' http://localhost:5131/v1/chat/completions \
		-H "Authorization: Bearer $(key)" \
		-H "Content-Type: application/json" \
		-d '{"model":"Qwen/Qwen2.5-0.5B-Instruct","messages":[{"role":"user","content":"Say hello in one sentence."}]}'

count ?= 1
allowance ?= 10000
days ?= 30

# make api-mint-voucher (uses defaults above)
# make api-mint-voucher count=1 allowance=10000 days=30
api-mint-voucher:
	curl -sS -w '\n' http://localhost:5131/admin/vouchers \
		-H "Authorization: Bearer dev-admin-key" \
		-H "Content-Type: application/json" \
		-d '{"section":"CSCI-101","count":$(count),"allowance":$(allowance),"expiresAt":"$(shell date -u -d "+$(days) days" +%Y-%m-%dT23:59:59Z)"}'

# Hits the vLLM container directly, bypassing the gateway
llm-check-life:
	curl -sS -w '\n' http://localhost:8000/v1/models
