
from typing import Any

class ConnectionPool:

    max_connections: int
    _created_connections: list[Any]
    _available_connections: list[Any]
    _in_use_connections: set[Any]

    @classmethod
    def from_url(
        cls,
        url: str,
        *,
        max_connections: int = 50,
        socket_connect_timeout: float | None = None,
        socket_timeout: float | None = None,
        retry_on_timeout: bool = False,
        socket_keepalive: bool = False,
        decode_responses: bool = True,
        health_check_interval: int = 0,
        socket_keepalive_options: dict[int, int] | None = None,
        **kwargs: Any,
    ) -> ConnectionPool: ...

    async def disconnect(self) -> None: ...
