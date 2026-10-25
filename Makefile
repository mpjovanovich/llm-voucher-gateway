# Needed so that Make knows these are not actual files
.PHONY: clean down migration run-api up-llm

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

# vLLM instance
up-llm:
	docker compose -f docker-compose.llm.yml up -d

# Gateway API on http://localhost:5131, pointed at the vLLM from up-llm
run-api:
	dotnet run --project src/LlmVoucherGateway.Api --launch-profile http
