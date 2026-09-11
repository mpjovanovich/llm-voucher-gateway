# Streaming response — without `stream_options.include_usage`

## Request

```
curl -N http://localhost:8000/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{
      "model": "Qwen/Qwen2.5-0.5B-Instruct",
      "messages": [{"role": "user", "content": "Say hello in one sentence."}],
      "stream": true
    }'
```

## Key finding

`finish_reason` and `system_fingerprint` land **on the same, final content chunk** —
there is no separate terminal event. `usage` is **absent entirely** on this request
shape; it only appears when `stream_options.include_usage` is set (see
[streaming-response-with-usage.md](streaming-response-with-usage.md)).

## Raw SSE stream

```
data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"role":"assistant","content":""},"logprobs":null,"finish_reason":null}],"prompt_token_ids":null,"prompt_text":null}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"Hello"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"!"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" It"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"'s"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" a"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" pleasure"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" to"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" meet"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":" you"},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":"."},"logprobs":null,"finish_reason":null,"token_ids":null}]}

data: {"id":"chatcmpl-8e9ee5c90e3156e5","object":"chat.completion.chunk","created":1789154459,"model":"Qwen/Qwen2.5-0.5B-Instruct","choices":[{"index":0,"delta":{"content":""},"logprobs":null,"finish_reason":"stop","stop_reason":null,"token_ids":null}],"system_fingerprint":"vllm-0.29.0-13ff2e7e"}

data: [DONE]
```
