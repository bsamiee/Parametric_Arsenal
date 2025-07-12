"""
Title         : disk.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/cache/backends/disk.py

Description
-----------
Disk-based cache backend implementation using anyio for async file operations.
Simple but powerful design with proper TTL support and atomic operations.
"""

from __future__ import annotations

import contextlib
import hashlib
import json
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import TYPE_CHECKING, Annotated, Any

import anyio
from beartype import beartype

from mzn.cache.exceptions import CacheBackendError
from mzn.cache.serializers import Serializer
from mzn.cache.types import CacheKey, FilePath, SerializationFormat
from mzn.errors.namespace import Error


if TYPE_CHECKING:
    from collections.abc import Sequence


@beartype
class DiskBackend:
    """
    Disk-based cache backend with async file operations.

    Features:
    - Async file I/O using anyio
    - TTL support with metadata files
    - Atomic operations via temporary files
    - Configurable serialization
    - Simple directory structure: one file per key
    """

    def __init__(  # pyright: ignore[reportMissingSuperCall]
        self,
        cache_dir: Annotated[FilePath | str, "Cache directory path"],
        *,
        serialization: Annotated[SerializationFormat, "Serialization format"] = SerializationFormat.PICKLE,
    ) -> None:
        """
        Initialize disk backend.

        Args:
            cache_dir: Directory for cache storage
            serialization: Serialization format for values
        """
        self.cache_dir = Path(str(cache_dir))
        self.serialization = serialization
        self._serializer = self._get_serializer()
        self._initialized = False

    def _get_serializer(self) -> Serializer:
        """Get appropriate serializer based on format."""
        return Serializer(self.serialization)

    async def _ensure_initialized(self) -> None:
        """Ensure cache directory exists."""
        if not self._initialized:
            await anyio.Path(self.cache_dir).mkdir(parents=True, exist_ok=True)
            self._initialized = True

    def _get_paths(self, key: CacheKey) -> tuple[Path, Path]:
        """
        Get data and metadata file paths for a key.

        Returns:
            Tuple of (data_path, metadata_path)
        """
        # Use SHA256 hash of key to avoid filesystem issues
        key_hash = hashlib.sha256(str(key).encode()).hexdigest()

        # Store in subdirectories based on first 2 chars of hash for better performance
        subdir = self.cache_dir / key_hash[:2]
        data_path = subdir / f"{key_hash}.dat"
        meta_path = subdir / f"{key_hash}.meta"

        return data_path, meta_path

    @staticmethod
    async def _read_metadata(meta_path: Path) -> dict[str, Any] | None:
        """Read metadata file if it exists."""
        try:
            if await anyio.Path(meta_path).exists():
                content = await anyio.Path(meta_path).read_bytes()
                metadata: dict[str, Any] = json.loads(content.decode())
                return metadata
        except (json.JSONDecodeError, OSError):
            # If metadata is corrupted, treat as missing
            return None
        return None

    @staticmethod
    async def _write_atomic(path: Path, data: bytes) -> None:
        """Write data atomically using a temporary file."""
        # Ensure parent directory exists
        await anyio.Path(path.parent).mkdir(parents=True, exist_ok=True)

        # Write to temporary file first
        temp_path = path.with_suffix(".tmp")
        _ = await anyio.Path(temp_path).write_bytes(data)

        # Atomic rename
        _ = await anyio.Path(temp_path).rename(path)

    async def get(self, key: CacheKey) -> Any:
        """Get value by key, respecting TTL."""
        await self._ensure_initialized()

        data_path, meta_path = self._get_paths(key)

        try:
            # Check if files exist
            if not await anyio.Path(data_path).exists():
                return None

            # Read metadata to check TTL
            metadata = await self._read_metadata(meta_path)
            if metadata and metadata.get("ttl"):
                expires_at = datetime.fromisoformat(metadata["expires_at"])
                if datetime.now(UTC) > expires_at:
                    # Expired - clean up files
                    _ = await self.delete(key)
                    return None

            # Read and deserialize data
            data = await anyio.Path(data_path).read_bytes()
            return self._serializer.deserialize(data)

        except Exception as e:
            error = Error.create(
                "cache.disk_get_failed",
                message=f"Failed to get key '{key}' from disk",
                backend="disk",
                key=str(key),
                cache_dir=str(self.cache_dir),
            )
            raise CacheBackendError(error.context) from e

    async def set(self, key: CacheKey, value: Any, ttl: int | None = None) -> bool:
        """Set value with optional TTL."""
        await self._ensure_initialized()

        data_path, meta_path = self._get_paths(key)

        try:
            # Serialize value
            data = self._serializer.serialize(value)

            # Write data file atomically
            await self._write_atomic(data_path, data)

            # Write metadata if TTL is specified
            if ttl is not None:
                metadata = {
                    "ttl": ttl,
                    "created_at": datetime.now(UTC).isoformat(),
                    "expires_at": (datetime.now(UTC) + timedelta(seconds=ttl)).isoformat(),
                    "key": str(key),
                }
                meta_data = json.dumps(metadata).encode()
                await self._write_atomic(meta_path, meta_data)
            else:
                # Remove metadata file if no TTL
                with contextlib.suppress(FileNotFoundError):
                    await anyio.Path(meta_path).unlink()

            return True  # noqa: TRY300

        except Exception as e:
            error = Error.create(
                "cache.disk_set_failed",
                message=f"Failed to set key '{key}' to disk",
                backend="disk",
                key=str(key),
                cache_dir=str(self.cache_dir),
                ttl=ttl,
            )
            raise CacheBackendError(error.context) from e

    async def delete(self, key: CacheKey) -> bool:
        """Delete key from cache."""
        await self._ensure_initialized()

        data_path, meta_path = self._get_paths(key)
        deleted = False

        try:
            # Delete data file
            if await anyio.Path(data_path).exists():
                await anyio.Path(data_path).unlink()
                deleted = True

            # Delete metadata file
            if await anyio.Path(meta_path).exists():
                await anyio.Path(meta_path).unlink()

            return deleted  # noqa: TRY300

        except Exception as e:
            error = Error.create(
                "cache.disk_delete_failed",
                message=f"Failed to delete key '{key}' from disk",
                backend="disk",
                key=str(key),
                cache_dir=str(self.cache_dir),
            )
            raise CacheBackendError(error.context) from e

    async def exists(self, key: CacheKey) -> bool:
        """Check if key exists and is not expired."""
        value = await self.get(key)
        return value is not None

    async def clear(self) -> None:
        """Clear all entries."""
        await self._ensure_initialized()

        try:
            # Remove all subdirectories
            async for item in anyio.Path(self.cache_dir).iterdir():
                if await item.is_dir():
                    # Remove all files in subdirectory
                    async for file in item.iterdir():
                        await file.unlink()
                    # Remove empty subdirectory
                    await item.rmdir()

        except Exception as e:
            error = Error.create(
                "cache.disk_clear_failed",
                message="Failed to clear disk cache",
                backend="disk",
                cache_dir=str(self.cache_dir),
            )
            raise CacheBackendError(error.context) from e

    async def get_many(self, keys: Sequence[CacheKey]) -> list[Any]:
        """Get multiple values at once."""
        return [await self.get(key) for key in keys]

    async def set_many(self, items: list[tuple[CacheKey, Any]], ttl: int | None = None) -> bool:
        """Set multiple values at once."""
        results = [await self.set(key, value, ttl) for key, value in items]
        return all(results)

    async def close(self) -> None:
        """Close backend connections - no-op for disk backend."""
        # No cleanup needed for disk backend
