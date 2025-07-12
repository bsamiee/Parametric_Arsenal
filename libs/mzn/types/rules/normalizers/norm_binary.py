"""
Title         : norm_binary.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/binary.py

Description
-----------
Binary data normalizers for encoding and compression.
"""

from __future__ import annotations

import gzip
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo


@Build.normalizer(
    register_as=NORM.PROTOCOLS.compress,
    description="Compresses bytes using gzip.",
    tags=(SYSTEM.INFRA.io,),
)
async def compress(value: bytes, info: ValidationInfo, *, level: int = 9) -> bytes:
    """Compresses bytes using gzip."""
    return gzip.compress(value, compresslevel=level)


@Build.normalizer(
    register_as=NORM.PROTOCOLS.decompress,
    description="Decompresses gzip-compressed bytes.",
    tags=(SYSTEM.INFRA.io,),
)
async def decompress(value: bytes, info: ValidationInfo) -> bytes:
    """Decompresses gzip-compressed bytes."""
    try:
        return gzip.decompress(value)
    except gzip.BadGzipFile:
        return value
