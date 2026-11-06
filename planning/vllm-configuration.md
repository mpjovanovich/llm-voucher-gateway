# Configurable vLLM launch

The model, GPU memory budget, image tag, published port, GPU count, and the WSL2 pinned-memory switch are literals in [docker-compose.llm.yml](../docker-compose.llm.yml). They differ per machine. The gateway forwards the model id the client sends; vLLM only serves the model it was started with, so the launch flags and every caller that assumes the published port or that model id move together.

Defaults stay the current literals. `make run-llm` with no `.env` behaves as it does today.

---

## Variables

| Variable | Default | Where it lands |
| --- | --- | --- |
| `VLLM_IMAGE` | `vllm/vllm-openai:v0.29.0` | Image reference |
| `VLLM_MODEL` | `Qwen/Qwen2.5-0.5B-Instruct` | `--model` |
| `VLLM_GPU_MEMORY_UTILIZATION` | `0.85` | `--gpu-memory-utilization` |
| `VLLM_GPU_COUNT` | `1` | `deploy.resources.reservations.devices[].count` |
| `VLLM_PORT` | `8000` | Host side of `ports`. The process inside the container keeps listening on 8000. |
| `VLLM_WSL2_ENABLE_PIN_MEMORY` | `1` | Container environment variable of the same name. `1` on the WSL2 dev box, `0` on a Linux host. |

`--enable-force-include-usage` stays a literal. Metering depends on it.

`--max-model-len` and the tool-calling flags (`--enable-auto-tool-choice`, `--tool-call-parser`) are settled in milestones 10 and 12. Add them the same way when those milestones land.

---

## How a value gets in

Two mechanisms, and a given variable uses one of them.

**Compose interpolation.** `${VLLM_MODEL:-Qwen/Qwen2.5-0.5B-Instruct}` is replaced in the compose file from the shell, then from a `.env` file in the directory `docker compose` is run from (the repo root, via the Makefile). The running container sees the substituted text. vLLM does not see a variable named `VLLM_MODEL`. Confirm with `docker compose -f docker-compose.llm.yml config`.

**Process environment.** A program reads this itself at startup. `VLLM_WSL2_ENABLE_PIN_MEMORY` is one: the compose `environment:` block sets it, and the value written there can still be interpolated. The gateway's upstream address is another: ASP.NET binds `Inference__BaseUrl` to `Inference:BaseUrl` in [InferenceOptions.cs](../src/LlmVoucherGateway.Api/Inference/InferenceOptions.cs). `dotnet run` and a containerized API both honor that variable. Neither reads a `.env` file.

Shell variables win over `.env`. Values in `.env` are unquoted.

---

## Files to change

**[docker-compose.llm.yml](../docker-compose.llm.yml).** Replace the six literals with `${NAME:-default}`. `VLLM_WSL2_ENABLE_PIN_MEMORY` is interpolated inside `environment:`; the model, memory utilization, image, port, and GPU count are interpolated into `image`, `ports`, `command`, and `deploy`.

**[Makefile](../Makefile).** `api-check-completion-nostream` and `api-check-completion-streaming` send `Qwen/Qwen2.5-0.5B-Instruct`. `llm-check-life` calls `localhost:8000`. Add `-include .env` and `export` at the top so those targets see the same values Compose does, and substitute `$(VLLM_MODEL)` and `$(VLLM_PORT)` with the same defaults as the compose file. `run-llm` needs no new flags; Compose loads `.env` on its own.

**[appsettings.Development.json](../src/LlmVoucherGateway.Api/appsettings.Development.json).** `Inference:BaseUrl` is an ngrok address. The committed Development value becomes `http://localhost:8000`, matching the default published port. A tunnel or a non-default `VLLM_PORT` is an override in the shell, not a committed URL.

## File to add

**`.env.example`** at the repo root. One line per variable, set to the defaults, with a comment that `.env` is the local copy and is already gitignored ([.gitignore](../.gitignore) matches `.env` and not `.env.example`).

## Files that stay as they are

**[docker-compose.yml](../docker-compose.yml).** The gateway service is not in this file yet. The production section below is the environment entry to add when that service lands.

**[appsettings.json](../src/LlmVoucherGateway.Api/appsettings.json).** `Inference:BaseUrl` stays empty. Production supplies it through the process environment, so a missing variable fails startup instead of silently targeting a developer machine.

**[app.py](../src/LlmVoucherGateway.Web/app.py).** The model list comes from `GET /v1/models`. `GATEWAY_BASE_URL` already defaults to `http://localhost:5131/v1`.

**Tests, [Fixtures/](../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/), and [api_documentation/](../api_documentation/).** Those record one capture session against one model and one image tag. They are not live configuration.

---

## Local development

vLLM runs in a container. The gateway and the web UI run on the host (`make run-api`, `make run-web`).

1. Copy `.env.example` to `.env` and edit the values for this machine.
2. `make run-llm`. Compose reads `.env` from the repo root and starts vLLM with those flags. The published address is `http://localhost:$(VLLM_PORT)`.
3. Start the gateway with `make run-api`. It reads [appsettings.Development.json](../src/LlmVoucherGateway.Api/appsettings.Development.json), then the shell. With the default port, `http://localhost:8000` is enough. When `VLLM_PORT` is something else:

   ```
   Inference__BaseUrl=http://localhost:8001 make run-api
   ```

   [launchSettings.json](../src/LlmVoucherGateway.Api/Properties/launchSettings.json) sets `ASPNETCORE_ENVIRONMENT=Development` and does not set `Inference__BaseUrl`, so the shell value is the one that applies.
4. `make run-web` needs nothing for the model. Set `GATEWAY_BASE_URL` only when the gateway is not on port 5131. Streamlit does not read `.env`; export the variable or put it on the `make` line.
5. `make llm-check-life` and the `api-check-completion-*` targets pick up `.env` through the Makefile include, so the curl model and port match the server that is running.

## Production

The gateway runs in a container on the same Compose network as `llm-server`. The same `.env` (or the same variables exported in the host shell) feeds Compose interpolation. GPU count, memory utilization, the model, and `VLLM_WSL2_ENABLE_PIN_MEMORY=0` live in that machine's `.env`, not in git.

The gateway reaches vLLM at `http://llm-server:8000`: the service name plus the port the process listens on inside its container. `VLLM_PORT` only changes the port published on the host, so it does not belong in this address. Set the process environment on the gateway service when it is added to [docker-compose.yml](../docker-compose.yml):

```yaml
environment:
  Inference__BaseUrl: http://llm-server:8000
  ASPNETCORE_ENVIRONMENT: Production
```

ASP.NET reads `Inference__BaseUrl` from the container environment and binds it over the empty value in [appsettings.json](../src/LlmVoucherGateway.Api/appsettings.json). Publishing vLLM's port is for operators curling the container from the host. The gateway does not use that mapping.
