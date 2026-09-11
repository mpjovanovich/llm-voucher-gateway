# Needed so that Make knows these are not actual files
.PHONY: clean down up-llm

# Removes bin and obj folders from all projects
clean:
	find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

# Stops the stack
down:
	docker compose -f docker-compose.llm.yml down

# vLLM instance
up-llm:
	docker compose -f docker-compose.llm.yml up -d
