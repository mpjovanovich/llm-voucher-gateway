# Pilot Milestones — Classroom Local-LLM Agent Project

## Milestone 1 — Bare vLLM, direct access
Get vLLM running as a process and prove the model responds.
- Provision a machine with a usable GPU (can be your own workstation at this stage)
- Install vLLM and dependencies, confirm GPU drivers work
- Pick and download a coding-capable open model
- Start the vLLM server on a local port
- Hit `/v1/chat/completions` directly with curl or Postman and get a valid JSON response back
- **Exit criteria:** a real coding answer returned from a raw HTTP request, no proxy involved

## Milestone 2 — Harness connected end-to-end
Prove the agent loop works against your own server.
- Install a harness locally (OpenCode or Aider)
- Point its config at the vLLM endpoint on localhost
- Run the spreadsheet/CSV demo task as a smoke test
- **Exit criteria:** the model reads a file, edits it, and reports back — the full tool loop

## Milestone 3 — Containerization
Make the deployment reproducible.
- Move vLLM into a Docker container with GPU passthrough
- Confirm parity with Milestone 1/2 results
- Write a Docker Compose file as the base for the next milestone
- **Exit criteria:** teardown and restart produces an identical working environment

## Milestone 4 — Reverse proxy with a single key
Add the access layer, minimally.
- Add Nginx or Caddy as a second container
- Terminate TLS with a real certificate
- Enforce a single hardcoded API key
- Verify vLLM is no longer reachable directly from outside
- **Exit criteria:** request succeeds with the key, fails without it

## Milestone 5 — Multi-user access
Scale the access layer to a class.
- Issue per-student API keys
- Add per-key rate limiting
- Add usage logging per key
- Basic key rotation/revocation process
- **Exit criteria:** several simulated students hitting the server concurrently without one starving the others

## Milestone 6 — Load and model validation
Find the real limits before students do.
- Test concurrency at expected class size
- Evaluate model quality on representative student tasks
- Tune model choice, quantization, and vLLM settings based on results
- **Exit criteria:** documented capacity numbers and a settled model choice

## Milestone 7 — Classroom rollout
Package it for students.
- Write student-facing setup instructions (Windows/Mac/Linux)
- Document the Chromebook limitation and any workaround
- Prepare the demo lesson (spreadsheet manipulation)
- Small pilot group before full class
- **Exit criteria:** a student with no prior setup can be running in under 15 minutes

## Deferred / Later
- Kubernetes or multi-server scaling
- SSO integration instead of static API keys
- Monitoring dashboards and alerting
