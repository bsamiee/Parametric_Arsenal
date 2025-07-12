"""
Title         : valid_path.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/validators/valid_path.py

Description
-----------
Path validation rules.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


@Build.validator(
    register_as=VALID.FILESYSTEM.exists,
    error_template="Path does not exist: '{value}'",
    description="Check if the file or directory at the path exists.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def exists(value: str | Path, info: ValidationInfo) -> bool:
    """Check if the file or directory at the path exists."""
    return Path(value).exists()


@Build.validator(
    register_as=VALID.FILESYSTEM.is_file,
    error_template="Path is not a file: '{value}'",
    description="Check if the path points to a file.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_file(value: str | Path, info: ValidationInfo) -> bool:
    """Check if the path points to a file."""
    return Path(value).is_file()


@Build.validator(
    register_as=VALID.FILESYSTEM.is_dir,
    error_template="Path is not a directory: '{value}'",
    description="Check if the path points to a directory.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_dir(value: str | Path, info: ValidationInfo) -> bool:
    """Check if the path points to a directory."""
    return Path(value).is_dir()


def has_extension(*, extensions: list[str]) -> Validator[str | Path]:
    """Factory for a validator that checks if the path has one of the specified file extensions."""
    @Build.validator(
        error_template=f"Path must have one of the following extensions: {extensions}",
        description="Check if the path has one of the specified file extensions.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _validator(value: str | Path, info: ValidationInfo) -> bool:
        path = Path(value)
        valid_extensions = {f".{ext.lstrip('.')}" for ext in extensions}
        return path.suffix in valid_extensions

    return _validator


@Build.validator(
    register_as=VALID.FILESYSTEM.is_absolute,
    error_template="Path must be absolute.",
    description="Checks if a path is absolute.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_absolute(value: str | Path, info: ValidationInfo) -> bool:
    """Checks if a path is absolute."""
    return Path(value).is_absolute()


@Build.validator(
    register_as=VALID.FILESYSTEM.is_relative,
    error_template="Path must be relative.",
    description="Checks if a path is relative.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_relative(value: str | Path, info: ValidationInfo) -> bool:
    """Checks if a path is relative."""
    return not Path(value).is_absolute()


@Build.validator(
    register_as=VALID.FILESYSTEM.parent_exists,
    error_template="Parent directory does not exist for path: '{value}'",
    description="Check if the parent directory of the path exists.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def parent_exists(value: str | Path, info: ValidationInfo) -> bool:
    """Check if the parent directory of the path exists."""
    return Path(value).parent.exists()


@Build.validator(
    register_as=VALID.FILESYSTEM.is_readable,
    error_template="Path must be readable.",
    description="Checks if the current user has read permissions for the path.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_readable(value: str | Path, info: ValidationInfo) -> bool:
    """Checks if the current user has read permissions for the path."""
    return os.access(str(value), os.R_OK)


@Build.validator(
    register_as=VALID.FILESYSTEM.is_writable,
    error_template="Path must be writable.",
    description="Checks if the current user has write permissions for the path.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_writable(value: str | Path, info: ValidationInfo) -> bool:
    """Checks if the current user has write permissions for the path."""
    return os.access(str(value), os.W_OK)


@Build.validator(
    register_as=VALID.FILESYSTEM.has_valid_path,
    error_template="Path is not valid.",
    description="Checks if a path is valid on the current OS.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def has_valid_path(value: str | Path, info: ValidationInfo) -> bool:
    """Checks if a path is valid on the current OS."""
    try:
        _ = Path(value)
    except (TypeError, ValueError):
        return False
    return True


@Build.validator(
    register_as=VALID.FILESYSTEM.is_symlink,
    error_template="Path must be a symbolic link.",
    description="Checks if a path is a symbolic link.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_symlink(value: str | Path, info: ValidationInfo) -> bool:
    """Checks if a path is a symbolic link."""
    return Path(value).is_symlink()


@Build.validator(
    register_as=VALID.FILESYSTEM.is_hidden,
    error_template="Path must be a hidden file or directory.",
    description="Checks if a path is a hidden file or directory.",
    tags=(SYSTEM.INFRA.filesystem,),
)
async def is_hidden(value: str | Path, info: ValidationInfo) -> bool:
    """Checks if a path is a hidden file or directory."""
    return Path(value).name.startswith(".")


def has_min_size(*, min_size: int) -> Validator[str | Path]:
    """Factory for a validator that checks if a file has a minimum size in bytes."""
    @Build.validator(
        error_template=f"File size must be at least {min_size} bytes.",
        description="Checks if a file has a minimum size in bytes.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _validator(value: str | Path, info: ValidationInfo) -> bool:
        return Path(value).stat().st_size >= min_size

    return _validator


def has_max_size(*, max_size: int) -> Validator[str | Path]:
    """Factory for a validator that checks if a file has a maximum size in bytes."""
    @Build.validator(
        error_template=f"File size must be at most {max_size} bytes.",
        description="Checks if a file has a maximum size in bytes.",
        tags=(SYSTEM.INFRA.filesystem,),
    )
    async def _validator(value: str | Path, info: ValidationInfo) -> bool:
        return Path(value).stat().st_size <= max_size

    return _validator
