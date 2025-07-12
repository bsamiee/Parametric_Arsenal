"""
Title         : core_tags.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/_core/core_tags.py.

Description ----------- Hierarchical tag system for asset categorization.

This module provides a sophisticated hierarchical tag system using aenum's native nested enum capabilities. The system
supports 3-tier hierarchies (e.g., SYSTEM.INFRA.io) and is fully compatible with downstream usage patterns.

"""

from __future__ import annotations

from typing import Annotated, override

from aenum import Enum, auto


# --- Base Tag Infrastructure --------------------------------------------------


class Tag(Enum):
    """Base class for all tags with hierarchical capabilities."""

    @property
    def path(self) -> Annotated[str, "Full hierarchical path"]:
        """Return the full hierarchical path of this tag instance."""
        # Use the class's qualified name to build the path
        parts = [self.name]

        # Extract all class names from the qualified name
        if hasattr(self.__class__, "__qualname__"):
            qualname_parts = self.__class__.__qualname__.split(".")
            # Add all parent class names except 'Tag' (the base class)
            parts.extend([part for part in reversed(qualname_parts) if part != "Tag"])

        return "::".join(reversed(parts))

    @property
    def doc(self) -> Annotated[str, "Documentation"]:
        """Return the documentation for this tag."""
        return self.__doc__ or ""

    def has_ancestor(
        self,
        ancestor: Annotated[Tag, "Potential ancestor tag"]
    ) -> Annotated[bool, "True if ancestor"]:
        """Check if the given tag is an ancestor of this tag."""
        # Simple path-based check - if ancestor's path is a prefix of this tag's path
        ancestor_path = ancestor.path
        this_path = self.path

        # Ancestor relationship means the ancestor path should be a prefix
        # and this tag should have more levels
        return (
            this_path.startswith(ancestor_path)
            and this_path != ancestor_path
            and this_path[len(ancestor_path):].startswith("::")
        )

    @override
    def __str__(self) -> str:
        return self.path

    @override
    def __repr__(self) -> str:
        return f"<{self.path}>"


# --- System Tag Hierarchy (Internal Plumbing) ---------------------------------


class SYSTEM(Tag):
    """Primary system categories for internal packages."""

    class COMMON(Tag):
        """Common, reusable assets."""
        identity = auto()
        time = auto()
        tagging = auto()
        metadata = auto()
        pattern = auto()
        encoding = auto()
        versioning = auto()
        operations = auto()

    class INFRA(Tag):
        """Infrastructure subcategories."""
        filesystem = auto()
        io = auto()
        network = auto()
        web = auto()
        database = auto()
        architecture = auto()
        performance = auto()
        concurrency = auto()
        hierarchy = auto()
        backend = auto()
        serialization = auto()
        compression = auto()
        memory = auto()
        feature = auto()
        provider = auto()

    class CACHE(Tag):
        """Caching services and utilities."""
        # No members yet

    class CONFIG(Tag):
        """Configuration management."""
        # No members yet

    class LOGGING(Tag):
        """Logging infrastructure."""
        reporting = auto()
        level = auto()
        context = auto()
        output = auto()

    class METRICS(Tag):
        """Metrics and monitoring."""
        lifecycle = auto()
        performance = auto()
        identification = auto()
        measurement = auto()
        aggregation = auto()
        temporal = auto()
        configuration = auto()
        statistics = auto()

    class DEBUG(Tag):
        """Debugging tools and utilities."""
        # No members yet

    class ERROR(Tag):
        """Error handling and reporting."""
        identity = auto()
        diagnostic = auto()
        reporting = auto()

    class SECURITY(Tag):
        """Security-related concerns."""
        # No members yet

    class PERFORMANCE(Tag):
        """Performance optimization and monitoring."""
        # No members yet

    class CALLBACKS(Tag):
        """Callback and event handling systems."""
        # No members yet


# --- AEC Tag Hierarchy (Domain-Specific) --------------------------------------


class AEC(Tag):
    """Primary categories for Architecture, Engineering, and Construction."""

    class GEOMETRY(Tag):
        """Geometric subcategories."""
        point = auto()
        curve = auto()
        surface = auto()
        mesh = auto()
        solid = auto()

    class DATA(Tag):
        """Data subcategories."""
        parameter = auto()
        material = auto()
        structure = auto()

    class BUILDING(Tag):
        """Building subcategories."""
        element = auto()
        system = auto()
        site = auto()

    class ANALYSIS(Tag):
        """Simulation and analysis types."""
        # No members yet

    class COMPUTATION(Tag):
        """Computational design concepts."""
        # No members yet


# --- Public re-exports --------------------------------------------------------

__all__ = [
    "AEC",
    "SYSTEM",
    "Tag",
]
