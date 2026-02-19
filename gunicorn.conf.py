bind = "0.0.0.0:5000"
workers = 1
threads = 4
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
    if not any(os.environ.get(k) for k in [
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
    ]):
        return
    try:
        try:
            from opentelemetry.exporter.otlp.proto.http.log_exporter import OTLPLogExporter
        except ImportError:
            from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter
        try:
            from opentelemetry.sdk.logs.export import SimpleLogRecordProcessor
        except ImportError:
            from opentelemetry.sdk._logs.export import SimpleLogRecordProcessor
        from opentelemetry import _logs

        # Get the existing provider (set by opentelemetry-instrument before fork)
        # and add a fresh processor — the original BatchLogRecordProcessor's
        # background thread dies on fork, so we add a sync one instead.
        provider = _logs.get_logger_provider()
        provider.add_log_record_processor(SimpleLogRecordProcessor(OTLPLogExporter()))
        server.log.info("OTel log processor added in worker %s", worker.pid)
    except Exception as e:
        server.log.warning("Failed to add OTel log processor: %s", e)
