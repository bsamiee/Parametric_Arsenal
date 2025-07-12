"""
Title         : prot_features.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_contracts/prot_features.py

Description
-----------
Protocols for all individual, composable features in the type system.

This module defines the structural contracts for each feature, such as
Rules, Comparison, Caching, etc. An asset that implements one of these
protocols is guaranteed to have the corresponding feature's capabilities.
"""

from __future__ import annotations

from abc import abstractmethod
from typing import (
    TYPE_CHECKING,
    Annotated,
    Any,
    LiteralString,
    Protocol,
    Self,
    TypeVar,
    override,
    runtime_checkable,
)

from mzn.types._contracts.prot_assets import CoreAsset, JSONLike


CT = TypeVar("CT", bound=type[CoreAsset])


if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence
    from types import MappingProxyType

    from mzn.types._contracts.prot_base import Rule, RuleProcessingResult
    from mzn.types._core.core_constants import AssetType, Sentinel
    from mzn.types._core.core_tags import Tag

# --- Rules ------------------------------------------------------------------


@runtime_checkable
class RulesProvider(Protocol):
    """Protocol for assets that support validation and normalization rules (fully async)."""

    # ── CRUD on individual / multiple rules ──────────────────────────────────
    @classmethod
    @abstractmethod
    async def add_rule(
        cls: type[Self],
        rule: Annotated[Rule, "A single validation or normalization rule."]
    ) -> None:
        """Add a single validation or normalization rule."""
        ...

    @classmethod
    @abstractmethod
    async def add_rules(
        cls: type[Self],
        rules: Annotated[Sequence[Rule], "A sequence of rules to add."]
    ) -> None:
        """Add multiple validation or normalization rules."""
        ...

    # ── Bulk processing / retrieval ──────────────────────────────────────────
    @classmethod
    @abstractmethod
    async def process_rules(
        cls: type[Self],
    ) -> Annotated[
        RuleProcessingResult,
        "A tuple containing lists of processed normalizers and validators.",
    ]:
        """Process and return all rules as normalizers and validators."""
        ...

    @classmethod
    @abstractmethod
    async def get_rules(
        cls: type[Self],
    ) -> Annotated[list[Rule], "A list of all configured rules."]:
        """Get all configured rules."""
        ...

    # ── Cache helpers ────────────────────────────────────────────────────────
    @classmethod
    @abstractmethod
    async def from_alternate_name(cls: type[Self], name: str) -> Self | None:
        """Get Enum member by an alternate name."""
        ...

    @classmethod
    @abstractmethod
    async def clear_rule_cache(cls: type[Self]) -> None:
        """Clear the rule cache."""
        ...

    # ── Introspection helpers ────────────────────────────────────────────────
    @classmethod
    @abstractmethod
    async def has_rules(
        cls: type[Self],
    ) -> Annotated[bool, "True if any rules are configured, False otherwise."]:
        """Return True if any rules are configured."""
        ...

    @classmethod
    @abstractmethod
    async def get_rule_count(
        cls: type[Self],
    ) -> Annotated[int, "The total number of configured rules."]:
        """Get the total number of configured rules."""
        ...

    @classmethod
    @abstractmethod
    async def get_normalizer_count(
        cls: type[Self],
    ) -> Annotated[int, "Number of processed normalizer rules."]:
        """Get the number of processed normalizer rules."""
        ...

    @classmethod
    @abstractmethod
    async def get_validator_count(
        cls: type[Self],
    ) -> Annotated[int, "Number of processed validator rules."]:
        """Get the number of processed validator rules."""
        ...

    @classmethod
    @abstractmethod
    async def inspect_rules(
        cls: type[Self],
    ) -> Annotated[
        dict[LiteralString, Any], "A dictionary with rule inspection data."
    ]:
        """Inspect and return rule data as a dictionary."""
        ...

    # ── Validation of rule configuration ─────────────────────────────────────
    @classmethod
    @abstractmethod
    async def validate_rule_configuration(
        cls: type[Self],
    ) -> Annotated[
        dict[LiteralString, Any], "Results of rule-configuration validation."
    ]:
        """Validate the rule configuration."""
        ...

    # ── Re-use between types --------------------------------------------------
    @classmethod
    @abstractmethod
    async def copy_rules_from(
        cls: type[Self],
        source_cls: Annotated[RulesProvider, "Source RulesProvider to copy from."]
    ) -> None:
        """Copy rules from another RulesProvider."""
        ...

    @classmethod
    @abstractmethod
    async def merge_rules_from(
        cls: type[Self],
        *source_classes: Annotated[RulesProvider, "Source RulesProviders to merge from."]
    ) -> None:
        """Merge rules from multiple RulesProvider sources."""
        ...

# --- Comparison --------------------------------------------------------------


@runtime_checkable
class ComparisonProvider(Protocol):
    """Protocol for assets that support type-safe comparison."""

    @override
    @abstractmethod
    def __eq__(
        self,
        other: Annotated[object, "The object to compare against."]
    ) -> Annotated[bool, "True if equal, False otherwise."]:
        """Type-safe equality comparison."""
        ...

    @abstractmethod
    def __lt__(
        self,
        other: Annotated[Self, "The object to compare against."]
    ) -> Annotated[bool, "True if less than, False otherwise."]:
        """Type-safe less-than comparison."""
        ...

    @override
    @abstractmethod
    def __hash__(self) -> Annotated[int, "The hash value."]:
        """Return the hash value of the object."""
        ...

# --- Documentation -----------------------------------------------------------


@runtime_checkable
class DocumentationProvider(Protocol):
    """Protocol for assets with self-generating documentation."""

    @classmethod
    @abstractmethod
    async def generate_docstring(
        cls: type[Self],
        asset_type: Annotated[str, "The type of asset (e.g., 'alias', 'model')."],
        shell_cls: Annotated[type[Any], "The original user-defined shell class."]
    ) -> Annotated[str, "A comprehensive, generated docstring."]:
        """Generate a comprehensive docstring for the asset."""
        ...

# --- Caching -----------------------------------------------------------------


@runtime_checkable
class CachingProvider(Protocol):
    """Protocol for assets with validation caching capabilities."""

    @classmethod
    @abstractmethod
    async def cached_validate(
        cls: type[Self],
        value: Annotated[object, "The input value to validate."]
    ) -> Annotated[object, "The validated value, potentially from cache."]:
        """Validate a value using cache if available."""
        ...

    @classmethod
    @abstractmethod
    async def clear_cache(cls: type[Self]) -> None:
        """Clear the validation cache."""
        ...

    @classmethod
    @abstractmethod
    async def get_cache_stats(
        cls: type[Self],
    ) -> Annotated[dict[str, Any], "Cache performance statistics (hits, misses, size, etc.)."]:
        """Get cache performance statistics."""
        ...


# --- Tagging -----------------------------------------------------------------


@runtime_checkable
class TaggingProvider(Protocol):
    """Protocol for assets with tagging capabilities."""

    @classmethod
    @abstractmethod
    async def get_tags(cls) -> set[Tag]:
        """Get all tags associated with this asset."""
        ...

    @classmethod
    @abstractmethod
    async def add_tags(cls, *tags: Tag) -> None:
        """Add tags to this asset."""
        ...

    @classmethod
    @abstractmethod
    async def remove_tags(cls, *tags: Tag) -> None:
        """Remove tags from this asset."""
        ...

    @classmethod
    @abstractmethod
    async def has_tag(cls, tag: Tag) -> bool:
        """Check if this asset has a specific tag."""
        ...

    @classmethod
    @abstractmethod
    async def has_any_tag(cls, *tags: Tag) -> bool:
        """Check if this asset has any of the specified tags."""
        ...

    @classmethod
    @abstractmethod
    async def filter_tags_by_prefix(cls, prefix: str) -> set[Tag]:
        """Filter asset's tags by path prefix."""
        ...

    @classmethod
    @abstractmethod
    async def has_tag_with_ancestor(cls, ancestor: Tag) -> bool:
        """Check if this asset has any tag that is a descendant of the given ancestor."""
        ...

# --- Registry ----------------------------------------------------------------


@runtime_checkable
class RegistryProvider[CT: type[CoreAsset]](Protocol):
    """Protocol for a generic async type registry keyed by AssetType and string."""

    # ── Registration & look-up ───────────────────────────────────────────────
    @abstractmethod
    async def aregister(
        self,
        cls: CT,
        /,
        *,
        key: Annotated[str | None, "Explicit key for registration."] = None,
        asset_type: Annotated[AssetType | Sentinel | None, "Asset category."] = None,
        metadata: Annotated[dict[str, Any] | None, "Extra metadata for the registration."] = None,
    ) -> CT:
        """Register an asset in the registry."""
        ...

    @abstractmethod
    async def aget(self, key: str, /) -> CT | None:
        """Get an asset from the registry by key."""
        ...

    @abstractmethod
    async def aget_asset_type(self, asset_type: AssetType, /) -> dict[str, CT]:
        """Get all assets of a given type."""
        ...

    @abstractmethod
    async def aget_asset_types(self) -> set[AssetType]:
        """Get all asset types in the registry."""
        ...

    @abstractmethod
    async def aget_all(self) -> MappingProxyType[str, CT]:
        """Get all assets in the registry."""
        ...

    @abstractmethod
    async def aunregister(self, key: str, /) -> CT | None:
        """Unregister an asset by key."""
        ...

    @abstractmethod
    async def aclear(self) -> None:
        """Clear all assets from the registry."""
        ...

    @abstractmethod
    async def acontains(self, key: str, /) -> bool:
        """Check if a key exists in the registry."""
        ...

    @abstractmethod
    async def alen(self) -> int:
        """Get the number of assets in the registry."""
        ...

    @abstractmethod
    async def aget_stats(self) -> dict[str, Any]:
        """Get statistics about the registry."""
        ...

# --- Metadata ----------------------------------------------------------------


@runtime_checkable
class MetadataProvider(Protocol):
    """Protocol for assets with unified async metadata management."""

    # ── Basic CRUD ───────────────────────────────────────────────────────────
    @classmethod
    @abstractmethod
    async def get_metadata(cls) -> Mapping[str, JSONLike]:
        """Get all metadata for the asset."""
        ...

    @classmethod
    @abstractmethod
    async def get_metadata_value(
        cls, key: LiteralString, /, default: JSONLike = None
    ) -> JSONLike:
        """Get a metadata value by key."""
        ...

    @classmethod
    @abstractmethod
    async def set_metadata(cls, **kwargs: JSONLike) -> None:
        """Set metadata values for the asset."""
        ...

    # ── Merge / validation helpers ───────────────────────────────────────────
    @classmethod
    @abstractmethod
    async def merge_metadata(cls, other_metadata: Mapping[str, JSONLike], /) -> None:
        """Merge metadata from another mapping."""
        ...

    @classmethod
    @abstractmethod
    async def has_metadata_key(cls, key: LiteralString, /) -> bool:
        """Check if a metadata key exists."""
        ...

    @classmethod
    @abstractmethod
    async def get_metadata_keys(cls) -> set[LiteralString]:
        """Get all metadata keys."""
        ...

    @classmethod
    @abstractmethod
    async def validate_metadata(cls) -> bool:
        """Validate the asset's metadata."""
        ...

    # ── Serialisation & copy helpers ─────────────────────────────────────────
    @classmethod
    @abstractmethod
    async def serialize_metadata(cls) -> dict[str, JSONLike]:
        """Serialize the asset's metadata to a dictionary."""
        ...

    @classmethod
    @abstractmethod
    async def copy_metadata_from(cls, source_cls: type[CoreAsset], /) -> None:
        """Copy metadata from another CoreAsset class."""
        ...

    @classmethod
    @abstractmethod
    async def merge_metadata_from(cls, *source_classes: type[CoreAsset]) -> None:
        """Merge metadata from multiple CoreAsset classes."""
        ...

    # ── Derived helpers ------------------------------------------------------
    @classmethod
    @abstractmethod
    async def get_asset_type(cls) -> object:
        """Get the asset type."""
        ...

    @classmethod
    @abstractmethod
    async def get_registry_key(cls) -> str:
        """Get the registry key for the asset."""
        ...
