"""
Title         : norm_data.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_data.py

Description
-----------
Normalizers for data serialization and deserialization.
"""

from __future__ import annotations

import base64
import bz2
import gzip
import json
import lzma
import zlib
from typing import TYPE_CHECKING, TypeVar

import magic
import yaml
from pydantic import BaseModel, TypeAdapter, ValidationError

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer

# --- Type Variables and Protocols ---------------------------------------------

T = TypeVar("T")

# --- Normalizers --------------------------------------------------------------


@Build.normalizer(
    register_as=NORM.DATA.to_json,
    description="Converts a Python object into a JSON formatted string.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_json(value: object, info: ValidationInfo) -> str:
    """Converts a Python object into a JSON formatted string."""
    if isinstance(value, BaseModel):
        return value.model_dump_json()
    return json.dumps(value)


def from_json(
    *, target_type: type[T] | TypeAdapter[T] | None = None
) -> Normalizer[str, T]:
    """Factory for a normalizer that parses a JSON string into a Python object."""

    @Build.normalizer(
        description="Parses a JSON string into a Python object.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> T:
        data = json.loads(value)
        if target_type:
            adapter = (
                target_type
                if isinstance(target_type, TypeAdapter)
                else TypeAdapter(target_type)
            )
            return adapter.validate_python(data)
        msg = "target_type must be provided to ensure return type T"
        raise ValueError(msg)

    return _normalizer


@Build.normalizer(
    register_as=NORM.DATA.to_yaml,
    description="Converts a Python object to a YAML string.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_yaml(value: object, info: ValidationInfo) -> str:
    """Converts a Python object to a YAML string."""
    if isinstance(value, BaseModel):
        value = value.model_dump()
    return yaml.dump(value)


def from_yaml(
    *, target_type: type[T] | TypeAdapter[T] | None = None
) -> Normalizer[str, T]:
    """Factory for a normalizer that parses a YAML string into a Python object."""

    @Build.normalizer(
        description="Parses a YAML string into a Python object.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> T:
        try:
            data = yaml.safe_load(value)
            if target_type:
                adapter = (
                    target_type
                    if isinstance(target_type, TypeAdapter)
                    else TypeAdapter(target_type)
                )
                return adapter.validate_python(data)
            msg = "target_type must be provided to ensure return type T"
            raise ValueError(msg)
        except (yaml.YAMLError, ValidationError) as exc:
            error_msg = "Failed to parse YAML or validate data"
            raise ValueError(error_msg) from exc

    return _normalizer


@Build.normalizer(
    register_as=NORM.DATA.to_base64,
    description="Encodes bytes into a Base64 string.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_base64(value: bytes, info: ValidationInfo) -> str:
    """Encodes bytes into a Base64 string."""
    return base64.b64encode(value).decode("ascii")


@Build.normalizer(
    register_as=NORM.DATA.from_base64,
    description="Decodes a Base64 string into bytes.",
    tags=(SYSTEM.INFRA.io,),
)
async def from_base64(value: str, info: ValidationInfo) -> bytes | str:
    """
    Decodes a Base64 string into bytes.

    Returns the original string if decoding fails.
    """
    try:
        return base64.b64decode(value)
    except (ValueError, TypeError):
        return value


# Binary data normalizers
def add_prefix(*, prefix: bytes) -> Normalizer[bytes, bytes]:
    """Factory for creating a prefix-adding normalizer."""

    @Build.normalizer(
        description=f"Add prefix: {prefix!r}",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: bytes, info: ValidationInfo) -> bytes:
        """Add prefix to binary data if not already present."""
        if value.startswith(prefix):
            return value

        return prefix + value

    return _normalizer


def add_suffix(*, suffix: bytes) -> Normalizer[bytes, bytes]:
    """Factory for creating a suffix-adding normalizer."""

    @Build.normalizer(
        description=f"Add suffix: {suffix!r}",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: bytes, info: ValidationInfo) -> bytes:
        """Add suffix to binary data if not already present."""
        if value.endswith(suffix):
            return value

        return value + suffix

    return _normalizer


def compress_if_large(
    *, threshold: int = 4096, algorithm: str = "gzip", level: int = 6
) -> Normalizer[bytes, bytes]:
    """Factory for creating a conditional compression normalizer."""

    @Build.normalizer(
        description=f"Compress if larger than {threshold} bytes using {algorithm}",
        tags=(SYSTEM.INFRA.io, SYSTEM.PERFORMANCE),
    )
    async def _normalizer(value: bytes, info: ValidationInfo) -> bytes:
        """Compress binary data if it exceeds threshold."""
        if len(value) <= threshold:
            return value

        # Store original size in context for validators
        if info.context is not None:
            info.context["original_size"] = len(value)

        # Compress based on algorithm
        if algorithm == "gzip":
            return gzip.compress(value, compresslevel=level)
        if algorithm == "zlib":
            return zlib.compress(value, level=level)
        if algorithm == "bzip2":
            return bz2.compress(value, compresslevel=level)
        if algorithm == "lzma":
            return lzma.compress(value, preset=level)
        # Unknown algorithm, return uncompressed
        return value

    return _normalizer


def add_compression_header(*, header_format: str = "MZN_COMPRESSED:{algorithm}:") -> Normalizer[bytes, bytes]:
    """Factory for creating a compression header normalizer."""

    @Build.normalizer(
        description="Add compression header to data",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: bytes, info: ValidationInfo) -> bytes:
        """Add compression header to binary data."""
        # Get algorithm from context
        algorithm = (info.context.get("compression_algorithm", "unknown")
                     if info.context is not None else "unknown")

        # Format header
        header = header_format.format(algorithm=algorithm).encode()

        # Don't add if already present
        if value.startswith(header):
            return value

        return header + value

    return _normalizer


# --- New Normalizers Using python-magic --------------------------------------

@Build.normalizer(
    register_as=NORM.DATA.detect_mime_type,
    description="Detect and return the MIME type of binary data.",
    tags=(SYSTEM.INFRA.io,),
)
async def detect_mime_type(value: bytes, info: ValidationInfo) -> str:
    """
    Detect and return the MIME type of binary data using python-magic.

    Falls back to 'application/octet-stream' if detection fails.
    """
    try:
        mime_type = magic.from_buffer(value, mime=True)

        # Store in context for other rules
        if info.context is not None:
            info.context["detected_mime_type"] = mime_type

    except (OSError, ValueError, RuntimeError):
        # Fallback to generic binary type
        return "application/octet-stream"
    else:
        return mime_type


@Build.normalizer(
    register_as=NORM.DATA.detect_file_type,
    description="Detect and return a human-readable file type description.",
    tags=(SYSTEM.INFRA.io,),
)
async def detect_file_type(value: bytes, info: ValidationInfo) -> str:
    """
    Detect and return a human-readable file type description.

    Uses python-magic to get detailed file information.
    """
    try:
        file_type = magic.from_buffer(value)

        # Store in context
        if info.context is not None:
            info.context["detected_file_type"] = file_type

    except (OSError, ValueError, RuntimeError):
        # Fallback detection based on magic bytes - consolidate returns
        return _detect_file_type_fallback(value)
    else:
        return file_type


def _detect_file_type_fallback(value: bytes) -> str:
    """Fallback file type detection using magic bytes."""
    # Dictionary to reduce complexity
    signatures = {
        b"\x89PNG": "PNG image data",
        b"\xff\xd8\xff": "JPEG image data",
        b"GIF8": "GIF image data",
        b"%PDF": "PDF document",
        b"PK\x03\x04": "ZIP archive data",
        b"\x1f\x8b": "gzip compressed data",
    }

    for sig, description in signatures.items():
        if value.startswith(sig):
            return description
    return "data"


def extract_file_metadata(
    *, include_mime: bool = True, include_encoding: bool = True
) -> Normalizer[bytes, dict[str, str | int | bool]]:
    """
    Factory for creating a file metadata extraction normalizer.

    Args:
        include_mime: Whether to include MIME type in metadata
        include_encoding: Whether to attempt encoding detection
    """
    @Build.normalizer(
        description="Extract file type metadata into a dictionary.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: bytes, info: ValidationInfo) -> dict[str, str | int | bool]:
        """Extract comprehensive file metadata."""
        metadata: dict[str, str | int | bool] = {
            "size": len(value),
        }

        try:
            if include_mime:
                metadata["mime_type"] = magic.from_buffer(value, mime=True)

            metadata["description"] = magic.from_buffer(value)

        except (OSError, ValueError, RuntimeError):
            # Fallback
            metadata["description"] = "Unknown file type"
            if include_mime:
                metadata["mime_type"] = "application/octet-stream"

        if include_encoding:
            # Try to detect text encoding
            try:
                _ = value.decode("utf-8")
                metadata["encoding"] = "utf-8"
                metadata["is_text"] = True
            except UnicodeDecodeError:
                try:
                    _ = value.decode("latin-1")
                    metadata["encoding"] = "latin-1"
                    metadata["is_text"] = True
                except UnicodeDecodeError:
                    metadata["is_text"] = False

        # Store in context
        if info.context is not None:
            info.context["file_metadata"] = metadata

        return metadata

    return _normalizer
