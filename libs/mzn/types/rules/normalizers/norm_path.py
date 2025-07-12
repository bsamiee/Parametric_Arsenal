"""
Title         : norm_path.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_path.py

Description
-----------
Path normalization rules.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import TYPE_CHECKING

import magic

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


@Build.normalizer(
    register_as=NORM.FILESYSTEM.resolve_path,
    description="Converts the path to an absolute path, resolving any symlinks.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def resolve_path(value: str | Path, info: ValidationInfo) -> str:
    """Converts the path to an absolute path, resolving any symlinks."""
    return str(Path(value).resolve())


@Build.normalizer(
    register_as=NORM.FILESYSTEM.normalize_path,
    description="Cleans up the path (e.g., A//B, A/./B, A/foo/../B).",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def normalize_path(value: str | Path, info: ValidationInfo) -> str:
    """Cleans up the path (e.g., A//B, A/./B, A/foo/../B)."""
    return os.path.normpath(str(value))


@Build.normalizer(
    register_as=NORM.FILESYSTEM.to_posix_style,
    description="Converts path separators to forward slashes.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def to_posix_style(value: str | Path, info: ValidationInfo) -> str:
    """Converts path separators to forward slashes."""
    return Path(value).as_posix()


def ensure_file_extension(*, extension: str) -> Normalizer[str | Path, str]:
    """Factory for a normalizer that ensures a path has a specific extension."""
    @Build.normalizer(
        description="Ensures a path has a specific extension.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _normalizer(value: str | Path, info: ValidationInfo) -> str:
        path = Path(value)
        if not path.suffix:
            return str(path.with_suffix(f".{extension.lstrip('.')}"))
        return str(path)

    return _normalizer


def ensure_directory_structure(*, path: str | Path) -> Normalizer[str | Path, str]:
    """Factory for a normalizer that ensures a directory structure exists."""
    @Build.normalizer(
        description="Ensures a directory structure exists.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _normalizer(value: str | Path, info: ValidationInfo) -> str:
        Path(value).mkdir(parents=True, exist_ok=True)
        return str(value)

    return _normalizer


@Build.normalizer(
    register_as=NORM.FILESYSTEM.ensure_file_exists,
    description="Ensures a file exists, creating it if it does not.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def ensure_file_exists(value: str | Path, info: ValidationInfo) -> str:
    """Ensures a file exists, creating it if it does not."""
    Path(value).touch()
    return str(value)


@Build.normalizer(
    register_as=NORM.FILESYSTEM.ensure_directory_exists,
    description="Ensures a directory exists, creating it if it does not.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def ensure_directory_exists(value: str | Path, info: ValidationInfo) -> str:
    """Ensures a directory exists, creating it if it does not."""
    Path(value).mkdir(parents=True, exist_ok=True)
    return str(value)


@Build.normalizer(
    register_as=NORM.FILESYSTEM.ensure_readable,
    description="Ensures a path is readable.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def ensure_readable(value: str | Path, info: ValidationInfo) -> str:
    """Ensures a path is readable."""
    if not os.access(str(value), os.R_OK):
        msg = f"Path is not readable: {value}"
        raise ValueError(msg)
    return str(value)


@Build.normalizer(
    register_as=NORM.FILESYSTEM.ensure_writable,
    description="Ensures a path is writable.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def ensure_writable(value: str | Path, info: ValidationInfo) -> str:
    """Ensures a path is writable."""
    if not os.access(str(value), os.W_OK):
        msg = f"Path is not writable: {value}"
        raise ValueError(msg)
    return str(value)


@Build.normalizer(
    register_as=NORM.FILESYSTEM.ensure_executable,
    description="Ensures a path is executable.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def ensure_executable(value: str | Path, info: ValidationInfo) -> str:
    """Ensures a path is executable."""
    if not os.access(str(value), os.X_OK):
        msg = f"Path is not executable: {value}"
        raise ValueError(msg)
    return str(value)


# --- New Normalizers Using python-magic ---------------------------------------

def ensure_correct_extension(*, read_content: bool = False) -> Normalizer[str | Path, str]:
    """
    Factory for a normalizer that ensures file extension matches content.

    Args:
        read_content: If True, reads file content to detect type.
                     If False, expects bytes content in context.
    """
    @Build.normalizer(
        description="Fix file extension based on detected content type.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _normalizer(value: str | Path, info: ValidationInfo) -> str:
        """Ensure file has correct extension based on content."""
        path = Path(value)

        # Get content either from file or context
        content: bytes | None = None
        if read_content and path.exists() and path.is_file():
            try:
                content = path.read_bytes()
            except OSError:
                return str(value)  # Can't read, return as-is
        elif info.context is not None:
            content = info.context.get("file_content")

        if not content:
            return str(value)  # No content to analyze

        try:
            mime_type = magic.from_buffer(content, mime=True)

            # Map MIME types to extensions
            mime_to_ext = {
                "image/jpeg": ".jpg",
                "image/png": ".png",
                "image/gif": ".gif",
                "image/bmp": ".bmp",
                "image/svg+xml": ".svg",
                "image/webp": ".webp",
                "application/pdf": ".pdf",
                "application/zip": ".zip",
                "application/x-tar": ".tar",
                "application/gzip": ".gz",
                "application/x-7z-compressed": ".7z",
                "application/x-rar-compressed": ".rar",
                "text/plain": ".txt",
                "text/html": ".html",
                "text/css": ".css",
                "text/javascript": ".js",
                "application/javascript": ".js",
                "application/json": ".json",
                "application/xml": ".xml",
                "text/xml": ".xml",
                "audio/mpeg": ".mp3",
                "audio/wav": ".wav",
                "audio/x-wav": ".wav",
                "video/mp4": ".mp4",
                "video/x-msvideo": ".avi",
                "video/quicktime": ".mov",
                "application/msword": ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document": ".docx",
                "application/vnd.ms-excel": ".xls",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": ".xlsx",
            }

            correct_ext = mime_to_ext.get(mime_type)
            if correct_ext and path.suffix.lower() != correct_ext:
                # Extension doesn't match content
                new_path = path.with_suffix(correct_ext)
                if info.context is not None:
                    info.context["extension_corrected"] = True
                    info.context["old_extension"] = path.suffix
                    info.context["new_extension"] = correct_ext
                return str(new_path)

        except (OSError, ValueError):
            # Unable to detect MIME type or read file
            pass

        return str(value)

    return _normalizer


def add_extension_from_content(*, read_content: bool = False) -> Normalizer[str | Path, str]:
    """
    Factory for a normalizer that adds missing extension based on content.

    Args:
        read_content: If True, reads file content to detect type.
                     If False, expects bytes content in context.
    """
    @Build.normalizer(
        description="Add missing file extension based on content type.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _normalizer(value: str | Path, info: ValidationInfo) -> str:
        """Add extension if missing, based on content type."""
        path = Path(value)

        # Only process if no extension
        if path.suffix:
            return str(value)

        # Get content either from file or context
        content: bytes | None = None
        if read_content and path.exists() and path.is_file():
            try:
                content = path.read_bytes()
            except OSError:
                return str(value)  # Can't read, return as-is
        elif info.context is not None:
            content = info.context.get("file_content")

        if not content:
            return str(value)  # No content to analyze

        try:
            mime_type = magic.from_buffer(content, mime=True)

            # Map MIME types to extensions (reuse from above)
            mime_to_ext = {
                "image/jpeg": ".jpg",
                "image/png": ".png",
                "image/gif": ".gif",
                "image/bmp": ".bmp",
                "image/svg+xml": ".svg",
                "application/pdf": ".pdf",
                "application/zip": ".zip",
                "application/x-tar": ".tar",
                "application/gzip": ".gz",
                "text/plain": ".txt",
                "text/html": ".html",
                "application/json": ".json",
                "application/xml": ".xml",
                "audio/mpeg": ".mp3",
                "video/mp4": ".mp4",
                "application/msword": ".doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document": ".docx",
            }

            new_ext = mime_to_ext.get(mime_type)
            if new_ext:
                new_path = path.with_suffix(new_ext)
                if info.context is not None:
                    info.context["extension_added"] = True
                    info.context["detected_mime"] = mime_type
                    info.context["added_extension"] = new_ext
                return str(new_path)

        except (OSError, ValueError):
            # Unable to detect MIME type or read file
            pass

        return str(value)

    return _normalizer
