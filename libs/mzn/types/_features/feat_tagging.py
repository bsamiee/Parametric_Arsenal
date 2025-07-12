"""
Title         : feat_tagging.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_features/feat_tagging.py

Description
-----------
Feature mixin for adding tagging capabilities to assets.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, ClassVar


if TYPE_CHECKING:
    from mzn.types._contracts.prot_assets import JSONLike
    from mzn.types._core.core_tags import Tag

# --- Mixin Definition ---------------------------------------------------------


class TaggingMixin:
    """
    Composable mixin providing tagging capabilities to assets.

    This mixin is automatically applied by the AssetFactory to all assets,
    allowing for categorization and filtering by domain-specific tags.
    It uses a simple `set[Tag]` for storage, making it efficient and clean.
    """

    # These attributes are provided by TypeAsset base class when the mixin is applied
    if TYPE_CHECKING:
        mzn_metadata: ClassVar[dict[str, JSONLike]]
        mzn_tags: ClassVar[set[Tag]]

    @classmethod
    async def get_tags(cls) -> Annotated[set[Tag], "All tags associated with this asset."]:
        """Get all tags associated with this asset."""
        # The attribute is guaranteed by the CoreAsset protocol
        return cls.mzn_tags

    @classmethod
    async def add_tags(cls, *tags: Annotated[Tag, "Tags to add"]) -> Annotated[None, "None"]:
        """Add tags to this asset."""
        # The attribute is guaranteed by the CoreAsset protocol
        cls.mzn_tags.update(tags)

    @classmethod
    async def remove_tags(cls, *tags: Annotated[Tag, "Tags to remove"]) -> Annotated[None, "None"]:
        """Remove tags from this asset."""
        # The attribute is guaranteed by the CoreAsset protocol
        cls.mzn_tags.difference_update(tags)

    @classmethod
    async def has_tag(
        cls,
        tag: Annotated[Tag, "Tag to check"],
    ) -> Annotated[bool, "True if tag exists, False otherwise."]:
        """Check if this asset has a specific tag."""
        return tag in await cls.get_tags()

    @classmethod
    async def has_any_tag(
        cls,
        *tags: Annotated[Tag, "Tags to check"],
    ) -> Annotated[bool, "True if any tag exists, False otherwise."]:
        """Check if this asset has any of the specified tags."""
        return not (await cls.get_tags()).isdisjoint(tags)

    @classmethod
    async def filter_tags_by_prefix(
        cls,
        prefix: Annotated[str, "Prefix to filter tags by"],
    ) -> Annotated[set[Tag], "Tags with the given prefix."]:
        """Filter asset's tags by path prefix."""
        return {tag for tag in await cls.get_tags() if tag.path.startswith(prefix)}

    @classmethod
    async def has_tag_with_ancestor(
        cls,
        ancestor: Annotated[Tag, "Ancestor tag"],
    ) -> Annotated[bool, "True if any tag is a descendant of ancestor."]:
        """Check if this asset has any tag that is a descendant of the given ancestor."""
        return any(tag.has_ancestor(ancestor) for tag in await cls.get_tags())


# --- Public re-exports --------------------------------------------------------

__all__ = ["TaggingMixin"]
