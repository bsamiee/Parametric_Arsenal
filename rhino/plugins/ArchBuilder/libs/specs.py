"""
Title         : specs.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/specs.py

Description
----------------------------------------------------------------------------
Arch specification data structures, enums, and helpers shared across the
ArchBuilder plugin.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import MISSING, asdict, dataclass, field, fields
from enum import Enum
from typing import Any, TypeVar

import Rhino.Geometry as rg


# --- Arch Family Enum -----------------------------------------------------
class ArchFamily(Enum):
    """Supported arch families."""

    CIRCULAR_SEGMENT = "circular_segment"
    SEMICIRCLE = "semicircle"
    ELLIPSE = "ellipse"
    PARABOLA = "parabola"
    CATENARY = "catenary"
    TWO_CENTER = "two_center"
    THREE_CENTER = "three_center"
    FOUR_CENTER = "four_center"
    OGEE = "ogee"
    HORSESHOE = "horseshoe"
    MULTIFOIL = "multifoil"

    @classmethod
    def from_string(cls, value: str) -> ArchFamily:
        """Normalize a string into an ArchFamily value."""
        normalized = value.strip().lower().replace("-", "_")
        for member in cls:
            if member.value == normalized:
                return member
        raise ValueError(f"Unknown arch family: {value}")


# --- Arch Specification ---------------------------------------------------
@dataclass(frozen=True)
class ArchSpec:
    """Immutable description of an arch configuration."""

    family: ArchFamily
    span: float
    rise: float
    plane: rg.Plane
    metadata: dict[str, Any] = field(default_factory=dict)

    def __post_init__(self) -> None:
        if self.span <= 0:
            raise ValueError("Span must be positive.")
        if self.rise <= 0:
            raise ValueError("Rise must be positive.")

    @property
    def half_span(self) -> float:
        """Half of the total span, convenient for symmetric profiles."""
        return self.span * 0.5

    @property
    def local_plane(self) -> rg.Plane:
        """Return the canonical local construction plane."""
        return rg.Plane.WorldXY

    @property
    def plane_transform(self) -> rg.Transform:
        """Transform from local plane to the arch plane."""
        return rg.Transform.PlaneToPlane(self.local_plane, self.plane)

    # --- Factory Methods --------------------------------------------------
    @classmethod
    def from_rectangle(
        cls,
        rectangle: rg.Rectangle3d,
        *,
        family: ArchFamily,
        metadata: dict[str, Any] | None = None,
    ) -> ArchSpec:
        """Build an ArchSpec from a planar rectangle."""
        if not rectangle.IsValid:
            raise ValueError("Input rectangle is invalid.")

        plane = rectangle.Plane
        span = rectangle.Width
        rise = rectangle.Height

        if span <= 0 or rise <= 0:
            raise ValueError("Rectangle dimensions must be positive.")

        mid = rectangle.Center
        plane.Origin = rg.Point3d(mid.X, mid.Y, mid.Z)

        return cls(
            family=family,
            span=span,
            rise=rise,
            plane=plane,
            metadata=metadata or {},
        )

    @classmethod
    def from_numeric(
        cls,
        span: float,
        rise: float,
        plane: rg.Plane,
        *,
        family: ArchFamily,
        metadata: dict[str, Any] | None = None,
    ) -> ArchSpec:
        """Build an ArchSpec from numeric inputs."""
        return cls(
            family=family,
            span=span,
            rise=rise,
            plane=plane,
            metadata=metadata or {},
        )


# --- Command Option Helpers -------------------------------------------------
T_Options = TypeVar("T_Options", bound="ArchCommandOptions")


@dataclass(frozen=True)
class ArchCommandOptions:
    """Base dataclass for arch command parameters."""

    def to_metadata(self) -> dict[str, Any]:
        """Convert option fields into serialisable metadata for ArchSpec."""
        return asdict(self)

    @classmethod
    def from_metadata(
        cls: type[T_Options],
        metadata: Mapping[str, Any] | None = None,
    ) -> T_Options:
        """Rehydrate an option dataclass from stored metadata."""
        data = metadata or {}
        values: dict[str, Any] = {}
        for field_info in fields(cls):
            if field_info.name in data:
                values[field_info.name] = data[field_info.name]
            elif field_info.default is not MISSING:
                values[field_info.name] = field_info.default
            elif field_info.default_factory is not MISSING:  # type: ignore[comparison-overlap]
                values[field_info.name] = field_info.default_factory()
            else:
                raise ValueError(f"Missing required option '{field_info.name}' for {cls.__name__}.")
        return cls(**values)


@dataclass(frozen=True)
class EmptyArchOptions(ArchCommandOptions):
    """Placeholder options for commands without custom parameters."""


@dataclass(frozen=True)
class ThreeCenterArchOptions(ArchCommandOptions):
    """User-configurable parameters for three-center arches."""

    shoulder_ratio: float = 0.25


@dataclass(frozen=True)
class FourCenterArchOptions(ArchCommandOptions):
    """User-configurable parameters for four-center arches."""

    shoulder_ratio: float = 0.3
    shoulder_height_ratio: float = 0.5


@dataclass(frozen=True)
class OgeeArchOptions(ArchCommandOptions):
    """User-configurable parameters for ogee arches."""

    inflection_height: float = 0.4
    curve_strength: float = 0.5


@dataclass(frozen=True)
class HorseshoeArchOptions(ArchCommandOptions):
    """User-configurable parameters for horseshoe arches."""

    extension_degrees: float = 240.0


@dataclass(frozen=True)
class MultifoilArchOptions(ArchCommandOptions):
    """User-configurable parameters for multifoil arches."""

    lobes: int = 5
    lobe_size: float = 0.7
