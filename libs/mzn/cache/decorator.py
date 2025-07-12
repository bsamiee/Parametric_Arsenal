"""
Title         : decorator.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/cache/decorator.py

Description
-----------
Simple cache decorator for function memoization.
"""

from __future__ import annotations

import hashlib
import inspect
from functools import wraps
from typing import TYPE_CHECKING, Annotated, Any, TypeVar, cast

from beartype import beartype

from mzn.cache.types import CacheKey, CacheTTL


if TYPE_CHECKING:
    from collections.abc import Awaitable, Callable
    from datetime import timedelta

    from mzn.cache.core import Cache

# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")

# --- Decorator ----------------------------------------------------------------


@beartype
def cached(
    cache: Annotated[Cache | None, "Cache instance to use"] = None,
    *,
    ttl: Annotated[CacheTTL | timedelta | int | None, "Time to live"] = None,
    key_prefix: Annotated[str | None, "Prefix for cache keys"] = None,
    tags: Annotated[list[str] | None, "Tags for cached values"] = None,
) -> Callable[[Callable[..., Awaitable[T]]], Callable[..., Awaitable[T]]]:
    """Cache async function results with automatic key generation.

    Args:
        cache: Cache instance (if None, uses self.cache from class)
        ttl: Time to live in seconds
        key_prefix: Custom prefix for cache keys
        tags: Tags for grouped invalidation

    Returns:
        Decorated function that caches results

    Example:
        @cached(cache_instance, ttl=300)
        async def expensive_function(x: int) -> str:
            return await slow_operation(x)
    """
    def decorator(func: Callable[..., Awaitable[T]]) -> Callable[..., Awaitable[T]]:
        # Get function signature for key generation
        sig = inspect.signature(func)

        @wraps(func)
        async def wrapper(*args: Any, **kwargs: Any) -> T:
            # Get cache instance
            cache_instance = cache
            if cache_instance is None:
                # Try to get from self if this is a method
                if args and hasattr(args[0], "cache"):
                    cache_instance = args[0].cache
                else:
                    msg = "No cache instance provided or found"
                    raise ValueError(msg)

            # Generate cache key
            cache_key = _generate_cache_key(func, args, kwargs, key_prefix, sig)

            # Try to get from cache
            cached_value = await cache_instance.get(cache_key)
            if cached_value is not None:
                return cast("T", cached_value)

            # Call function and cache result
            result = await func(*args, **kwargs)
            _ = await cache_instance.set(cache_key, result, ttl=ttl, tags=tags)

            return result

        # Store metadata on wrapper
        wrapper.cached = True  # type: ignore[attr-defined]
        wrapper.cache_config = {  # type: ignore[attr-defined]
            "ttl": ttl,
            "key_prefix": key_prefix,
            "tags": tags,
        }

        return wrapper

    return decorator

# --- Generate Cache Key -------------------------------------------------------


def _generate_cache_key(
    func: Callable[..., Any],
    args: tuple[Any, ...],
    kwargs: dict[str, Any],
    prefix: str | None,
    sig: inspect.Signature,
) -> CacheKey:
    """Generate a cache key for function call."""
    # Build key components
    components: list[str] = []

    # Add prefix if provided
    if prefix:
        components.append(prefix)
    else:
        # Use module and function name
        components.extend([
            func.__module__.replace(".", "_"),
            func.__qualname__.replace(".", "_")
        ])

    # Bind arguments to get normalized view
    try:
        bound = sig.bind(*args, **kwargs)
        bound.apply_defaults()

        # Create stable string representation
        arg_parts: list[str] = []
        for name, value in bound.arguments.items():
            # Skip 'self' for methods
            if name == "self":
                continue
            arg_parts.append(f"{name}={_serialize_arg(value)}")

        if arg_parts:
            components.append("_".join(arg_parts))
    except Exception:  # noqa: BLE001  # Need to catch all binding errors
        # Fallback to hash if binding fails
        hasher = hashlib.sha256()
        hasher.update(str(args).encode())
        hasher.update(str(sorted(kwargs.items())).encode())
        components.append(hasher.hexdigest()[:16])

    return CacheKey("_".join(components))

# --- Serialize Arguments ------------------------------------------------------


def _serialize_arg(value: Any) -> str:
    """Serialize an argument to a stable string representation."""
    if value is None:
        return "None"
    if isinstance(value, (str, int, float, bool)):
        return str(value)
    if isinstance(value, (list, tuple)):
        return f"[{','.join(_serialize_arg(v) for v in value)}]"  # pyright: ignore[reportUnknownVariableType]
    if isinstance(value, dict):
        items = sorted(value.items())  # pyright: ignore[reportUnknownVariableType,reportUnknownArgumentType]
        return f"{{{','.join(f'{k}:{_serialize_arg(v)}' for k, v in items)}}}"  # pyright: ignore[reportUnknownVariableType]
    if hasattr(value, "__dict__"):
        # For objects, try to use their id or hash
        return f"{value.__class__.__name__}_{id(value)}"
    # Fallback to string representation
    return str(value)
