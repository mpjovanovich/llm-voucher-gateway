# Token usage in chat completions

This document covers how vLLM reports token counts, and what that forces on the gateway's metering.

**_The raw captures are the integration tests' fixtures_**. Only excerpts are included here:

[tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures](../../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/README.md).

Non-streaming is the uninteresting case: `usage` is always present, nothing has to be opted into, and `total_tokens` is final — see [non-streaming-response.json](../../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/non-streaming-response.json). Everything below is about streaming.

## The two switches

Two independent switches decide whether a streaming response carries token counts. Neither causes streaming — that is `"stream": true` in the request body.

| Switches                                         | Resulting `usage`                   | Capture                                                                                                                                 |
| ------------------------------------------------ | ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| server `--enable-force-include-usage`            | on every chunk, as a running count  | [usage-requested-server.sse](../../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/streaming-response-usage-requested-server.sse) |
| client `stream_options: {"include_usage": true}` | one trailing chunk, empty `choices` | [usage-requested-client.sse](../../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/streaming-response-usage-requested-client.sse) |
| neither                                          | absent entirely                     | [usage-not-requested.sse](../../tests/LlmVoucherGateway.Api.IntegrationTests/Fixtures/streaming-response-usage-not-requested.sse)       |

**The first row is what the gateway sees.** [docker-compose.llm.yml](../../docker-compose.llm.yml)
sets the flag, and harnesses send no `stream_options`.

## Development testing

With vLLM up (`make run-llm`), the request that produces the gateway's real shape sends no
usage option at all:

```bash
curl -N http://localhost:8000/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{
      "model": "Qwen/Qwen2.5-0.5B-Instruct",
      "messages": [{"role": "user", "content": "Say hello in one sentence."}],
      "stream": true
    }'
```

Add `"stream_options": {"include_usage": true}` for the client-opt-in shape. The two flag-off shapes cannot be reproduced while the compose file sets the flag — reproducing them means removing it and restarting, which is why they are captured rather than re-derived. Through the gateway (`:5131` instead of `:8000`) the endpoint needs a voucher; [../README.md](../README.md) has the minting call.

## Why the flag, and what it costs

**`usage` arrives without the client asking.** The flag is read from server config in `should_include_usage()` (`vllm/entrypoints/serve/utils/api_utils.py`) and short-circuits ahead of any client option. This is the whole reason metering works: a harness that never sends `stream_options` still gets counted.

**It also puts a running count on every chunk** — not obvious from the flag's name. Forcing returns `(include_usage=True, include_continuous_usage=True)`, and that second value is what makes `completion_tokens` climb by one per chunk:

```
# first (role) chunk
…"delta":{"role":"assistant","content":""},"finish_reason":null}],"usage":{"prompt_tokens":35,"total_tokens":35,"completion_tokens":0,…}

# last content chunk — the final total is already here
…"delta":{"content":""},"finish_reason":"stop",…}],"usage":{"prompt_tokens":35,"total_tokens":60,"completion_tokens":25,…}
```

Consequences for consumption recording:

- **Read `total_tokens`; don't compute it.** It already equals `prompt_tokens + completion_tokens` (35 + 25 = 60 above). Summing `prompt_tokens` with `total_tokens` double-bills the prompt.
- **No need to parse chunks as they stream past.** Every chunk holds a running total, so the gateway relays bytes untouched while keeping a copy of the most recent `data:` line, then parses that one line after the stream ends. One JSON parse per request, not per token.
- **A client disconnect does not lose the request.** The last chunk that got out carries a valid running count, so a dropped connection undercounts by only the tokens after it.
- **The trailing `choices: []` chunk is still there**, repeating the same totals plus `system_fingerprint`. It is no longer load-bearing for metering — convenient, since it is the chunk a disconnect is most likely to lose.

**Compatibility risk, untested.** The flag sends `usage` to clients that never requested it, plus a trailing empty-`choices` chunk. Both are spec-shaped and a conforming client should ignore them, but this has not been verified against OpenCode or Aider. If one chokes, the fix is stripping the field on the way out — which means rewriting chunks after all, undoing the no-parsing property above.

## Shapes to keep in mind if the flag ever comes off

- **Client opt-in** puts `usage` only in a trailing chunk _after_ the `finish_reason: "stop"` chunk, so a consumer must read through to `[DONE]` rather than stopping at `finish_reason`. `system_fingerprint` moves onto that trailing chunk too.
- **Neither switch** gives no counts at all, and there is no separate terminal event: `finish_reason` and `system_fingerprint` both land on the last content chunk.
- `StreamOptions` in [openapi.json](../openapi.json) carries a second field, `continuous_usage_stats` (default `false`), which is the client-side equivalent of the per-chunk running count the server flag forces. A client could therefore ask for the every-chunk shape itself — the shape is not evidence of which side requested it.

Minor: `completion_tokens_details` (always `null` here) appears on the continuous chunks and is absent from the trailing one.
