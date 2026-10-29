import streamlit as st
from openai import OpenAI, APIStatusError

st.title("LLM Playground")
token = st.sidebar.text_input("Voucher", type="password")
st.session_state.setdefault("msgs", [])

for m in st.session_state.msgs:
    st.chat_message(m["role"]).write(m["content"])

if prompt := st.chat_input("Ask something"):

    if not token:
        st.error("Enter your voucher in the sidebar first.")
        st.stop()

    st.session_state.msgs.append({"role": "user", "content": prompt})
    st.chat_message("user").write(prompt)
    client = OpenAI(base_url="http://localhost:8000/v1", api_key=token)

    try:
        stream = client.chat.completions.create(
            # This should go in config - needs to match exactly the model returned in the json response
            model="Qwen/Qwen2.5-0.5B-Instruct", 
            messages=st.session_state.msgs,
            stream=True, 
            max_tokens=1000
        )

        with st.chat_message("assistant"):
            reply = st.write_stream(
                c.choices[0].delta.content or "" for c in stream if c.choices
            )

        st.session_state.msgs.append({"role": "assistant", "content": reply})
    except APIStatusError as e:
        st.error(f"{e.status_code}: {e.message}")
