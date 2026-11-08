# Build context is this directory:
#   docker build -f src/LlmVoucherGateway.Web/Dockerfile -t llm-voucher-gateway-web src/LlmVoucherGateway.Web

FROM python:3.12-slim

WORKDIR /app

# Requirements first, so the install layer is reused until a dependency changes.
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Production default. make run-web does not set this, so the dev default in app.py stays.
ENV GATEWAY_BASE_URL=http://gateway:8080/v1

EXPOSE 8501

CMD ["streamlit", "run", "app.py", \
     "--server.address", "0.0.0.0", \
     "--server.port", "8501", \
     "--server.headless", "true"]
