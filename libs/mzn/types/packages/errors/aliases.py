"""
Title         : aliases.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/packages/errors/aliases.py

Description
-----------
Domain-aware error type aliases for the new error system.
Focused on enabling flexible, domain-qualified error management.
"""

from __future__ import annotations

from mzn.types._core.core_builders import Build
from mzn.types._core.core_operations import MethodConfig
from mzn.types._core.core_tags import SYSTEM
from mzn.types.primitives.prim_standard import PrimStr
from mzn.types.rules.rule_registry import NORM, VALID


# --- Core Error Types ---------------------------------------------------------


@Build.alias(
    base=PrimStr,
    rules=[
        NORM.STRING.to_lowercase(),
        NORM.STRING.strip_whitespace(),
        VALID.STRING.matches_pattern(r"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$"),
        VALID.STRING.has_length(min_length=3, max_length=64),
    ],
    operations=MethodConfig(casting=True),
    description="Domain-qualified error code (e.g., 'cache.backend_failure').",
    tags=(SYSTEM.ERROR,),
)
class ErrorCode:
    """
    Domain-qualified error code following 'domain.error' pattern.

    This replaces the rigid ErrorCode enum, allowing each package to define its own error codes dynamically while
    maintaining a consistent format. The casting operation enables smooth string conversions in error messages.

    """


@Build.alias(
    base=PrimStr,
    rules=[
        NORM.STRING.strip_whitespace(),
        NORM.STRING.normalize_whitespace(),
        VALID.STRING.has_length(min_length=10, max_length=1024),
    ],
    operations=MethodConfig(casting=True),
    description="Validated error message.",
    tags=(SYSTEM.ERROR,),
)
class ErrorMessage:
    """
    A validated error message with normalized whitespace.

    The casting operation supports string interpolation and formatting in error display methods like format_message().

    """


@Build.alias(
    base=PrimStr,
    rules=[
        NORM.STRING.strip_whitespace(),
        NORM.STRING.normalize_whitespace(),
        VALID.STRING.has_length(min_length=10, max_length=512),
    ],
    operations=None,
    description="Recovery hint for error resolution.",
    tags=(SYSTEM.ERROR,),
)
class RecoveryHint:
    """Actionable hint for error recovery with reasonable length constraints."""


# --- Exports ------------------------------------------------------------------

__all__ = [
    "ErrorCode",
    "ErrorMessage",
    "RecoveryHint",
]
