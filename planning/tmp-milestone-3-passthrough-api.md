# Milestone 3 build plan — Pass-through API

Scope: everything the API project needs to satisfy [Milestone 3](pilot-roadmap.md) and
_nothing else_. Milestones 4–9 get referenced only where a decision now would be
expensive to reverse later; anything else is listed under
[Deliberately deferred](#deliberately-deferred).

---

## Guiding constraints

**Prefer the minimal implementation.** At every step, the smallest thing that makes the
milestone's "done when" true is the correct thing. This is a pilot with one endpoint and
no persistence yet — a folder is preferable to a project, a static extension method is
preferable to an interface + registrar, and a pass-through is preferable to a DTO
round-trip. Structure earns its place by being needed, not by being conventional.

**Minimal APIs, not controllers.** No MVC, no `AddControllers`, no `[ApiController]`.

**Idiomatic .NET.** File-scoped namespaces, explicit types over `var` where the type
isn't apparent (per [.editorconfig](../.editorconfig)), `sealed` by default, options
pattern for configuration, `IHttpClientFactory` for outbound HTTP.

**Follow the shape of well-documented reference projects** (Evently and friends) —
`src/`-rooted projects, an `Endpoints/` folder of thin route registrations, an adapter
isolated behind an interface, configuration bound to an options record — but do not
import their layering wholesale. Evently has four projects per module because it has
many modules, CQRS, and an event bus. We have one endpoint that copies bytes.

**Cross-cutting concerns are out of scope.** No correlation IDs, no structured logging
setup, no global exception handler, no health checks, no auth. Milestone 9 owns those.

---

## Architecture decisions

### One new project, not three

Add **`src/LlmVoucherGateway.Api`** only. It is the composition root _and_, for now, the
home of the inference adapter:

```
src/LlmVoucherGateway.Api/
    Program.cs
    Endpoints/
        ChatCompletionsEndpoint.cs
    Inference/
        IInferenceClient.cs
        InferenceResponse.cs
        VllmInferenceClient.cs
        InferenceOptions.cs
    appsettings.json
    appsettings.Development.json
    LlmVoucherGateway.Api.csproj
```

The `Inference/` folder is written so it can be lifted into a
`LlmVoucherGateway.Infrastructure` project verbatim — no references back into `Api/` —
but creating that project now buys nothing. There is no second consumer and no test that
needs it isolated.

**Do not reference `LlmVoucherGateway.Domain` from the API yet.** Milestone 3 has no
domain concepts in the request path. Adding the reference early invites accidental
coupling of the passthrough to voucher types.

### The request body is forwarded opaquely — no DTOs

The pass-through does not need to understand the payload. Copy
`HttpContext.Request.Body` to vLLM and copy vLLM's response body back. No
deserialization, no re-serialization, no model validation.

This matters for more than effort: **round-tripping through DTOs would silently drop
every field we didn't model** (`stream_options`, `logprobs`, `tools`, sampling
parameters a harness happens to set), and Milestone 3's whole test is "the harness can't
tell the difference." Opaque forwarding is both the least code and the most faithful.

DTOs arrive in **Milestone 7**, when reading `usage` requires parsing — and then only for
the response, and only for the fields we meter on.

### Upstream errors pass through verbatim

vLLM's error payloads are already OpenAI-shaped (see
[api_documentation/errors/](../api_documentation/errors/)). Forwarding the status code and
body unchanged gives Milestone 3 correct error behavior for free. **Milestone 6** owns
producing our _own_ OpenAI-shaped errors for rejections we generate.

---

## Steps

Small, committable increments. Each has a check that must pass before moving on.

### Step 1 — Empty web project that builds clean under the strict analyzer settings

1. `dotnet new web -o src/LlmVoucherGateway.Api -n LlmVoucherGateway.Api`
2. Delete the template's sample `/` endpoint and any `weatherforecast` leftovers.
3. Strip the csproj down to what [Directory.Build.props](../Directory.Build.props)
   doesn't already provide (it supplies `TargetFramework`, `ImplicitUsings`, `Nullable`,
   analyzer settings, and SonarAnalyzer) — match how
   [LlmVoucherGateway.Domain.csproj](../src/LlmVoucherGateway.Domain/LlmVoucherGateway.Domain.csproj)
   is nearly empty.
4. Register it in [LlmVoucherGateway.slnx](../LlmVoucherGateway.slnx) under the `/src/`
   folder.

**Done when:** `dotnet build` succeeds with zero warnings.

`TreatWarningsAsErrors` + `AnalysisMode=All` is on repo-wide, so do this _before_ writing
logic. Expect to resolve a couple of analyzer complaints from the template itself (e.g.
`CA1852` on generated types, Sonar's top-level-statement rules). Fix them here, in
isolation, rather than while debugging a stream.

### Step 2 — Configuration and options binding

1. `InferenceOptions` with a single `BaseUrl` property (`string`, required).
2. Bind in `Program.cs`:
   ```csharp
   builder.Services
       .AddOptions<InferenceOptions>()
       .BindConfiguration(InferenceOptions.SectionName)
       .ValidateDataAnnotations()
       .ValidateOnStart();
   ```
3. `appsettings.json` — section present, value empty or absent.
   `appsettings.Development.json` — `http://localhost:8000`.

**Done when:** the app fails fast at startup with a clear message when `BaseUrl` is
missing, and starts when it's set.

`ValidateOnStart` is the point of this step. A misconfigured endpoint must be a startup
failure, not a 500 on the first student request. This is the configuration seam
Milestone 3 explicitly calls for, and it's what later lets the same binary target local
vLLM and real hardware.

### Step 3 — `IInferenceClient` and the response abstraction

```csharp
public interface IInferenceClient
{
    Task<InferenceResponse> CreateChatCompletionAsync(
        Stream requestBody,
        CancellationToken cancellationToken);
}
```

`InferenceResponse` exposes only what the endpoint needs to relay — status code, content
type, and the unread body stream — and owns disposal of the underlying
`HttpResponseMessage`:

```csharp
public sealed class InferenceResponse : IAsyncDisposable
{
    public required HttpStatusCode StatusCode { get; init; }
    public required string? ContentType { get; init; }
    public required Stream Body { get; init; }
    // holds the HttpResponseMessage privately; DisposeAsync disposes it
}
```

**Keep `HttpResponseMessage` out of the interface.** Returning it directly is tempting
and shorter, but it makes every future consumer (the metering wrapper in Milestone 7, any
fake in tests) an `HttpClient` participant. This type is the one place a little ceremony
is worth it, because it _is_ the seam.

**Done when:** it compiles. No behavior yet.

### Step 4 — `VllmInferenceClient`, registered as a typed client

1. ```csharp
   builder.Services.AddHttpClient<IInferenceClient, VllmInferenceClient>((serviceProvider, httpClient) =>
   {
       InferenceOptions options = serviceProvider
           .GetRequiredService<IOptions<InferenceOptions>>().Value;

       httpClient.BaseAddress = new Uri(options.BaseUrl);
       httpClient.Timeout = Timeout.InfiniteTimeSpan;
   });
   ```

2. The implementation builds a `POST /v1/chat/completions` with
   `new StreamContent(requestBody)`, `Content-Type: application/json`, and sends it with
   **`HttpCompletionOption.ResponseHeadersRead`**.

**Done when:** a non-streaming request through the API returns the same JSON as hitting
vLLM directly (Step 6 verifies properly; a `curl` sanity check suffices here).

Two lines in this step are the milestone's entire risk surface:

- **`HttpCompletionOption.ResponseHeadersRead`.** The default,
  `ResponseContentRead`, buffers the _complete_ response before `SendAsync` returns.
  Omit it and streaming is dead on the outbound leg, with no error — just a long pause
  followed by the whole answer at once.
- **`Timeout = Timeout.InfiniteTimeSpan`.** `HttpClient`'s 100-second default cancels
  long agentic generations mid-stream and surfaces as a `TaskCanceledException` that
  reads like a network fault. Request lifetime is bounded by the client's own
  cancellation (Step 5) and, later, by the reverse proxy.

### Step 5 — The `/v1/chat/completions` endpoint

A static class in `Endpoints/` with a `MapChatCompletions(this IEndpointRouteBuilder)`
extension, called from `Program.cs`. The handler:

1. Calls `IInferenceClient` with `HttpContext.Request.Body` and
   `HttpContext.RequestAborted`.
2. Copies `StatusCode` and `ContentType` onto the response.
3. Calls `HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering()`.
4. `await response.Body.CopyToAsync(HttpContext.Response.Body, cancellationToken);`

Notes on each:

- **Forward only status code and `Content-Type`.** Copying the rest of vLLM's headers
  drags along `Content-Length` and `Transfer-Encoding`, which Kestrel manages itself —
  forwarding them produces truncated responses or protocol errors.
- **Do not add response compression.** Not registering
  `AddResponseCompression` is sufficient; the note exists so nobody adds it for Milestone
  8 without revisiting streaming. Compression middleware coalesces small SSE writes and
  reintroduces exactly the failure this milestone exists to eliminate.
- **`CopyToAsync` flushes as it goes** against an unbuffered Kestrel response body, which
  is why no manual `FlushAsync` loop is needed. `DisableBuffering()` is belt-and-braces
  against middleware added later.
- **Pass `RequestAborted` all the way through** so a student killing their harness tears
  down the vLLM request instead of leaving it generating.

No `IEndpoint` interface, no assembly scanning for endpoint registration. That pattern
pays off somewhere north of a dozen endpoints; with one, it's indirection with no reader.
Revisit when the admin endpoints land in Milestones 5 and 8.

**Done when:** `curl` against the API returns a correct non-streaming completion, and a
`"stream": true` request returns SSE chunks ending in `[DONE]`.

### Step 6 — Prove streaming is unbuffered, with timestamps

**Requires live vLLM** (`make up-llm`). This is the step the milestone exists for, and
"it eventually printed the answer" does not pass it.

1. Timestamp each SSE line as it arrives:
   ```bash
   curl -N -s http://localhost:5000/v1/chat/completions \
       -H "Content-Type: application/json" \
       -d '{"model":"Qwen/Qwen2.5-0.5B-Instruct",
            "messages":[{"role":"user","content":"Count slowly from 1 to 50."}],
            "stream":true}' \
     | while IFS= read -r line; do printf '%s %s\n' "$(date +%s.%3N)" "$line"; done
   ```
2. Run the same against vLLM on `:8000` directly.
3. Compare the two timestamp columns. **Chunk arrival must be spread across the
   generation window in both, and the deltas should be comparable.** All-lines-at-one
   timestamp means buffering, even though the payload is byte-identical.
4. Record the numbers somewhere durable — a short section in
   [api_documentation/README.md](../api_documentation/README.md) or a note in the
   milestone commit. When something buffers in Milestone 9 behind Caddy, this is the
   baseline you diff against.

**Done when:** timestamp spread through the API matches vLLM direct.

### Step 7 — Point the harness at the API and redo the Milestone 2 task

1. Reconfigure Aider/OpenCode's base URL from `:8000` to the API's port. Nothing else
   about the harness config changes.
2. Run the CSV file-edit task from Milestone 2 end to end.
3. **Watch the harness's request log for paths other than `/v1/chat/completions`.**
   `GET /v1/models` is the likely one — harnesses commonly probe it to validate
   configuration or resolve a model name, and a 404 there can fail startup before any
   completion is attempted.

If it's needed: add it as a second opaque pass-through (a `GET` method on
`IInferenceClient`, same relay logic). Do not model the models payload. If Milestone 2
showed the harness never calls it, skip it — the point is to find out from observed
traffic rather than to guess in either direction.

**Done when:** the model reads the file, edits it, and reports back — through the API,
with the harness unmodified apart from its base URL.

### Step 8 — Developer ergonomics and cleanup

Small, and only what's actually used:

1. `make run-api` target in the [Makefile](../Makefile), alongside `up-llm`.
2. A `.http` file (or extend the `curl` comments pattern already used in
   [docker-compose.llm.yml](../docker-compose.llm.yml)) with the streaming and
   non-streaming requests.
3. Trim `launchSettings.json` to an HTTP-only profile. TLS is Caddy's job in Milestone 9;
   a dev-cert HTTPS profile here is one more variable between you and a buffering bug.
4. Update [pilot-roadmap.md](pilot-roadmap.md): mark Milestone 3 complete.

**Done when:** a fresh clone can run `make up-llm && make run-api` and issue a streaming
request from the `.http` file.

---

## On testing this milestone

**Do not try to prove unbuffered streaming with `WebApplicationFactory`.** `TestServer`
is not Kestrel; its response body pipeline has different buffering and flush semantics, so
a passing in-process test proves nothing about production and a failing one may be an
artifact of the harness. The roadmap already says this milestone has to be proven against
the real server — Step 6 is that proof, and it's a manual check on purpose.

What _would_ be worth unit-testing here is thin: that `VllmInferenceClient` passes
`ResponseHeadersRead` and composes the right URL, via a fake `HttpMessageHandler`. That's
a test of two literals. Written now it's noise; written in **Milestone 7**, when the
client grows usage extraction, the fake handler becomes genuinely useful — build the test
project then.

The existing [Domain.UnitTests](../tests/LlmVoucherGateway.Domain.UnitTests/) project is
untouched by this milestone.

---

## Deliberately deferred

Noted here so they aren't mistaken for oversights.

| Item                                                                     | Lands in                 | Why not now                                                                                                                                                                          |
| ------------------------------------------------------------------------ | ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `LlmVoucherGateway.Infrastructure` project                               | 4–6                      | `Inference/` is written to lift out cleanly; a project with one folder in it is overhead until there's a DB adapter beside it.                                                       |
| Request/response DTOs                                                    | 7                        | Passthrough needs no schema. Parsing arrives with metering, and only for `usage`. Generated DTOs and the CI drift check were dropped outright — the pinned image tag is the control. |
| Fake `IInferenceClient` replaying fixtures                               | 6                        | The seam exists as of Step 3; the fake is written when validation first needs a passing request without a GPU. A class, not a stub server. Milestone 3 is explicitly GPU-bound.      |
| Domain project reference from the API                                    | 5                        | Nothing in the request path is a domain concept yet.                                                                                                                                 |
| Credential middleware, `{ VoucherId?, IsAdmin }`                         | 5                        | Written once there, admin branch first.                                                                                                                                              |
| Our own OpenAI-shaped error responses                                    | 6                        | Upstream errors relay verbatim and are already correctly shaped.                                                                                                                     |
| `stream_options: {"include_usage": true}` injection                      | 7                        | Requires reading and rewriting the request body — the one thing opaque passthrough avoids. Revisit the tradeoff there, with the capture set in hand.                                 |
| Structured logging, correlation IDs, global exception handler, `/health` | 9                        | Out of scope by instruction.                                                                                                                                                         |
| Caddy, buffering config at the proxy, read timeouts                      | 9                        | Step 6's timestamp baseline is what you'll diff against when you add it.                                                                                                             |
| Response compression                                                     | never, for this endpoint | Coalesces SSE writes. If it's added for other routes, exclude `/v1/chat/completions` explicitly.                                                                                     |
| Rate limiting, concurrency caps                                          | 10                       | Needs the load-test numbers to pick a value.                                                                                                                                         |
