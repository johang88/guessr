FROM python:3.12-slim

LABEL maintainer="guessr-scoreboard"
LABEL description="Guessr Scoreboard - Daily puzzle score tracker"

# Prevent Python from writing .pyc files and enable unbuffered output
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser -d /app -s /sbin/nologin appuser

WORKDIR /app

# Install production WSGI server and OpenTelemetry
RUN pip install --no-cache-dir flask "gunicorn==21.2.0" \
    opentelemetry-distro \
    opentelemetry-exporter-otlp

# Auto-detect installed libraries and install the right OTel instrumentations
RUN opentelemetry-bootstrap -a install

# Copy application files
COPY app.py index.html ./

# Create directory for SQLite database (mount a volume here in k8s)
RUN mkdir -p /data && chown appuser:appuser /data

# Point the app at the persistent data directory
ENV DB_PATH=/data/guessr_scores.db
ENV OTEL_SERVICE_NAME=guessr
ENV OTEL_PYTHON_LOGGING_AUTO_INSTRUMENTATION_ENABLED=true

EXPOSE 5000

# Switch to non-root user
USER appuser

# Health check for Kubernetes readiness/liveness probes
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:5000/')" || exit 1

# Run with gunicorn for production
CMD ["opentelemetry-instrument", "gunicorn", "--bind", "0.0.0.0:5000", "--workers", "2", "--timeout", "120", "app:app"]
