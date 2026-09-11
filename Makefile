# Needed so that Make knows these are not actual files
.PHONY: up-llm down

# vLLM instance
up-llm:
	docker compose -f docker-compose.llm.yml up -d

# Stops the stack
down:
	docker compose -f docker-compose.llm.yml down