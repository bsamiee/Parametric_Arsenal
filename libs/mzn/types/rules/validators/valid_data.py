"""
Title         : valid_data.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/types/rules/validators/valid_data.py.

Description ----------- Validators for structured data formats and payloads.

"""

from __future__ import annotations

import base64
import binascii
import csv
import json
from io import StringIO
from typing import TYPE_CHECKING, Any

import magic as pymagic
import toml
import yaml
from jsonschema import ValidationError as JsonSchemaValidationError, validate
from lxml import etree
from pydantic import BaseModel, ValidationError

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


@Build.validator(
    register_as=VALID.DATA.is_json,
    error_template="Value must be valid JSON.",
    description="Checks if a string is valid JSON.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_json(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is valid JSON."""
    try:
        json.loads(value)
    except json.JSONDecodeError:
        return False
    return True


@Build.validator(
    register_as=VALID.DATA.is_yaml,
    error_template="Value must be valid YAML.",
    description="Checks if a string is valid YAML.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_yaml(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is valid YAML."""
    try:
        _ = yaml.safe_load(value)
    except yaml.YAMLError:
        return False
    return True


@Build.validator(
    register_as=VALID.DATA.is_toml,
    error_template="Value must be valid TOML.",
    description="Checks if a string is valid TOML.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_toml(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is valid TOML."""
    try:
        _ = toml.loads(value)
    except toml.TomlDecodeError:
        return False
    return True


def is_csv_row(*, num_columns: int) -> Validator[str]:
    """Factory for a validator that checks if a string is a valid CSV row."""
    error_template = f"Value must be a CSV row with {num_columns} columns."

    @Build.validator(
        error_template=error_template,
        description="Checks if a string is a valid CSV row with a specific number of columns.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        reader = csv.reader(StringIO(value))
        try:
            row = next(reader)
            return len(row) == num_columns
        except StopIteration:
            return False

    return _validator


def has_schema(*, schema: type[BaseModel] | dict[str, Any]) -> Validator[dict[str, Any]]:
    """Factory for a validator that checks if a payload conforms to a schema."""
    @Build.validator(
        error_template="Payload must conform to the specified schema.",
        description="Checks if a dictionary payload conforms to a Pydantic model or JSON Schema.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: dict[str, Any], info: ValidationInfo) -> bool:
        try:
            if isinstance(schema, dict):
                validate(instance=value, schema=schema)
            else:
                pydantic_model: type[BaseModel] = schema
                _ = pydantic_model.model_validate(value)
        except (ValidationError, JsonSchemaValidationError):
            return False
        return True

    return _validator


@Build.validator(
    register_as=VALID.DATA.is_base64,
    error_template="Value must be a valid Base64 string.",
    description="Checks if a string is a valid Base64 encoded string.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_base64(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is a valid Base64 encoded string."""
    try:
        _ = base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error):
        return False
    return True


@Build.validator(
    register_as=VALID.DATA.is_hex_encoded,
    error_template="Value must be a valid hex-encoded string.",
    description="Checks if a string is a valid hex-encoded string.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_hex_encoded(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is a valid hex-encoded string."""
    try:
        _ = bytes.fromhex(value)
    except ValueError:
        return False
    return True


@Build.validator(
    register_as=VALID.DATA.is_xml_well_formed,
    error_template="Value must be well-formed XML.",
    description="Checks if a string is well-formed XML.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_xml_well_formed(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is well-formed XML."""
    try:
        _ = etree.fromstring(value.encode("utf-8"))
    except etree.XMLSyntaxError:
        return False
    return True


# Binary data validators
def has_prefix(*, prefix: bytes) -> Validator[bytes]:
    """Factory for creating a prefix validator for binary data."""

    @Build.validator(
        error_template=f"Binary data missing required prefix: {prefix!r}",
        description=f"Check for prefix: {prefix!r}",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        """Check if binary data starts with specified prefix."""
        return value.startswith(prefix)

    return _validator


def has_suffix(*, suffix: bytes) -> Validator[bytes]:
    """Factory for creating a suffix validator for binary data."""

    @Build.validator(
        error_template=f"Binary data missing required suffix: {suffix!r}",
        description=f"Check for suffix: {suffix!r}",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        """Check if binary data ends with specified suffix."""
        return value.endswith(suffix)

    return _validator


def is_compressed_format(*, formats: list[str] | None = None) -> Validator[bytes]:
    """Factory for creating a compressed format validator."""
    # Default compression format magic bytes
    default_formats = {
        "gzip": b"\x1f\x8b",
        "zlib": b"\x78\x9c",
        "bzip2": b"BZ",
        "lz4": b"\x04\x22\x4d\x18",
        "lzma": b"\xfd\x37\x7a\x58\x5a",
        "zip": b"PK\x03\x04",
    }

    if formats:
        magic_bytes = {fmt: default_formats.get(fmt, b"") for fmt in formats if fmt in default_formats}
    else:
        magic_bytes = default_formats

    @Build.validator(
        error_template="Binary data is not in a recognized compressed format",
        description="Check if binary data is compressed",
        tags=(SYSTEM.INFRA.io, SYSTEM.PERFORMANCE),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        """Check if binary data appears to be compressed."""
        if not value:
            return False

        # Check magic bytes for known formats
        for fmt, magic in magic_bytes.items():
            if magic and value.startswith(magic):
                if info.context is not None:
                    info.context["detected_format"] = fmt
                return True

        # Use python-magic as fallback for better detection
        try:
            mime_type = pymagic.from_buffer(value, mime=True)
            compressed_mimes = {
                "application/gzip", "application/x-gzip",
                "application/zip", "application/x-zip-compressed",
                "application/x-bzip2", "application/x-bzip",
                "application/x-lzma", "application/x-xz",
                "application/x-7z-compressed",
                "application/x-rar-compressed",
            }
            if mime_type in compressed_mimes:
                if info.context is not None:
                    info.context["detected_format"] = mime_type.split("/")[-1].replace("x-", "")
                return True
        except (OSError, ValueError):
            # python-magic detection failed
            pass

        return False

    return _validator


def is_within_size_range(*, min_size: int = 0, max_size: int = 10 * 1024 * 1024) -> Validator[bytes]:
    """Factory for creating a size range validator."""

    @Build.validator(
        error_template="Binary data size {size} is outside range [{min_size}, {max_size}]",
        description=f"Check size is between {min_size} and {max_size} bytes",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        """Check if binary data size is within specified range."""
        size = len(value)
        if info.context is not None:
            info.context["size"] = size
            info.context["min_size"] = min_size
            info.context["max_size"] = max_size

        return min_size <= size <= max_size

    return _validator


def compression_ratio_acceptable(*, min_ratio: float = 0.1, original_size: int | None = None) -> Validator[bytes]:
    """Factory for creating a compression ratio validator."""

    @Build.validator(
        error_template="Compression ratio {ratio:.2f} is below minimum {min_ratio}",
        description=f"Check compression ratio is at least {min_ratio}",
        tags=(SYSTEM.INFRA.io, SYSTEM.PERFORMANCE),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        """Check if compression ratio is acceptable."""
        compressed_size = len(value)

        # Get original size from context or parameter
        orig_size = (info.context.get("original_size", original_size)
                     if info.context is not None else original_size)
        if not orig_size:
            # Can't calculate ratio without original size
            return True

        ratio = 1.0 - (compressed_size / orig_size)
        if info.context is not None:
            info.context["ratio"] = ratio
            info.context["min_ratio"] = min_ratio

        return ratio >= min_ratio

    return _validator


# --- New Validators Using python-magic ----------------------------------------

@Build.validator(
    register_as=VALID.DATA.is_archive_format,
    error_template="Binary data is not a recognized archive format.",
    description="Check if binary data is an archive (zip, tar, etc.).",
    tags=(SYSTEM.INFRA.io,),
)
async def is_archive_format(value: bytes, info: ValidationInfo) -> bool:
    """
    Check if binary data is an archive format (zip, tar, rar, etc.).

    Uses both magic bytes and python-magic for comprehensive detection.

    """
    if not value or len(value) < 4:
        return False

    # Check common archive magic bytes
    archive_signatures = {
        "zip": b"PK\x03\x04",
        "zip_empty": b"PK\x05\x06",
        "zip_spanned": b"PK\x07\x08",
        "tar": b"ustar",  # at offset 257
        "rar": b"Rar!",
        "7z": b"7z\xbc\xaf\x27\x1c",
        "cab": b"MSCF",
        "ar": b"!<arch>",
        "lha": b"-lh",
        "iso": b"CD001",  # at offset 32769
    }

    # Check direct signatures
    for fmt, sig in archive_signatures.items():
        found = False
        if fmt == "tar" and len(value) > 262:
            found = value[257:262] == sig
        elif fmt == "iso" and len(value) > 32774:
            found = value[32769:32774] == sig
        else:
            found = value.startswith(sig)

        if found:
            if info.context is not None:
                info.context["archive_format"] = fmt
            return True

    # Try python-magic for more comprehensive detection
    try:
        mime_type = pymagic.from_buffer(value, mime=True)
        archive_mimes = {
            "application/zip", "application/x-zip-compressed",
            "application/x-tar", "application/x-gtar",
            "application/x-rar-compressed", "application/vnd.rar",
            "application/x-7z-compressed",
            "application/x-iso9660-image",
            "application/x-archive", "application/x-cpio",
            "application/x-debian-package", "application/x-rpm",
            "application/java-archive",
        }

        if mime_type in archive_mimes:
            if info.context is not None:
                info.context["archive_format"] = mime_type.split("/")[-1].replace("x-", "").replace("-compressed", "")
            return True

    except (OSError, ValueError, RuntimeError):
        # python-magic detection failed
        pass

    return False


def has_file_signature(*, signatures: dict[str, bytes] | None = None) -> Validator[bytes]:
    """
    Factory for creating a generic file signature validator.

    Args:     signatures: Dict mapping format names to their magic byte signatures.                If None, uses python-
    magic for detection.

    """
    @Build.validator(
        error_template="Binary data does not match any expected file signature.",
        description="Check if data has a valid file signature.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        """Check if binary data has a recognized file signature."""
        if not value:
            return False

        # If specific signatures provided, check them
        if signatures:
            for fmt, sig in signatures.items():
                if value.startswith(sig):
                    if info.context is not None:
                        info.context["detected_signature"] = fmt
                    return True
            return False

        # Otherwise use python-magic for generic detection
        try:
            detected = pymagic.from_buffer(value)
            # If magic returns something other than 'data', it recognized the format
            if detected and detected.lower() != "data":
                if info.context is not None:
                    info.context["detected_signature"] = detected
                return True
        except (OSError, ValueError):
            # Fallback: check for any common signatures
            common_sigs = [
                b"\xff\xd8\xff",  # JPEG
                b"\x89PNG",       # PNG
                b"GIF8",          # GIF
                b"%PDF",          # PDF
                b"PK",            # ZIP-based
                b"\x1f\x8b",      # GZIP
                b"BM",            # BMP
                b"ID3",           # MP3
                b"RIFF",          # WAV/AVI
            ]
            return any(value.startswith(sig) for sig in common_sigs)

        return False

    return _validator
