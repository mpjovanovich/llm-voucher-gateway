# Streaming response — with `--enable-force-include-usage` (server-forced)

**This is the shape the gateway actually sees.** The dev compose file sets
`--enable-force-include-usage`, so this is what a streaming request returns even
when the client asks for nothing (see [docker-compose.llm.yml](../../docker-compose.llm.yml)).

## Request

Note what is *not* here: no `stream_options`. This is the shape a student harness
sends.

```
curl -N http://localhost:8000/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{
      "model": "Qwen/Qwen2.5-0.5B-Instruct",
      "messages": [{"role": "user", "content": "Say hello in one sentence."}],
      "stream": true
    }'
```

## Key finding — feeds Milestone 7

The server flag does two things, and the second one was not obvious from the flag's
name.

**1. `usage` appears without the client asking.** This closes the metering problem:
harnesses won't send `stream_options`, and now they don't have to. The flag is read
from server config in `should_include_usage()`
(`vllm/entrypoints/serve/utils/api_utils.py`), which short-circuits ahead of any
client option.

**2. Every chunk carries a running count, not just the last one.** Forcing returns
`(include_usage=True, include_continuous_usage=True)` — that second value is what
puts `usage` on each content chunk, with `completion_tokens` incrementing by one per
chunk. The first (role) chunk reports `completion_tokens: 0`; the `finish_reason:
"stop"` chunk already reports the final total.

**Consequences for consumption recording:**

- **Read `total_tokens`, don't compute it.** It already equals `prompt_tokens +
  completion_tokens` (here: 35 + 25 = 60). Summing `prompt_tokens` and `total_tokens`
  double-bills the prompt.
- **No need to parse chunks as they stream past.** Because every chunk holds a
  running total, the gateway can relay bytes untouched while keeping a copy of the
  most recent `data:` line, then parse that one line after the stream ends. One JSON
  parse per request instead of one per token.
- **A client disconnect no longer loses the request.** The last chunk that got out
  carries a valid running count, so the overshoot the plan accepted as
  "undercounts that request's output tokens" mostly goes away.
- **The trailing `choices: []` chunk is still there**, carrying the same totals as the
  `finish_reason` chunk plus `system_fingerprint`. It is no longer load-bearing for
  metering — convenient, since it is the chunk a disconnect is most likely to lose.

**Compatibility risk, untested:** this sends `usage` to clients that never requested
it, plus a trailing empty-`choices` chunk. Both are spec-shaped and should be ignored
by a conforming client, but this has not been verified against OpenCode or Aider. If
one of them chokes, the fix is stripping the field on the way out — which would mean
rewriting chunks after all, and would undo the no-parsing property above. Worth
checking during Milestone 6's harness run.

Minor: `completion_tokens_details` (always `null` here) appears on the continuous
chunks and is absent from the trailing one.

## Compare

- [streaming-response.md](streaming-response.md) — flag off, no `usage` at all
- [streaming-response-with-usage.md](streaming-response-with-usage.md) — flag off,
  client opts in; usage only in the trailing chunk

## Raw SSE stream

```
data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"role":"assistant","content":""},"logprobs":null,"finish_reason":null}],"usage":{"prompt_tokens":35,"total_tokens":35,"completion_tokens":0,"completion_tokens_details":null},"prompt_token_ids":null,"prompt_text":null}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"Hello"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":36,"completion_tokens":1,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"!"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":37,"completion_tokens":2,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" It"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":38,"completion_tokens":3,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"'s"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":39,"completion_tokens":4,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" always"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":40,"completion_tokens":5,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" nice"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":41,"completion_tokens":6,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" to"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":42,"completion_tokens":7,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" say"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":43,"completion_tokens":8,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" hello"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":44,"completion_tokens":9,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" to"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":45,"completion_tokens":10,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" someone"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":46,"completion_tokens":11,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":","},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":47,"completion_tokens":12,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" whether"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":48,"completion_tokens":13,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" it"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":49,"completion_tokens":14,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"'s"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":50,"completion_tokens":15,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" a"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":51,"completion_tokens":16,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" friend"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":52,"completion_tokens":17,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":","},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":53,"completion_tokens":18,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" family"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":54,"completion_tokens":19,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" member"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":55,"completion_tokens":20,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":","},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":56,"completion_tokens":21,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" or"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":57,"completion_tokens":22,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" colleague"},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":58,"completion_tokens":23,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"."},"logprobs":null,"finish_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":59,"completion_tokens":24,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":""},"logprobs":null,"finish_reason":"stop","stop_reason":null,"token_ids":null}],"usage":{"prompt_tokens":35,"total_tokens":60,"completion_tokens":25,"completion_tokens_details":null}}

data: {"id":"chatcmpl-9c0e5aba6336a201","object":"chat.completion.chunk","created":1793243310,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[],"usage":{"prompt_tokens":35,"total_tokens":60,"completion_tokens":25},"system_fingerprint":"vllm-0.29.0-13ff2e7e"}

data: [DONE]
```
