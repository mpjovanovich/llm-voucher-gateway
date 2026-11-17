# IDE integration plan — Milestone 11

_Work in progress developer documentation, not finished product documentation._

Expands Milestone 11 of the [pilot roadmap](pilot-roadmap.md#11-updates-for-vs-code-integration): students use a VS Code agent harness against the gateway, which means tool calling. This holds what has to change, in what order, and where it is likely to go wrong. Settled detail moves into code, tests and the API README as it lands.

**Requires the GPU box** for 11.1, 11.2 and 11.7. Everything else can proceed without one.

---

## Where things stand

What the code already does, and what that means for this milestone. Verified against the source, not the roadmap.

- **The gateway never looks inside a chat request or response.** [ChatCompletionsEndpoint.cs](../src/LlmVoucherGateway.Api/Endpoints/ChatCompletionsEndpoint.cs) hands the unread request body to [VllmInferenceClient.cs](../src/LlmVoucherGateway.Api/Inference/VllmInferenceClient.cs) and relays the response byte for byte. A request carrying `tools` or `tool` role messages, and a response carrying `tool_calls`, pass through today with no code change. Tool calling is a vLLM feature; the gateway's job is to not break it and to meter it
- **Metering does not depend on the response shape.** [StreamedUsageObserver.cs](../src/LlmVoucherGateway.Api/Inference/StreamedUsageObserver.cs) keeps the last `data:` line and reads `usage.total_tokens` from it; [BufferedUsageObserver.cs](../src/LlmVoucherGateway.Api/Inference/BufferedUsageObserver.cs) does the same for one JSON body. `--enable-force-include-usage` puts a running count on every chunk, including tool-call chunks and the `finish_reason: "tool_calls"` chunk. Expected to hold; **unproven until 11.2 captures it**
- **vLLM refuses tool calling today.** Both compose files start vLLM without `--enable-auto-tool-choice` and `--tool-call-parser`. A request that sends `tools` gets a `400` from vLLM, relayed verbatim. So the current behaviour for an agent harness is a clean, OpenAI-shaped failure on the first request, not silence
- **Only two `/v1` routes exist.** `GET /v1/models` and `POST /v1/chat/completions`. Anything else under `/v1` is an empty-bodied `404` from the framework: no endpoint matched, so [ExceptionHandler.cs](../src/LlmVoucherGateway.Api/ExceptionHandlers/ExceptionHandler.cs) never runs. A client configured for the Responses API (`/v1/responses`) sees a bare `404`
- **Consumption recording loses charges under concurrency** (roadmap 7, 12.2). Load on entry, save an absolute total. Agent harnesses fire requests in parallel: a utility request for a chat title alongside the main turn, retries, a second window. This was tolerable for a chat UI and is less so here
- **The capture set has no tool-call shape.** [Fixtures/](../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/README.md) covers plain chat only. The roadmap's own rule applies: a harness sending `tools` is a return trip to Milestone 1
- **The `--api-key` contradiction in the roadmap is stale.** The command in the roadmap no longer shows `--api-key`, and [vps-deployment.md](vps-deployment.md#vllm) decides against an upstream key. Nothing here changes that: vLLM stays unpublished on the Docker network with no key. Delete the bullet when Milestone 11 is rewritten

---

## The client

Three ways to get an OpenAI-compatible endpoint into VS Code. All of them talk to `POST /v1/chat/completions` with `stream: true`, send `tools`, and expect `tool_calls` back, so the gateway work below is the same whichever is chosen. What differs is what the student needs and how much the instructions will rot.

| Route                                                      | Needs                                                                                                                                                              | Models                                                                                     | Notes                                                                                                                                                                                                                                              |
| ---------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **VS Code built-in "Custom Endpoint" provider**            | Copilot Chat extension. Whether a GitHub sign-in or Copilot plan is needed is not stable across VS Code versions; the docs currently say Local harness sessions work without either, and that harness is being retired | Listed by hand in `chatLanguageModels.json` with `toolCalling: true`, or discovered from a `url` | The former `github.copilot.chat.customOAIModels` setting is deprecated. Has an API type switch (Chat Completions / Responses / Messages); students must pick Chat Completions. Covers chat and agent mode only, never inline completions |
| **OAI Compatible Provider for Copilot** (`johnny-zhao.oai-compatible-copilot`, MIT) | Copilot Chat extension plus this one                                                                                                                   | Listed by hand in `oaicopilot.models` (`id`, `owned_by`, `context_length`, `max_tokens`)    | Exists because the built-in provider was Insiders-only for a long time. Community-maintained; a reported problem is upstreams rejecting the tool schemas it generates from VS Code tool definitions                                      |
| **Continue / Cline / Roo Code / Kilo Code**                | Only the extension. No GitHub account, no Copilot                                                                                                                  | Config file or UI, OpenAI-compatible provider                                               | The fallback if students cannot or should not have Copilot. Different UI, same wire protocol                                                                                                                                                    |

**Decision needed (open question 1):** which route the pilot documents. Until it is made, 11.1 tries the built-in provider first because it is the one with no third-party code, and falls back in the order of the table. Foundry Toolkit is not in the table: it is a model host, not a provider for an arbitrary endpoint, and the roadmap already flags licensing.

Whatever the choice, **student instructions must pin a VS Code version and a date.** The BYOK surface has changed names three times in a year (`customOAIModels` → "OpenAI Compatible" → "Custom Endpoint"), and a setting that is experimental today (`chat.agentHost.byokModels.enabled`) may be gone or default tomorrow.

---

## Order of work

Ordered to kill unknowns first. 11.1 and 11.2 are the whole risk; everything after is plumbing.

### 11.1 Spike — does the chain work at all?

Dev GPU box, nothing committed. Hand-edit `docker-compose.llm.yml` to add `--enable-auto-tool-choice --tool-call-parser hermes` (the parser for the current `Qwen2.5` dev model), `make down && make run-llm && make run-api`, mint a voucher with a large allowance, point VS Code at `http://localhost:5131/v1` with the voucher as the key, and run one agent-mode task that has to read and edit a file.

Three things are learned here and nowhere else:

1. **The client accepts what vLLM sends.** [chat-completions.md](../docs/api_documentation/chat-completions/chat-completions.md#why-the-flag-and-what-it-costs) records that the forced `usage` on every chunk and the trailing empty-`choices` chunk are untested against any harness. If the client chokes, the fix is rewriting chunks on the way out, which undoes the no-parsing property the whole relay rests on. Find out now
2. **The model follows the tool protocol.** A 0.5B model will not, and that is fine for the spike; the question is whether the parser emits well-formed `tool_calls` at all, or whether tool calls arrive as text in `content`. A parser mismatch looks like the second
3. **What the client actually sends.** Which paths (does it call `GET /v1/models`? does it try `/v1/responses` first?), whether it sets `tool_choice`, `stream_options`, `parallel_tool_calls`, `max_tokens`, and how big the system prompt is. Read it off vLLM: start it with `--enable-log-requests` for the spike only, never in prod

**Done when:** one agent task completes through the gateway, or the failure is understood well enough to pick a different client or model. Write what was observed into this document.

### 11.2 Capture — return to Milestone 1

With the flags still on, extend the capture set by the same method as the existing one ([Fixtures/README.md](../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/README.md), `curl` shapes in [docs/api_documentation/](../docs/api_documentation/README.md)). New captures:

- Streaming chat completion with `tools`, where the model calls a tool: the `delta.tool_calls` chunks, the `finish_reason: "tool_calls"` chunk and its `usage`, the trailing chunk, `[DONE]`
- The same, non-streaming
- The follow-up request shape: a `tool` role message carrying a result, and the assistant message that repeats the `tool_calls`. This is a request capture, kept so tests can forward a realistic agent turn
- The flags-off rejection: with the flags removed, the `400` vLLM returns for a request with `tools`. This becomes an error fixture like [errors/unknown-model.json](../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/errors/unknown-model.json), and the documented symptom of a misconfigured server
- If a reasoning parser is enabled (11.3): a streamed chunk carrying `reasoning_content`, since that field is outside the OpenAI shape and a client may ignore or display it

Record the parser name and model in [api_documentation/README.md](../docs/api_documentation/README.md#environment-at-capture-time) alongside the existing environment block. Re-pull `openapi.json` only if the image tag changes; the tool-call flags do not alter the schema.

**Done when:** every fixture above is committed, linked as content into the test project, and described in the fixtures README with its final `usage` counts.

### 11.3 vLLM configuration

Apply the test in [vllm-configuration.md](../docs/configuration/vllm-configuration.md#what-is-a-literal-and-why): is it a per-machine decision?

- `--enable-auto-tool-choice` is a **literal** in both compose files, beside `--enable-force-include-usage`. The gateway is for harnesses; a server without it is misconfigured
- `--tool-call-parser` is a **required variable**, `VLLM_TOOL_CALL_PARSER`, because it is a function of `VLLM_MODEL`, which is per-machine. `${VLLM_TOOL_CALL_PARSER:?...}` so a missing one stops compose with its name. Add it to [.env.example](../.env.example) next to `VLLM_MODEL` with the pairing spelled out: `hermes` for `Qwen2.5-*` and `Qwen3` instruct models; for `Qwen3.5` the model card says `qwen3_coder` and field reports prefer `qwen3_xml`, which is exactly the kind of thing 11.1 settles. Whatever value is chosen is recorded in the capture environment
- `--reasoning-parser` (`qwen3` for the Qwen3 family) is **optional**, and compose interpolation does not do optional flags cleanly. Either a second required variable that may be empty and is interpolated as `${VLLM_REASONING_PARSER:+--reasoning-parser ${VLLM_REASONING_PARSER}}`, or a general `VLLM_EXTRA_ARGS` escape hatch. Prefer the first: it keeps `.env.example` the complete list, which is the rule the file states
- **Thinking costs tokens the student never sees.** A thinking model spends completion tokens on reasoning before every tool call, and those are billed against the voucher. vLLM can set chat-template defaults server-side (a flag that takes JSON such as `{"enable_thinking": false}`; confirm its name for `v0.29.0`). Decide in 11.1 whether thinking is worth its cost on this hardware; the default should probably be off for the pilot
- **Consider `--served-model-name`.** Every client in the table lists the model id by hand, so students will type `Qwen/Qwen3.5-9B` or whatever it is into a settings file. Change the model and every student's configuration breaks. A fixed alias such as `pilot` survives a model swap. Costs: the Makefile's `VLLM_MODEL` doubles as the `model` field in its curl helpers, the capture environment records the real name, and `GET /v1/models` would list the alias. Decide; do not drift into it

Then update [vllm-configuration.md](../docs/configuration/vllm-configuration.md): new rows in the variables table, remove the TODO, and the compose file comments that list the literals that must not change.

**Done when:** `docker compose -f docker-compose.prod.yml config --quiet` and the dev file both render the flags, a fresh clone without the new variable fails with its name, and the docs describe it.

### 11.4 Gateway

Little to write; mostly tests proving the relay and metering hold for the new shapes.

- **Tests on the new fixtures.** The fake client in [FakeInferenceClient.cs](../tests/LlmVoucherGateway.Api.IntegrationTests/Inference/FakeInferenceClient.cs) picks a fixture by the `stream` flag; it needs a way to serve the tool-call captures (by `tools` in the forwarded body, or a per-test override). Assert: a tool-call stream relays byte for byte and ends in `[DONE]`; `total_tokens` read from the `finish_reason: "tool_calls"` capture matches the fixture README; a follow-up request with a `tool` role message is forwarded unchanged; the flags-off `400` relays with status and body intact
- **OpenAI-shaped `404` for unknown `/v1` routes.** A fallback endpoint under `/v1` returning the envelope with `invalid_request_error`, so a student who picked "Responses" as the API type gets a message naming the route rather than an empty body. Same wire format rule as 6a. Admin routes keep framework behaviour
- **No request inspection.** Tempting to validate `tools` or strip `stream_options`; don't. The gateway's correctness argument is that it forwards what it was given
- **Logging.** The request line already carries method, path, status, duration and prefix. Agent sessions produce bursts of requests per task, and the thing an operator will want is per-request token counts to explain a drained voucher. Consider adding `tokens=<n>` to the request line from [ConsumptionRecorder.cs](../src/LlmVoucherGateway.Api/Consumption/ConsumptionRecorder.cs); never the body, never tool arguments, which contain student source files

**Done when:** the integration suite covers tool-call streaming, non-streaming, the follow-up turn and the flags-off error, without a GPU.

### 11.5 Metering under agent load

Two consequences of agent harnesses that the chat UI never produced.

- **Pull the concurrency fix forward from 12.2.** An atomic `UPDATE Vouchers SET Consumed = Consumed + @tokens` (or a concurrency token on `Consumed` with reload-and-retry) replaces the load-then-save in `RecordAsync`. With an agent firing a title-generation request beside the main turn, lost charges stop being rare. Add the test 12.2 asks for: two overlapping requests on one voucher, both charges land. The spendability check on entry can stay as it is; soft limits still hold
- **Allowance sizing.** Every agent turn resends the whole context: system prompt, tool definitions, every tool result so far. A ten-turn task at 15k tokens of context is 150k tokens or more. `make api-mint-voucher` defaults to 10,000, which may not survive one agent request. Set the pilot allowance from numbers observed in 11.1 and say so in the README's allowance section, where "a chat resends the whole conversation each turn" already warns about the chat case

**Done when:** the overlap test passes and the README gives a number for an agent-mode allowance.

### 11.6 Documentation

- [API README](../src/LlmVoucherGateway.Api/README.md): replace "Tool calling is not enabled" with what is supported (`tools`, `tool_choice` `auto`/`none`/named, `parallel_tool_calls`), the parser caveat (tool calls are only as good as the model), and that `/v1/responses` is not available
- **Student setup page**, one per chosen client: install, where the base URL and voucher go, the model entry to paste with `toolCalling: true` and context limits that match `VLLM_MAX_MODEL_LEN`, and what each gateway error looks like in that UI. Pin the VS Code version. Screenshots date fast; prefer the settings JSON
- [LlmVoucherGateway.Api.http](../src/LlmVoucherGateway.Api/LlmVoucherGateway.Api.http) and the Makefile: one tool-call request each, so a developer can prove the server is configured without opening VS Code
- [pilot-roadmap.md](pilot-roadmap.md): rewrite Milestone 11 as a pointer to this file plus status, and delete the stale `--api-key` bullet

### 11.7 Rehearsal and deployment

Repeat the relevant 8A steps through `docker-compose.prod.yml` on the dev box, then on the VPS after 8B:

- Streaming through Caddy with a tool-call response: chunks spread over the generation. Tool-call chunks are small and frequent; Caddy's `flush_interval -1` is already set and should not care
- Log hygiene (8A step 8) again: with tool calling, prompts carry student source code and tool arguments carry file paths and shell commands. `logs caddy`, `logs llm-server` and `$GATEWAY_DATA_DIR/logs` contain none of it. `--enable-log-requests` must not be in either compose file
- The VPS model and parser pair: `VLLM_TOOL_CALL_PARSER` on the server matches the model on the server, which is not the dev model. The first start after this milestone is the first time that pairing runs
- One real agent task from a student-like laptop over the public hostname, metered, with the voucher's `consumed` read back from `GET /admin/vouchers/{prefix}`

**Done when:** a student-configured VS Code completes an agent task against the public hostname and the charge is visible to the operator.

---

## Trouble to expect

Roughly in the order it will be met.

- **Parser mismatch looks like a dumb model.** The symptom of a wrong `--tool-call-parser` is tool calls arriving as JSON-ish text in `content`, or a `tool_calls` array with mangled arguments, not an error. Compare against the model card for the exact model and vLLM tag before blaming the model
- **The 8GB card.** A 9B model in 16-bit weights is roughly 18GB and does not fit. It needs a 4-bit quantized checkpoint (around 5–6GB), leaving perhaps 2GB of KV cache. Agent-mode prompts are commonly 10–30k tokens, and `VLLM_MAX_MODEL_LEN` has to admit the largest one while the cache still holds a few requests. This is Milestone 10's question, but tool calling forces it earlier because an agent prompt is ten times a chat prompt. Expect either a smaller model, a lower context cap, or both; and the roadmap's note that an RTX 3060 12GB is the better card stands
- **Context overflow is a vLLM `400`.** Already captured ([context-length-overflow.json](../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/errors/context-length-overflow.json)). The client's `maxInputTokens` must be set below `VLLM_MAX_MODEL_LEN` minus the output budget, or the harness will hit it mid-task and most harnesses do not recover gracefully
- **Small models loop.** A model that cannot follow the tool protocol reliably will call the same tool repeatedly or never stop. Each loop iteration is a metered request. The allowance is the only brake the gateway has; there is no per-session request cap, and adding one is a design change (what is a session?). Note it; do not build it before seeing it happen
- **Thinking tokens.** With a reasoning model and thinking on, a large share of billed tokens is invisible to the student, and `reasoning_content` is a non-OpenAI field. Decide in 11.3
- **Forced `usage` and the trailing chunk.** The known-untested compatibility risk. If the chosen client breaks on it, the options are stripping on the way out (rewrite chunks, lose the no-parse property) or dropping the server flag and reading usage from a client-requested trailing chunk (then every harness that omits `stream_options` meters as nothing). Neither is good; this is why 11.1 comes first
- **Wrong API type.** VS Code's provider offers Responses and Messages as well as Chat Completions. A student who picks the wrong one hits `/v1/responses` and gets the gateway's `404`. 11.4 makes that `404` say so
- **Model discovery.** A provider that discovers models from `GET /v1/models` gets vLLM's list, which carries no capability flags, so the model may not appear in the agent picker at all. Instructions should list the model by hand with `toolCalling: true` rather than rely on discovery
- **Lost charges, now systematic.** Covered in 11.5. If it is deferred, know that agent mode under-meters and by how much
- **Account prerequisites.** If the chosen route needs a GitHub account or a Copilot plan, that is an institutional question (student accounts, the organisation's BYOK policy) that no amount of gateway work resolves. Ask before 11.6
- **Instruction rot.** VS Code's BYOK surface has been renamed repeatedly within a year. Expect to revise the student page each term

---

## Open questions

1. **Which client route** does the pilot document: the built-in Custom Endpoint provider, the OAI Compatible extension, or a non-Copilot extension such as Continue? Decides 11.1's starting point and the whole of 11.6. The answer depends on whether students have, or may be required to have, GitHub accounts
2. **Is the model decided?** The roadmap proposes `Qwen3.5-9B`, which needs a quantized checkpoint to fit the 8GB card and whose parser name is disputed. A smaller or non-thinking model would simplify 11.3 considerably
3. **Fixed served model name** (`--served-model-name`) or the real Hugging Face id in every student's settings?
4. **Is the concurrency fix in scope** for this milestone, or accepted as under-metering for the pilot?
5. **Thinking on or off** by default, if the model has it?

---

## Files touched

| Step | Files                                                                                                                                                                                       |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 11.2 | `tests/.../Fixtures/*` (new captures, README), `docs/api_documentation/README.md`                                                                                                           |
| 11.3 | `docker-compose.llm.yml`, `docker-compose.prod.yml`, `.env.example`, `docs/configuration/vllm-configuration.md`, possibly `Makefile`                                                        |
| 11.4 | `FakeInferenceClient.cs`, new tests under `tests/.../Inference/`, a `/v1` fallback endpoint in `src/LlmVoucherGateway.Api/Endpoints/`, maybe `ConsumptionRecorder.cs` and the request log |
| 11.5 | `ConsumptionRecorder.cs`, `Voucher.cs`, a migration if a concurrency token is added, `ConsumptionRecordingTests.cs`                                                                        |
| 11.6 | `src/LlmVoucherGateway.Api/README.md`, `LlmVoucherGateway.Api.http`, `Makefile`, a new student setup page, `planning/pilot-roadmap.md`                                                      |
