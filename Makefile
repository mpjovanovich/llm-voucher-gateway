# Needed so that Make knows these are not actual files
.PHONY: up down

# GPU host (NVIDIA Container Toolkit)
up:
	docker compose up -d

# Stops the stack
down:
	docker compose down