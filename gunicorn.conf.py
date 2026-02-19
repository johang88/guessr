bind = "0.0.0.0:5000"
workers = 2
timeout = 120
worker_tmp_dir = "/dev/shm"
control_socket = "/dev/shm/gunicorn.sock"

# Route gunicorn logs through Python logging so OTel captures them
accesslog = "-"
errorlog = "-"
loglevel = "info"


def post_fork(server, worker):
    """Reinitialize OTel log provider after fork — exporter threads don't survive fork."""
    import os
    if not os.environ.get("OTEL_EXPORTER_OTLP_ENDPOINT"):
        return
    try:
        from opentelemetry.sdk._logs import LoggerProvider
        from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
        from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter
        from opentelemetry import _logs
        from opentelemetry.instrumentation.logging import LoggingInstrumentor

        provider = LoggerProvider()
        provider.add_log_record_processor(BatchLogRecordProcessor(OTLPLogExporter()))
        _logs.set_logger_provider(provider)
        LoggingInstrumentor().instrument()
    except Exception as e:
        server.log.warning("Failed to reinitialize OTel log provider: %s", e)
