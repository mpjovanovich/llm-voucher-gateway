# LLM Voucher Gateway

This application provides a gateway to self-hosted LLMs, allowing a hosted LLM model to serve multiple users with usage constraints in place. It provides the following benefits over direct access to the LLM API:

1. Access control. Only users who have been issued a voucher can use the API.
1. Budget enforcement. Usage is tracked per-voucher, and usage is capped at voucher issuance.
1. Expiry. Vouchers expiry date is set at issuance.

As an example, you may issue a voucher to a user allowing them to use a total of 10000 tokens, and set the expiry date to one month from the date of issuance. After all tokens are consumed, or after the expiry date, the user will no longer be able to use the API.

No Personally Identifiable Information (PII) is stored in the system. They system instead uses a single issued voucher key as the authentication artifact.

This application is free for public use. All that is needed is infrastructure to host the components. Even a personal computer can work, given the right hardware.

## Goals

The application provides a way for users to share access to a hosted vLLM instance. It provides OpenAI shaped responses to hook into any application that supports the OpenAI API.

Below we list some of the problems that motivated this project, and the benefits that the application provides. In summary, it can be stated as follows:

> "I want to offer access to a hosted LLM model to my users while still retaining control over authorization (who can use it) and usage (how many tokens they can use)."

### Chat Interface

_Problem:_ Current frontier model web interfaces can have availability issues and have limited free usage.

_Solution:_ The application provides a generic web interface with text and file parsing, pointed at the Gateway API application. We used streamlit for this. Authorized users can use a voucher key within the chat interface to interact with the hosted LLM model. Usage is managed as requests pass through the gateway.

### Harness Integration

_Problem:_ Even though there are plenty of open source harnesses, you still need an API key to use them. This leads to the same "limited free usage" problem listed above.

_Solution:_ The application provides an OpenAI compatible API that can be used with any open source harness. Authorized users can use a voucher key to make requests against the Gateway API. Usage is managed as requests pass through the gateway.

### IDE Integration

_Problem:_ Users don't get many free chat completions or agent calls within the IDE from existing providers.

_Solution:_ The application provides an OpenAI compatible API that can be used with any IDE extension that supports it. Authorized users can use a voucher key to make requests against the Gateway API. Usage is managed as requests pass through the gateway.

## Workflow

1. Admin requests vouchers for users and issues them. These vouchers serve as a form of authorization to the Gateway API.
2. Users plug in vouchers to OpenAI compatible software and use it.
3. If users runs out of tokens, they can request more from the admin.

Users never hit vLLM directly. All requests flow through the Gateway API. This does just enough to prevent overt abuse while avoiding PII storage:

- User must have an issued voucher to use the API.
  - Functions as very basic access control.
  - Vouchers have expiry date.
  - Vouchers have a usage cap.
- Usage caps help to solve throughput issues.
  - Users can't intentionally abuse the system with a large number of requests.
  - Unintentional usage doesn't kill the system.

_Limitations:_

- There is nothing stopping the users from using all of their tokens in a short time period, which may strain the system.

## Components

This application contains the following components, which we are keeping in a monorepo:

**vLLM** — official Docker image (not part of the repo). This is what serves the chat completions. Talks to API layer only.

**ASP.NET API** — gateway layer between the frontend and vLLM.

**Streamlit frontend** — the web interface.

## Auth model

The client uses the voucher as the bearer token to authenticate with the Gateway API. vLLM is reachable only from the Gateway API, never publicly.

## Budget and Usage Recording Model

"Usage" is tracked on total tokens (input + output). This is reported by vLLM. Usage is updated upon each request.

Budgeting enforcement is checked before calls are forwarded to vLLM. If no budget remains, the call is denied.
