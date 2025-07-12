"""
Title         : exporters.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/metrics/exporters.py

Description
-----------
Prometheus metrics exporters for HTTP endpoints.
Provides ASGI application and standalone server for metrics scraping.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any

from beartype import beartype
from prometheus_client import make_asgi_app  # pyright: ignore[reportUnknownVariableType]

from mzn.errors.namespace import Error
from mzn.log.namespace import Log
from mzn.metrics.exceptions import ExportError
from mzn.metrics.registry import get_registry


if TYPE_CHECKING:
    from prometheus_client import CollectorRegistry

    from mzn.log.core import Logger


# --- Module State -------------------------------------------------------------

_logger: Logger | None = None


# --- Internal Helpers ---------------------------------------------------------


async def _get_logger() -> Logger:
    """Get or create the exporters logger."""
    if _logger is None:
        logger = await Log.console("metrics.exporters").level(Log.Level.INFO).build()
        globals()["_logger"] = logger
        return logger
    return _logger


# --- ASGI Exporter ------------------------------------------------------------


@beartype
async def create_asgi_app(
    registry: Annotated[CollectorRegistry | None, "Prometheus registry"] = None,
) -> Any:  # noqa: ANN401
    """
    Create ASGI application for prometheus metrics endpoint.

    This creates an ASGI app that serves prometheus metrics in text format.
    The app can be mounted at any path in ASGI frameworks like FastAPI,
    Starlette, Quart, etc.

    Args:
        registry: Custom prometheus registry (default: uses our global registry)

    Returns:
        ASGI application callable that serves metrics

    Raises:
        ExportError: If ASGI app creation fails

    Example:
        ```python
        # Create metrics app
        metrics_app = await create_asgi_app()

        # Mount in FastAPI at any path
        from fastapi import FastAPI
        app = FastAPI()
        app.mount("/metrics", metrics_app)

        # Or use with Starlette
        from starlette.applications import Starlette
        app = Starlette()
        app.mount("/prometheus", metrics_app)
        ```
    """
    try:
        # Use our registry if none provided
        if registry is None:
            registry = await get_registry()

        # Create ASGI app using prometheus-client
        # This returns an ASGI app that serves metrics in prometheus text format
        # with proper content-type headers
        asgi_app = make_asgi_app(registry=registry)  # pyright: ignore[reportUnknownVariableType]

        logger = await _get_logger()
        await logger.info(
            "ASGI metrics app created",
            registry_id=id(registry),
        )

        return asgi_app  # noqa: TRY300  # pyright: ignore[reportUnknownVariableType]

    except Exception as e:
        error = Error.create(
            "metrics.asgi_creation_failed",
            message=f"Failed to create ASGI metrics app: {e}",
            error=str(e),
        )
        raise ExportError(error.context) from e


# --- Standalone Server --------------------------------------------------------


@beartype
async def serve(
    host: Annotated[str, "Host to bind to"] = "0.0.0.0",  # noqa: S104
    port: Annotated[int, "Port to bind to"] = 9090,
    path: Annotated[str, "URL path for metrics"] = "/metrics",
) -> None:
    """
    Run standalone metrics server using uvicorn.

    This is a convenience function for testing or simple deployments.
    For production, consider mounting the ASGI app in your main application.

    Args:
        host: Host address to bind (default: 0.0.0.0)
        port: Port number to bind (default: 9090)
        path: URL path for metrics endpoint (default: /metrics)

    Raises:
        ExportError: If server fails to start
        ImportError: If uvicorn is not installed

    Example:
        ```python
        # Run metrics server on port 9090
        await serve()

        # Custom configuration
        await serve(host="127.0.0.1", port=8080, path="/prometheus")
        ```

    Note:
        This function will run until interrupted (Ctrl+C).
        Uvicorn must be installed: `pip install uvicorn`
    """
    try:
        # Import uvicorn (optional dependency)
        try:
            import uvicorn  # noqa: PLC0415
        except ImportError as e:
            msg = "uvicorn is required for standalone server. Install with: pip install uvicorn"
            raise ImportError(msg) from e

        # Create the ASGI app
        metrics_app = await create_asgi_app()

        # Create a simple ASGI app that routes the path
        async def app(scope: dict[str, Any], receive: Any, send: Any) -> None:  # noqa: ANN401
            """Simple ASGI app that serves metrics at the configured path."""
            if scope["type"] == "http" and scope["path"] == path:
                await metrics_app(scope, receive, send)
            else:
                # Return 404 for other paths
                await send({
                    "type": "http.response.start",
                    "status": 404,
                    "headers": [[b"content-type", b"text/plain"]],
                })
                await send({
                    "type": "http.response.body",
                    "body": b"Not Found",
                })

        logger = await _get_logger()
        await logger.info(
            "Starting metrics server",
            host=host,
            port=port,
            path=path,
            url=f"http://{host}:{port}{path}",
        )

        # Configure and run uvicorn
        config = uvicorn.Config(
            app=app,
            host=host,
            port=port,
            log_level="info",
            access_log=False,  # Disable access logs for metrics endpoint
        )
        server = uvicorn.Server(config)
        await server.serve()

    except ImportError:
        raise
    except Exception as e:
        error = Error.create(
            "metrics.server_failed",
            message=f"Failed to start metrics server: {e}",
            host=host,
            port=port,
            path=path,
            error=str(e),
        )
        raise ExportError(error.context) from e


# --- FastAPI Integration ------------------------------------------------------


@beartype
async def mount_metrics(
    app: Annotated[Any, "FastAPI or Starlette application instance"],  # noqa: ANN401
    path: Annotated[str, "URL path for metrics"] = "/metrics",
) -> None:
    """
    Mount metrics endpoint on existing FastAPI/Starlette application.

    This is a convenience function to easily add prometheus metrics
    to an existing ASGI application.

    Args:
        app: FastAPI or Starlette application instance
        path: URL path where metrics will be mounted

    Raises:
        ExportError: If mounting fails
        TypeError: If app doesn't have mount() method

    Example:
        ```python
        from fastapi import FastAPI
        from mzn.metrics.exporters import mount_metrics

        app = FastAPI()
        await mount_metrics(app)  # Adds /metrics endpoint

        # Or with custom path
        await mount_metrics(app, path="/prometheus/metrics")
        ```
    """
    try:
        # Verify app has mount method (duck typing)
        if not hasattr(app, "mount"):
            msg = "app must have a mount() method (FastAPI/Starlette)"
            raise TypeError(msg)  # noqa: TRY301

        # Get our registry
        registry = await get_registry()

        # Create ASGI app using prometheus-client
        # Note: path parameter here is only for logging, actual routing
        # is handled by the ASGI framework's mount point
        metrics_app = make_asgi_app(registry=registry)  # pyright: ignore[reportUnknownVariableType]

        # Mount the metrics app
        app.mount(path, metrics_app)

        logger = await _get_logger()
        await logger.info(
            "Metrics endpoint mounted",
            path=path,
            app_type=type(app).__name__,
        )

    except Exception as e:
        error = Error.create(
            "metrics.mount_failed",
            message=f"Failed to mount metrics endpoint: {e}",
            path=path,
            error=str(e),
        )
        raise ExportError(error.context) from e


# --- Exports ------------------------------------------------------------------

__all__ = [
    "create_asgi_app",
    "mount_metrics",
    "serve",
]
