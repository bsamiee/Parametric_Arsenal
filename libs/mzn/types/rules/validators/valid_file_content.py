"""
Title         : valid_file_content.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       :
MIT Path          : libs/mzn/types/rules/validators/valid_file_content.py.

Description ----------- File content validation rules.

"""

from __future__ import annotations

import math
from pathlib import Path
from typing import TYPE_CHECKING

import magic

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


def has_valid_mime_type(*, mime_type: str) -> Validator[bytes]:
    """Factory for a validator that checks if a file's content has a valid MIME type."""
    @Build.validator(
        error_template=f"File content must have MIME type {mime_type}.",
        description="Checks if a file's content has a valid MIME type.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:

        try:
            detected_mime = magic.from_buffer(value, mime=True)
        except (OSError, ValueError):
            return False

        return detected_mime == mime_type

    return _validator


def has_valid_encoding(*, encoding: str) -> Validator[bytes]:
    """Factory for a validator that checks if a file's content has a valid encoding."""
    @Build.validator(
        error_template=f"File content must have encoding {encoding}.",
        description="Checks if a file's content has a valid encoding.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        # First try python-magic
        try:
            detected = magic.from_buffer(value)
            # Check if the detected type mentions the encoding
            if encoding.lower() in detected.lower():
                return True
        except (OSError, ValueError):
            # magic detection failed, continue to fallback
            pass

        # Fallback to decode test
        try:
            _ = value.decode(encoding)
        except UnicodeDecodeError:
            return False
        return True

    return _validator


# --- New Validators Using python-magic ----------------------------------------

@Build.validator(
    register_as=VALID.FILESYSTEM.is_text_file,
    error_template="File content must be text, not binary.",
    description="Checks if file content is text (not binary).",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_text_file(value: bytes, info: ValidationInfo) -> bool:
    """
    Check if file content is text rather than binary.

    Uses python-magic to detect MIME type and checks if it's a text type.

    """
    try:
        mime_type = magic.from_buffer(value, mime=True)
        # Text files typically have mime types starting with 'text/'
        # or are common text formats
        text_mimes = {
            "application/json", "application/xml", "application/javascript",
            "application/x-yaml", "application/toml", "application/x-sh",
            "application/x-python", "application/x-ruby", "application/x-perl"
        }
        return mime_type.startswith("text/") or mime_type in text_mimes
    except (OSError, ValueError, RuntimeError):
        return False


@Build.validator(
    register_as=VALID.FILESYSTEM.is_binary_file,
    error_template="File content must be binary, not text.",
    description="Checks if file content is binary.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_binary_file(value: bytes, info: ValidationInfo) -> bool:
    """
    Check if file content is binary rather than text.

    This is the inverse of is_text_file.

    """
    return not await is_text_file(value, info)


@Build.validator(
    register_as=VALID.FILESYSTEM.is_media_file,
    error_template="File content must be a media file (image, audio, or video).",
    description="Checks if file is image, audio, or video.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_media_file(value: bytes, info: ValidationInfo) -> bool:
    """Check if file content is a media file (image, audio, or video)."""
    try:
        mime_type = magic.from_buffer(value, mime=True)
        return (mime_type.startswith(("image/", "audio/", "video/")))
    except (OSError, ValueError, RuntimeError):
        return False


@Build.validator(
    register_as=VALID.FILESYSTEM.is_document_file,
    error_template="File content must be a document file.",
    description="Checks if file is a document (PDF, Word, etc.).",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_document_file(value: bytes, info: ValidationInfo) -> bool:
    """Check if file content is a document file (PDF, Word, Excel, etc.)."""
    try:
        mime_type = magic.from_buffer(value, mime=True)
        document_mimes = {
            "application/pdf",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/vnd.oasis.opendocument.text",
            "application/vnd.oasis.opendocument.spreadsheet",
            "application/vnd.oasis.opendocument.presentation",
            "application/rtf",
            "text/plain",
            "text/markdown",
            "text/html",
        }
    except (OSError, ValueError, RuntimeError):
        return False

    return mime_type in document_mimes


def matches_extension(*, path: str | Path) -> Validator[bytes]:
    """Factory for a validator that checks if content matches file extension."""
    @Build.validator(
        error_template="File content does not match extension {extension}.",
        description="Validates content matches the file extension.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _validator(value: bytes, info: ValidationInfo) -> bool:
        """Check if file content matches the expected type for the extension."""
        file_path = Path(path)
        extension = file_path.suffix.lower()

        if not extension:
            return True  # No extension to validate against

        try:
            mime_type = magic.from_buffer(value, mime=True)

            # Common extension to MIME type mappings
            ext_to_mime = {
                ".txt": ["text/plain"],
                ".html": ["text/html"],
                ".htm": ["text/html"],
                ".css": ["text/css"],
                ".js": ["application/javascript", "text/javascript"],
                ".json": ["application/json"],
                ".xml": ["application/xml", "text/xml"],
                ".pdf": ["application/pdf"],
                ".doc": ["application/msword"],
                ".docx": ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
                ".xls": ["application/vnd.ms-excel"],
                ".xlsx": ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
                ".ppt": ["application/vnd.ms-powerpoint"],
                ".pptx": ["application/vnd.openxmlformats-officedocument.presentationml.presentation"],
                ".jpg": ["image/jpeg"],
                ".jpeg": ["image/jpeg"],
                ".png": ["image/png"],
                ".gif": ["image/gif"],
                ".bmp": ["image/bmp", "image/x-ms-bmp"],
                ".svg": ["image/svg+xml"],
                ".mp3": ["audio/mpeg"],
                ".wav": ["audio/wav", "audio/x-wav"],
                ".mp4": ["video/mp4"],
                ".avi": ["video/x-msvideo"],
                ".zip": ["application/zip"],
                ".gz": ["application/gzip"],
                ".tar": ["application/x-tar"],
                ".7z": ["application/x-7z-compressed"],
                ".rar": ["application/x-rar-compressed"],
            }

            expected_mimes = ext_to_mime.get(extension, [])
            if not expected_mimes:
                return True  # Unknown extension, can't validate

        except (OSError, ValueError):
            return False

        return mime_type in expected_mimes

    return _validator


@Build.validator(
    register_as=VALID.FILESYSTEM.is_encrypted_file,
    error_template="File appears to be encrypted or password-protected.",
    description="Checks if file appears to be encrypted.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_encrypted_file(value: bytes, info: ValidationInfo) -> bool:
    """
    Check if file content appears to be encrypted or password-protected.

    This checks for common encrypted file signatures and high entropy.

    """
    if len(value) < 16:
        return False

    # Check for common encrypted file signatures
    encrypted_signatures = [
        b"Salted__",  # OpenSSL encrypted
        b"\x50\x4b\x03\x04",  # PKZip encrypted (when combined with encryption flag)
        b"GPG",  # GPG encrypted
        b"-----BEGIN PGP MESSAGE-----",  # PGP ASCII armor
    ]

    for sig in encrypted_signatures:
        if value.startswith(sig):
            return True

    # Check for high entropy (common in encrypted data)
    # Simple byte frequency analysis
    if len(value) >= 256:
        byte_counts = [0] * 256
        for byte in value[:1024]:  # Sample first 1KB
            byte_counts[byte] += 1

        # Calculate simple entropy metric
        total = sum(byte_counts)
        entropy = 0.0
        for count in byte_counts:
            if count > 0:
                freq = count / total
                entropy -= freq * math.log2(freq) if freq > 0 else 0

        # High entropy suggests encryption
        entropy_threshold = 7.5
        if entropy > entropy_threshold:
            return True

    return False
