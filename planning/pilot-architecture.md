# Classroom Local-LLM Agent Pilot — Architecture Overview

## Goal
Give students a free, "Claude Code"-like experience (agentic harness with tool access to files, shell, and spreadsheets) without requiring a paid API or powerful student hardware. Compute is centralized on an institution-hosted server; students only run a lightweight harness locally.

## Layers

### 1. Model Serving Layer (institution-hosted)
- **Tool:** vLLM (preferred over LM Studio for this use case — vLLM is headless, multi-user, and built for concurrent serving; LM Studio requires a GUI session and is meant for single-user local use).
- **Hardware:** A server with a capable GPU (exact sizing TBD based on model choice and expected concurrent students).
- **Model:** An open coding-capable model (e.g., a Qwen Coder variant or similar) loaded into vLLM.
- **Output:** An OpenAI-compatible chat completions endpoint, reachable only on the internal network by default (not exposed raw to the internet).

### 2. Access & Security Layer (reverse proxy)
- **Tool:** Nginx or Caddy sitting in front of vLLM.
- **Responsibilities:**
  - TLS termination (encrypts traffic between students and the server).
  - API key authentication — each student issued a unique key.
  - Rate limiting per key, so no single student can monopolize the GPU.
  - Optional: usage logging per key for tracking adoption/engagement.
- **Why needed:** Neither vLLM nor LM Studio ships with built-in authentication, so this layer is mandatory for any public-facing exposure.

### 3. Student-Side Harness Layer (runs on each student's machine)
- **Tool:** A lightweight agent harness such as OpenCode or Aider (Cline is another option).
- **Role:** Provides the "hands" — reads/writes files, runs shell commands, manipulates spreadsheets (CSV recommended for simplicity, though full Excel is possible with the right libraries).
- **Configuration:** Points at the institution's public endpoint (proxy URL) using the student's individual API key, instead of a local model server.
- **Hardware requirement:** Minimal — no GPU or heavy local compute needed, since inference happens on the central server.
- **OS support:** Windows, Mac, Linux all supported natively. Chromebooks are not well supported unless the device has Linux (Crostini) enabled, and even then it's inconsistent.

## Data Flow Summary
1. Student types a request into the harness (e.g., "clean up this CSV and add a totals column").
2. Harness sends the prompt + relevant file context to the proxy endpoint, authenticated with the student's API key.
3. Proxy validates the key, applies rate limits, and forwards the request to vLLM.
4. vLLM runs inference on the shared GPU and returns the model's response (including any tool-call instructions).
5. Harness executes the requested tool actions locally (e.g., editing the CSV) and reports results back to the model for the next step, looping until the task is done.

## Open Questions for Pilot Phase
- Server/GPU sizing based on expected concurrent student load.
- Which specific open model best balances coding capability vs. resource use.
- Whether per-student rate limits should be static or usage-based.
- Whether to track/report usage for course credit or engagement purposes.
