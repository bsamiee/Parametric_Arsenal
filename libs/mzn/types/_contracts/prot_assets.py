"""
Title         : prot_assets.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path
: libs/mzn/types/_contracts/prot_assets.py.

Description ----------- Async-first consolidated protocol hierarchy for the composable feature system.

Defines a simple 3-tier protocol system that enables progressive enhancement of type assets with clear capability
boundaries and automatic feature composition. Fully integrated with Error/Cache/Log infrastructure.

"""

from __future__ import annotations

from abc import abstractmethod
from typing import (
    TYPE_CHECKING,
    Annotated,
    Any,
    ClassVar,
    LiteralString,
    Protocol,
    TypeVar,
    runtime_checkable,
)


if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

    from mzn.types._contracts.prot_base import Rule, RuleProcessingResult
    from mzn.types._core.core_constants import AssetType
    from mzn.types._core.core_tags import Tag


# --- Type Variables -----------------------------------------------------------

T_co = TypeVar("T_co", covariant=True)
M = TypeVar("M")

type JSONPrimitive = str | int | float | bool | None
type JSONLike = JSONPrimitive | list[JSONLike] | dict[str, JSONLike]

# --- Tier 1: Core Asset Protocol ----------------------------------------------


@runtime_checkable
class CoreAsset(Protocol):
    """
    Essential asset with metadata and registry integration (Tier 1).

    Every asset type gets this automatically. Provides unified metadata management and registry integration for type
    introspection.

    """

    mzn_metadata: ClassVar[Annotated[Mapping[str, JSONLike], "The metadata mapping for the asset."]]
    mzn_tags: ClassVar[Annotated[set[Tag], "The tags for the asset."]]

    @classmethod
    @abstractmethod
    async def get_metadata(cls) -> Annotated[Mapping[str, JSONLike], "The complete, immutable metadata mapping."]:
        """Get the type's complete metadata."""
        ...

    @classmethod
    @abstractmethod
    async def get_metadata_value(
        cls,
        key: Annotated[LiteralString, "The key of the metadata value to retrieve."],
        default: Annotated[JSONLike, "A default value to return if the key is not found."] = None,
    ) -> Annotated[JSONLike, "The retrieved metadata value or the provided default."]:
        """Get a specific metadata value."""
        ...

    @classmethod
    @abstractmethod
    async def set_metadata(cls, **kwargs: Annotated[JSONLike, "Keyword arguments to set as metadata."]) -> None:
        """Set metadata values."""
        ...

    @classmethod
    @abstractmethod
    async def mzn_json_schema(cls) -> Annotated[dict[str, JSONLike], "The Pydantic v2 JSON schema for the asset."]:
        """Get Pydantic v2 JSON schema if available."""
        ...

    @classmethod
    @abstractmethod
    async def aget_asset_type(cls) -> Annotated[AssetType | None, "The asset's category (e.g., ALIAS, MODEL)."]:
        """Get the asset type for this class."""


# --- Tier 2: Validated Asset Protocol -----------------------------------------


@runtime_checkable
class ValidatedAsset(CoreAsset, Protocol):
    """
    Asset with validation and normalization capabilities (Tier 2).

    For primitives → aliases, domain models, and any type that needs business logic validation and data transformation.

    """

    @classmethod
    @abstractmethod
    async def add_rule(cls, rule: Annotated[Rule, "A single validation or normalization rule."]) -> None:
        """Add a validation or normalization rule."""
        ...

    @classmethod
    @abstractmethod
    async def add_rules(cls, rules: Annotated[Sequence[Rule], "A sequence of rules to add."]) -> None:
        """Add multiple rules at once."""
        ...

    @classmethod
    @abstractmethod
    async def process_rules(cls) -> Annotated[
        RuleProcessingResult, "A tuple containing lists of processed normalizers and validators."
    ]:
        """Process rules into normalizers and validators."""
        ...

    @classmethod
    @abstractmethod
    async def has_rules(cls) -> Annotated[bool, "True if any rules are configured, False otherwise."]:
        """Check if the class has any rules configured."""
        ...

    @classmethod
    @abstractmethod
    async def get_rule_count(cls) -> Annotated[int, "The total number of configured rules."]:
        """Get the number of configured rules."""
        ...

    @classmethod
    @abstractmethod
    async def get_rules(cls) -> Annotated[list[Rule], "A list of all configured rules."]:
        """Get all configured rules."""
        ...

    @classmethod
    @abstractmethod
    async def clear_rules(cls) -> None:
        """Clear all rules."""
        ...

    @classmethod
    @abstractmethod
    async def model_validate(
        cls, value: Annotated[object, "The input value to validate."]
    ) -> Annotated[object, "The validated value."]:
        """Validate a value, returning it on success or raising an error."""
        ...

    @classmethod
    @abstractmethod
    async def inspect_rules(cls) -> Annotated[dict[LiteralString, Any], "A dictionary with rule inspection data."]:
        """Inspect configured rules and their metadata."""
        ...


# --- Tier 3: Advanced Asset Protocol ------------------------------------------


@runtime_checkable
class AdvancedAsset(ValidatedAsset, Protocol):
    """
    Asset with performance and debugging features (Tier 3).

    For complex domain models, high-performance aliases, and types that need caching, structured logging, and debugging
    capabilities.

    """

    @classmethod
    @abstractmethod
    async def cached_validate(
        cls, value: Annotated[object, "The input value to validate."]
    ) -> Annotated[object, "The validated value, potentially from cache."]:
        """Validate with caching optimization."""
        ...

    @classmethod
    @abstractmethod
    async def clear_cache(cls) -> None:
        """Clear validation cache."""
        ...

    @classmethod
    @abstractmethod
    async def get_cache_stats(cls) -> Annotated[dict[str, Any], "A dictionary of cache performance statistics."]:
        """Get cache performance statistics."""
        ...

    @classmethod
    @abstractmethod
    async def get_log_schema(cls) -> Annotated[dict[str, Any], "The JSON schema for the asset's logging structure."]:
        """Get schema for logging structure."""
        ...

    @classmethod
    @abstractmethod
    async def trace_validation(
        cls, value: Annotated[object, "The value to trace through the validation process."]
    ) -> Annotated[dict[str, Any], "A dictionary containing detailed trace information."]:
        """Trace validation process for debugging."""
        ...

    @classmethod
    @abstractmethod
    async def get_debug_info(cls) -> Annotated[dict[str, Any], "A dictionary of comprehensive debug info."]:
        """Get comprehensive debug information."""
        ...

    @abstractmethod
    async def to_log_dict(
        self, *, mask_pii: Annotated[bool, "If True, masks fields marked as PII."] = True
    ) -> Annotated[dict[str, Any], "A dictionary representation of the asset suitable for logging."]:
        """Convert to logging-safe dictionary."""
        ...

# --- Type Aliases for Common Usage --------------------------------------------


BasicType = CoreAsset
type ValidatedType = ValidatedAsset
type AdvancedType = AdvancedAsset
