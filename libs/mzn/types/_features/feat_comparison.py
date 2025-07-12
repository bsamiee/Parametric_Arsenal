"""
Title         : feat_comparison.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_features/feat_comparison.py

Description
-----------
Universal, type-safe comparison and hashing for all assets.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any, ClassVar, cast

from mzn.types._core.core_factory_base import TypeAsset


if TYPE_CHECKING:
    from mzn.types._contracts.prot_assets import JSONLike
    from mzn.types._core.core_tags import Tag

# --- Mixin Definition ---------------------------------------------------------


class ComparisonMixin:
    """
    Composable mixin providing universal, type-safe comparison and hashing.

    This mixin is applied to all assets created by the AssetFactory to ensure
    consistent and correct comparison behavior, preventing type-related bugs.
    """

    # These attributes are provided by TypeAsset base class when the mixin is applied
    if TYPE_CHECKING:
        mzn_metadata: ClassVar[dict[str, JSONLike]]
        mzn_tags: ClassVar[set[Tag]]

    def __hash__(self) -> int:  # pyright: ignore[reportImplicitOverride]
        """Hash based on the root value for RootModel or standard hash for BaseModel."""
        if hasattr(self, "root"):
            return hash(cast("Any", self).root)
        # Fall back to the default object hash to avoid abstract method issues
        return object.__hash__(self)

    def _get_comparison_value(
        self, other: Annotated[object, "The other object to compare against."]
    ) -> Annotated[object, "The value to use for comparison, or NotImplemented."]:
        """
        Helper to extract a value for comparison, handling type checks.

        This method enforces strict comparison rules based on metadata,
        preventing comparisons between unrelated alias types.

        Args:
            other: The other object to compare against.

        Returns:
            The value to use for comparison, or NotImplemented if comparison
            is not possible.
        """
        if other is None:
            return NotImplemented

        # For RootModel assets, compare the .root value
        if hasattr(self, "root"):
            # Check for strict comparison rules
            strict_comparison = getattr(self.__class__, "mzn_metadata", {}).get("strict_comparison", True)
            if strict_comparison and isinstance(other, TypeAsset):
                # If strict, check if the other asset is a related type
                self_base = getattr(self.__class__, "mzn_metadata", {}).get("base_primitive_name")
                other_metadata = getattr(other.__class__, "mzn_metadata", {})
                other_base = other_metadata.get("base_primitive_name")

                # Allow comparison if they share the same base primitive
                if self_base is None or other_base is None or self_base != other_base:
                    return NotImplemented

                return getattr(other, "root", other)

            # Allow comparison with raw values
            return other

        # For BaseModel assets, the `other` object itself is the comparison value
        return other

    def __eq__(self, other: object) -> bool:  # pyright: ignore[reportImplicitOverride]
        """
        Type-safe equality comparison.

        Compares root values for RootModels and uses Pydantic's default
        field-based comparison for BaseModels.

        Args:
            other: The other object to compare against.

        Returns:
            True if the objects are equal, False otherwise.
        """
        # Handle RootModel comparison
        if hasattr(self, "root"):
            other_value = self._get_comparison_value(other)
            if other_value is NotImplemented:
                return False
            result = cast("Any", self).root == other_value
            if result is NotImplemented:
                return False
            return bool(result)
        # Fallback to Pydantic's BaseModel comparison
        result = cast("Any", super()).__eq__(other)
        if result is NotImplemented:
            return False
        return bool(result)

    def __lt__(self, other: object) -> bool:
        """
        Type-safe less-than comparison.

        Compares root values for RootModels. For BaseModels, this might not
        be well-defined unless `total_ordering` is used with specific fields.

        Args:
            other: The other object to compare against.

        Returns:
            True if self is less than other, False otherwise.
        """
        # Handle RootModel comparison
        if hasattr(self, "root"):
            other_value = self._get_comparison_value(other)
            if other_value is NotImplemented:
                return False
            result = cast("Any", self).root < other_value
            if result is NotImplemented:
                return False
            return bool(result)
        # Fallback for BaseModel (may not be supported by default)
        return False

# --- Public re-exports --------------------------------------------------------


__all__ = ["ComparisonMixin"]
