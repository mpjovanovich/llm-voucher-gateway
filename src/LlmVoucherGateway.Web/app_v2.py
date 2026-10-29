import json
import os

import streamlit as st
from openai import OpenAI, APIConnectionError, APIStatusError

# ---- Config (override with env vars) -----------------------------------------
BASE_URL = os.getenv("VLLM_BASE_URL", "http://localhost:8000/v1")
MAX_FILE_CHARS = int(os.getenv("MAX_FILE_CHARS", 12_000))        # per file, ~3k tokens
MAX_HISTORY_CHARS = int(os.getenv("MAX_HISTORY_CHARS", 24_000))  # total sent per request
TEXT_TYPES = ["txt", "md", "py", "csv", "json", "html", "xml", "yaml", "yml", "log", "sql"]

try:
    from pypdf import PdfReader
    FILE_TYPES = TEXT_TYPES + ["pdf"]
except ImportError:  # PDF support is optional: pip install pypdf
    PdfReader = None
    FILE_TYPES = TEXT_TYPES

st.set_page_config(page_title="LLM Playground", page_icon="≡ƒÆ¼")
st.title("LLM Playground")
st.session_state.setdefault("msgs", [])


# ---- Helpers ------------------------------------------------------------------
@st.cache_resource
def get_client(token: str) -> OpenAI:
    return OpenAI(base_url=BASE_URL, api_key=token, timeout=60, max_retries=1)


@st.cache_data(ttl=300)
def list_models(token: str) -> list[str]:
    # Exceptions are not cached, so a bad voucher is re-checked on the next run.
    return [m.id for m in get_client(token).models.list().data]


def read_file(f) -> tuple[str, bool]:
    """Return (text, was_truncated) for an uploaded file."""
    if f.name.lower().endswith(".pdf") and PdfReader:
        text = "\n".join(page.extract_text() or "" for page in PdfReader(f).pages)
    else:
        text = f.getvalue().decode("utf-8", errors="replace")
    return text[:MAX_FILE_CHARS], len(text) > MAX_FILE_CHARS


def build_messages(system: str, msgs: list[dict]) -> list[dict]:
    """Keep the most recent turns that fit in MAX_HISTORY_CHARS."""
    kept, total = [], 0
    for m in reversed(msgs):
        total += len(m["content"])
        if total > MAX_HISTORY_CHARS and kept:
            break
        kept.append({"role": m["role"], "content": m["content"]})
    kept.reverse()
    return ([{"role": "system", "content": system}] if system.strip() else []) + kept


# ---- Sidebar ------------------------------------------------------------------
with st.sidebar:
    token = st.text_input("Voucher", type="password")

    model = None
    if token:
        try:
            models = list_models(token)
            model = st.selectbox("Model", models) if models else None
        except APIStatusError as e:
            st.error(f"{e.status_code}: {e.message}")
        except APIConnectionError:
            st.error(f"Can't reach the model server at {BASE_URL}.")

    with st.expander("Generation settings"):
        system_prompt = st.text_area("System prompt", "You are a helpful assistant.")
        temperature = st.slider("Temperature", 0.0, 1.5, 0.7, 0.05)
        max_tokens = st.slider("Max tokens", 64, 2048, 1000, 64)
    actions = st.container()  # filled at the end, after this run's chat turn

# ---- History ------------------------------------------------------------------
for m in st.session_state.msgs:
    st.chat_message(m["role"]).markdown(m.get("display", m["content"]))

# ---- Input --------------------------------------------------------------------
ready = bool(token and model)
submission = st.chat_input(
    "Ask something" if ready else "Enter a valid voucher in the sidebar first",
    accept_file="multiple",
    file_type=FILE_TYPES,
    disabled=not ready,
)

if submission:
    # Model sees file contents; the chat shows only file names.
    parts, labels = [], []
    for f in submission.files:
        text, truncated = read_file(f)
        parts.append(f'<file name="{f.name}">\n{text}\n</file>')
        labels.append(f"≡ƒôÄ `{f.name}`" + (" *(truncated)*" if truncated else ""))
    question = submission.text or "Summarize the attached file(s)."
    content = "\n\n".join(parts + [question])
    display = "\n\n".join([question] + ([" ".join(labels)] if labels else []))

    st.session_state.msgs.append({"role": "user", "content": content, "display": display})
    st.chat_message("user").markdown(display)

    messages = build_messages(system_prompt, st.session_state.msgs)

    with st.chat_message("assistant"):
        try:
            stream = get_client(token).chat.completions.create(
                model=model,
                messages=messages,
                stream=True,
                temperature=temperature,
                max_tokens=max_tokens,
            )
            reply = st.write_stream(
                c.choices[0].delta.content or "" for c in stream if c.choices
            )
        except APIConnectionError:
            st.error(f"Lost connection to {BASE_URL}. Try again.")
            st.session_state.msgs.pop()  # don't leave an unanswered turn in history
        except APIStatusError as e:
            st.error(f"{e.status_code}: {e.message}")
            st.session_state.msgs.pop()
        else:
            st.session_state.msgs.append({
                "role": "assistant",
                "content": reply,
                # Settings this reply was generated with (not sent to the model)
                "system_prompt": system_prompt,
                "temperature": temperature,
            })

# ---- Sidebar actions (rendered last so they reflect this run's turn) ---------
with actions:
    col1, col2 = st.columns(2)
    if col1.button("Clear chat", use_container_width=True):
        st.session_state.msgs = []
        st.rerun()
    export = {
        "model": model,
        "max_tokens": max_tokens,
        "messages": st.session_state.msgs,
    }
    col2.download_button(
        "Download",
        json.dumps(export, indent=2),
        file_name="chat.json",
        mime="application/json",
        use_container_width=True,
        disabled=not st.session_state.msgs,
    )
