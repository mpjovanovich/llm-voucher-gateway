# LLM Voucher Gateway

This application provides a gateway to self-hosted LLMs. It provides the following benefits over direct access to the LLM API:

1. Access control. Only users who have been issued a voucher can use the API.
1. Budget enforcement. Usage is tracked per-voucher, and usage is capped at voucher issuance.
1. Expiry. Vouchers expiry date is set at issuance.

As an example, you may issue a voucher to a user allowing them to use a total of 10000 tokens, and set the expiry date to one month from the date of issuance. After all tokens are consumed, or after the expiry date, the user will no longer be able to use the API.

No Personally Identifiable Information (PII) is stored in the system. They system instead uses a single issued voucher key as the authentication artifact.

This application is free for public use. All that is needed is infrastructure to host the components. Even a personal computer can work, given the right hardware.

## Goals

Provide OpenAI shaped responses to hook into any application that supports the OpenAI API.

Self-hosting is meant to address usage restrictions on commercial models.

### Chat Interface

_Problem:_ Current frontier model web interfaces are unreliable and have limited free usage.

_Solution:_ Generic web interface with text and file parsing, pointed at the Gateway API application. We used streamlit for this.

### IDE Integration

_Problem:_ Users don't get many free chat completions or agent calls within the IDE from existing providers.

_Solution:_ IDE (VS Code, Cursor, etc.) integration via extentions, pointed at the Gateway API application. Minimally this should be chat completions, but ideally this is agentic / tool calling.

### Harness Integration

_Problem:_ Even though there are plenty of open source harnesses, you still need an API key to use them.

_Solution:_ Use any open source harness and point it at the Gateway API application.

## Workflow

1. Admin requests vouchers for users and issues them. These are in the form of an API key.
2. Users plug in vouchers to OpenAI compatible software and use it.
3. If users runs out of tokens, they can request more from the admin.

Users never hit vLLM directly. All requests flow through this application - the metering gateway. This does just enough to prevent overt abuse while avoiding PII storage:

- User must have an issued voucher to use the API.
  - Functions as very basic access control.
  - Vouchers have date expiry.
  - Vouchers have a usage cap.
- Usage caps mostly solve throughput issues.
  - Users can't intentionally abuse the system with a large number of requests.
  - Unintentional usage doesn't kill the system.

## Components

We're keeping a monorepo with the following components:

**vLLM** — official Docker image; not technically ours. This is what serves the chat completions. Talks to API layer only.

**ASP.NET API** — our gateway layer between the frontend and vLLM.

**Streamlit frontend** — the web interface.

## Auth model

The client uses the voucher as the bearer token to authenticate with the Gateway API. Only the Gateway API knows the vLLM API key.

## Budget and Usage Recording Model

Budget model decided to be on total tokens (input + output). This is reported by vLLM. Usage is updated upon each request.

Budgeting (enforcement, checked before calling vLLM, soft cap so users can go over during request and next request is denied not current):
