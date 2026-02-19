FROM python:3.12-slim

LABEL maintainer="guessr-scoreboard"
LABEL description="Guessr Scoreboard - Daily puzzle score tracker"

# Prevent Python from writing .pyc files and enable unbuffered output
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser -d /app -s /sbin/nologin appuser

WORKDIR /app

# Install production ASGI server and OpenTelemetry
RUN pip install --no-cache-dir flask uvicorn[standard] a2wsgi \
    opentelemetry-distro \
    opentelemetry-exporter-otlp

# Auto-detect installed libraries and install the right OTel instrumentations
RUN opentelemetry-bootstrap -a install

ARG GIT_SHA=dev
ENV APP_VERSION=$GIT_SHA

# Copy application files
COPY app.py index.html ./

# Create directory for SQLite database (mount a volume here in k8s)
RUN mkdir -p /data && chown appuser:appuser /data

# Point the app at the persistent data directory
ENV DB_PATH=/data/guessr_scores.db
ENV OTEL_SERVICE_NAME=guessr
ENV OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
ENV OTEL_LOGS_EXPORTER=otlp
ENV OTEL_PYTHON_LOGGING_AUTO_INSTRUMENTATION_ENABLED=true

EXPOSE 5000

# Switch to non-root user
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:5000/health')" || exit 1

# Run with uvicorn — single process, event loop, no fork so OTel logging works correctly
CMD ["opentelemetry-instrument", "uvicorn", "app:asgi_app", "--host", "0.0.0.0", "--port", "5000"]
