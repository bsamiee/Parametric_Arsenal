"""
Title         : core_constants.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/_core/core_constants.py.

Description ----------- System-wide constants, enums, and sentinels for the type system. Clean separation of Assets,
Features, and Functions.

"""

from __future__ import annotations

from enum import Enum, auto
from typing import Final, override


# --- Asset Types --------------------------------------------------------------


class AssetType(Enum):
    """Types of assets in the type system."""

    PRIMITIVE = auto()  # Basic types (int, str, float, etc.)
    ALIAS = auto()  # Type aliases (UserId = int, etc.)
    ENUM = auto()  # Enumeration types
    MODEL = auto()  # Data models/dataclasses
    PROTOCOL = auto()  # Interface definitions

    @override
    def __str__(self) -> str:
        return self.name.capitalize()


# --- Feature Types ------------------------------------------------------------


class FeatureType(Enum):
    """Types of features that enhance assets."""

    METADATA = auto()  # Metadata capabilities
    RULES = auto()  # Validation/normalization rules
    REGISTRY = auto()  # Type registration and lookup
    CACHING = auto()  # Caching capabilities
    COMPARISON = auto()  # Comparison capabilities
    DOCUMENTATION = auto()  # Documentation capabilities

    @override
    def __str__(self) -> str:
        return self.name.capitalize()


# --- System Modes -------------------------------------------------------------


class SystemMode(Enum):
    """System operation modes."""

    DEVELOPMENT = auto()  # Rich context, stack traces, verbose logging
    PRODUCTION = auto()  # Clean messages, minimal context, performance focused
    TESTING = auto()  # Structured for assertions, predictable output

    @override
    def __str__(self) -> str:
        """Return the name for clean display."""
        return self.name


# --- Sentinel Markers ---------------------------------------------------------


class Sentinel(Enum):
    """Enum-based sentinel system for type-safe magic values."""

    AUTO_DETECT = auto()
    MISSING = auto()
    NOT_SET = auto()
    INHERIT = auto()
    DEFAULT = auto()
    SKIP = auto()
    IGNORE = auto()
    # High-value error sentinels
    UNHANDLED = auto()
    RETRY = auto()
    SUPPRESS = auto()
    ESCALATE = auto()
    # --- New sentinels for rules system ---
    DISABLED = auto()   # Explicitly disables a rule without removing it
    DEFER = auto()      # Marks a rule for deferred/later processing
    NOOP = auto()       # No-operation rule, placeholder

    def __bool__(self) -> bool:
        """Sentinels are always truthy for conditional checks."""
        return True

    @override
    def __repr__(self) -> str:
        """Clean representation."""
        return f"<{self.name}>"

    @override
    def __str__(self) -> str:
        """String representation."""
        return self.name

    @override
    def __reduce__(self) -> str:
        """Pickle support - just return the name."""
        return self.name

    @classmethod
    def is_sentinel(cls, value: object) -> bool:
        """Check if value is any sentinel."""
        return isinstance(value, Sentinel)


# --- Convenient Constants -----------------------------------------------------

# Collections
ASSET_TYPES: Final[tuple[AssetType, ...]] = tuple(AssetType.__members__.values())
FEATURE_TYPES: Final[tuple[FeatureType, ...]] = tuple(FeatureType.__members__.values())
SENT_TYPES: Final[tuple[Sentinel, ...]] = tuple(Sentinel.__members__.values())

# Asset constants
PRIMITIVE: Final[AssetType] = AssetType.PRIMITIVE
ALIAS: Final[AssetType] = AssetType.ALIAS
ENUM: Final[AssetType] = AssetType.ENUM
MODEL: Final[AssetType] = AssetType.MODEL
PROTOCOL: Final[AssetType] = AssetType.PROTOCOL

# Feature constants
METADATA: Final[FeatureType] = FeatureType.METADATA
RULES: Final[FeatureType] = FeatureType.RULES
REGISTRY: Final[FeatureType] = FeatureType.REGISTRY

# Sentinel constants
AUTO_DETECT: Final[Sentinel] = Sentinel.AUTO_DETECT
MISSING: Final[Sentinel] = Sentinel.MISSING
NOT_SET: Final[Sentinel] = Sentinel.NOT_SET
INHERIT: Final[Sentinel] = Sentinel.INHERIT
DEFAULT: Final[Sentinel] = Sentinel.DEFAULT
SKIP: Final[Sentinel] = Sentinel.SKIP
IGNORE: Final[Sentinel] = Sentinel.IGNORE
UNHANDLED: Final[Sentinel] = Sentinel.UNHANDLED
RETRY: Final[Sentinel] = Sentinel.RETRY
SUPPRESS: Final[Sentinel] = Sentinel.SUPPRESS
ESCALATE: Final[Sentinel] = Sentinel.ESCALATE
# --- New sentinels for rules system ---
DISABLED: Final[Sentinel] = Sentinel.DISABLED
DEFER: Final[Sentinel] = Sentinel.DEFER
NOOP: Final[Sentinel] = Sentinel.NOOP
