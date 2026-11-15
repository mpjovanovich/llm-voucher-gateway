# vLLM and gateway configuration

How per-machine values reach vLLM, the gateway and the curl helpers, and why each one
lives where it does. This is a record of what is in place, not a plan.

The gateway forwards the model id the client sends, and vLLM only serves the model it
was started with, so the launch flags and every caller that assumes that model id move
together.

---

## The rule

**`.env` in the repo root is the only place a machine sets anything.** [.env.example](../.env.example)
is the list of what a machine has to decide; `cp .env.example .env` and edit.

A value with no sensible default is **required**, not defaulted: compose stops with a
message naming the variable. Nothing downstream carries a second copy of a default, so
there is one place to look and no way for two layers to disagree. A fresh clone with no
`.env` fails rather than quietly starting someone else's model.

---

## Variables

| Variable                      | Required                    | Where it lands                                                                                                       |
| ----------------------------- | --------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `VLLM_MODEL`                  | yes                         | `--model`, and the `model` field in the Makefile's curl helpers                                                      |
| `VLLM_GPU_MEMORY_UTILIZATION` | yes                         | `--gpu-memory-utilization`                                                                                           |
| `VLLM_PORT`                   | no, `8000`                  | Host side of `ports`, and the Makefile's `llm-check-life` and `run-api`. Dev only; production publishes no vLLM port |
| `VLLM_WSL2_ENABLE_PIN_MEMORY` | no, `0`                     | Container environment variable of the same name. `1` on a WSL2 box, where vLLM keeps pinned memory opt-in            |
| `ADMIN_KEY`                   | yes, production             | `Admin__Keys__0` on the gateway service                                                                              |
| `GATEWAY_DATA_DIR`            | no, `/srv/llm-gateway/data` | Host side of the gateway's `/data` mount                                                                             |

The two that have defaults have them because neither is an opinion: `8000` is the port
the vLLM process listens on inside its own container, and a native Linux host is the
case where the pinned-memory switch means nothing.

## What is a literal, and why

- **The image tag.** `vllm/vllm-openai:v0.29.0`, written out in both compose files. It is
  the pin that the captured [api_documentation/](../docs/api_documentation/) describes and
  the only drift control there is; a per-machine tag could leave the captures describing
  something other than what is running, with nothing to reveal it. Bumping it is a commit
- **`--enable-force-include-usage`.** Streaming metering depends on it. Without it,
  streamed requests meter as nothing
- **One GPU.** `count: 1`. More would also need `--tensor-parallel-size`, which is settled
  in Milestone 10; the two belong in the same change
- **In-container addresses.** `Inference__BaseUrl=http://llm-server:8000` and
  `GATEWAY_BASE_URL=http://gateway:8080/v1` in [docker-compose.prod.yml](../docker-compose.prod.yml)
  are a service name plus the port that process listens on inside its container. Neither
  varies per machine, and `VLLM_PORT` deliberately has no effect on them

TODO:

`--max-model-len` and the tool-calling flags (`--enable-auto-tool-choice`,
`--tool-call-parser`) are settled in Milestones 10 and 11. Each arrives as a required
variable or a literal by the same test: is it a per-machine decision?

---

## How a value gets in

Two mechanisms, and a given variable uses one of them.

**Compose interpolation.** `${VLLM_MODEL:?...}` is replaced in the compose file from the
shell, then from `.env` in the directory `docker compose` runs from (the repo root, via
the Makefile). The running container sees the substituted text; vLLM does not see a
variable named `VLLM_MODEL`. Confirm with `docker compose -f docker-compose.llm.yml config`.

**Process environment.** A program reads this itself at startup.
`VLLM_WSL2_ENABLE_PIN_MEMORY` is one: the compose `environment:` block sets it, and the
value written there can still be interpolated. The gateway's upstream address is another:
ASP.NET binds `Inference__BaseUrl` to `Inference:BaseUrl` in
[InferenceOptions.cs](../src/LlmVoucherGateway.Api/Inference/InferenceOptions.cs). `dotnet run`
and a containerized API both honor that variable. Neither reads a `.env` file.

**Precedence is not uniform, which is why `.env` is the only place to set things.**
`docker compose` prefers a shell variable over `.env`. `make` does the opposite:
`-include .env` is a makefile assignment, which overrides the environment. Measured, with
`.env` holding `8000`:

```
VLLM_PORT=9999 docker compose ... config   → published: "9999"
VLLM_PORT=9999 make llm-check-life         → curl http://localhost:8000/...
```

The [Makefile](../Makefile)'s `export VLLM_MODEL VLLM_PORT` is what keeps this from
mattering: anything reached through `make` hands compose make's values, so both sides
read one file. The export names its variables rather than exporting everything, so a
secret in `.env` does not land in the environment of every recipe.

---

## Local development

vLLM runs in a container. The gateway and the web UI run on the host.

1. `cp .env.example .env` and edit the values for this machine
2. `make run-llm`. Compose reads `.env` and starts vLLM with those flags. The published
   address is `http://localhost:$(VLLM_PORT)`
3. `make run-api`. The recipe sets `Inference__BaseUrl=http://localhost:$(VLLM_PORT)`, so
   the gateway follows `.env` and cannot end up dialing a port vLLM was not published on.
   The environment variable overrides
   [appsettings.Development.json](../src/LlmVoucherGateway.Api/appsettings.Development.json),
   which keeps `http://localhost:8000` as the value for launching from an IDE instead —
   that path cannot read `.env`
4. `make run-web` needs nothing for the model; the list comes from `GET /v1/models`. Set
   `GATEWAY_BASE_URL` only when the gateway is not on port 5131. Streamlit does not read
   `.env`
5. `make llm-check-life` and the `api-check-completion-*` targets read `.env` through the
   Makefile, so the curl model and port match the server that is running. With no `.env`,
   they stop with a message instead of sending an empty model id

## Production

See [vps-deployment.md](vps-deployment.md). The same `.env` mechanism feeds compose
interpolation; the server's `.env` additionally sets `ADMIN_KEY` and
`VLLM_WSL2_ENABLE_PIN_MEMORY=0`, and holds the model and memory utilization for that
hardware. Lifecycle is `make deploy`, `make run-prod`, `make down-prod`, `make ps-prod`,
`make logs-prod service=<name>` — thin wrappers that add the compose file and nothing
else, so `make` never becomes a second place configuration is set.

[appsettings.json](../src/LlmVoucherGateway.Api/appsettings.json) keeps `Inference:BaseUrl`,
`Admin:Keys` and `Database:Path` empty. With `ValidateOnStart`, a missing variable fails
startup instead of silently targeting a developer machine.
