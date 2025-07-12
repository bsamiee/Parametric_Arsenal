"""
Title         : serializers.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/cache/serializers.py.

Description ----------- Simple serialization utilities for cache backends.

"""

from __future__ import annotations

import pickle  # noqa: S403  # Pickle is required for caching arbitrary Python objects
from typing import Any

import orjson
from beartype import beartype

from mzn.cache.exceptions import CacheSerializationError
from mzn.cache.types import SerializationFormat
from mzn.errors.namespace import Error


@beartype
class Serializer:
    """Simple serializer that backends can use."""

    def __init__(self, fmt: SerializationFormat) -> None:  # pyright: ignore[reportMissingSuperCall]
        """Initialize with serialization format."""
        self.format = fmt

    def serialize(self, value: Any) -> bytes:
        """Serialize value to bytes."""
        try:
            match self.format:
                case SerializationFormat.JSON:
                    return orjson.dumps(value)
                case SerializationFormat.PICKLE:
                    return pickle.dumps(value)
                case _:
                    # Default to pickle for reliability
                    return pickle.dumps(value)
        except Exception as e:
            error = Error.create(
                "cache.serialization_failed",
                message=f"Failed to serialize value with {self.format.value}",
                format=self.format.value,
                value_type=type(value).__name__,
            )
            raise CacheSerializationError(error.context) from e

    def deserialize(self, data: bytes) -> Any:
        """Deserialize bytes to value."""
        try:
            match self.format:
                case SerializationFormat.JSON:
                    return orjson.loads(data)
                case SerializationFormat.PICKLE:
                    return pickle.loads(data)  # noqa: S301  # Trusted cache data
                case _:
                    # Default to pickle
                    return pickle.loads(data)  # noqa: S301  # Trusted cache data
        except Exception as e:
            error = Error.create(
                "cache.deserialization_failed",
                message=f"Failed to deserialize data with {self.format.value}",
                format=self.format.value,
                data_size=len(data),
            )
            raise CacheSerializationError(error.context) from e
