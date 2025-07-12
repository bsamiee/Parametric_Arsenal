"""
Title         : aliases.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/general/aliases.py.

Description ----------- General-purpose, reusable type aliases.

"""

from __future__ import annotations

from mzn.types._core.core_builders import Build
from mzn.types._core.core_operations import MethodConfig
from mzn.types._core.core_tags import SYSTEM
from mzn.types.primitives.prim_datetime import PrimTimestamp
from mzn.types.primitives.prim_special import PrimPath, PrimUUID
from mzn.types.rules.rule_registry import NORM, VALID


# --- General Type Aliases -----------------------------------------------------


@Build.alias(
    base=PrimPath,
    rules=[
        NORM.FILESYSTEM.normalize_path(),
        NORM.FILESYSTEM.resolve_path(),
        VALID.FILESYSTEM.exists(),
        VALID.FILESYSTEM.is_absolute(),
        VALID.FILESYSTEM.is_readable(),
    ],
    operations=None,
    description="A validated, normalized file system path.",
    tags=(SYSTEM.INFRA.filesystem,),
)
class FilePath:
    """A validated, normalized file system path."""


@Build.alias(
    base=PrimTimestamp,
    rules=[
        NORM.TEMPORAL.to_utc(),
        VALID.TEMPORAL.is_timezone_aware(),
    ],
    operations=MethodConfig(datetime_like=True),
    description="A UTC timestamp, defaulting to the current time.",
    tags=(SYSTEM.COMMON.time,),
)
class TimestampUTC:
    """A UTC timestamp, defaulting to the current time."""


@Build.alias(
    base=PrimUUID,
    rules=[
        NORM.PROTOCOLS.generate_if_none(),
        VALID.PROTOCOLS.is_uuid(),
        VALID.PROTOCOLS.is_uuid_version(version=4),
    ],
    operations=None,
    description="A unique identifier for tracing an operation. Auto-generates a new UUID if one is not provided.",
    tags=(SYSTEM.COMMON, SYSTEM.COMMON.identity),
)
class RequestID:
    """
    A unique identifier for tracing an operation.

    Auto-generates a new UUID if one is not provided.

    """


# --- Exports ------------------------------------------------------------------

__all__ = [
    "FilePath",
    "RequestID",
    "TimestampUTC",
]
