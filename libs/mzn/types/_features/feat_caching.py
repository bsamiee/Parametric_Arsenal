"""
Title         : feat_caching.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/_features/feat_caching.py.

Description ----------- Caching feature mixin for advanced asset types.

Provides methods for cached validation and cache management, implementing the caching-related aspects of the
AdvancedAsset protocol.

"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any, ClassVar, TypeVar, cast

import orjson

from mzn.types._functions.func_cache import Cache


if TYPE_CHECKING:
    from cachetools import LRUCache

    from mzn.types._contracts.prot_assets import JSONLike
    from mzn.types._core.core_tags import Tag

# --- Type Variables -----------------------------------------------------------

T = TypeVar("T")

# --- Mixin Definition ---------------------------------------------------------


class CachingMixin:
    """
    Mixin providing caching functionality for advanced asset types.

    Implements methods for cached validation, cache management, and cache statistics, fulfilling the caching-related
    aspects of the AdvancedAsset protocol.

    """

    # These attributes are provided by TypeAsset base class when the mixin is applied
    if TYPE_CHECKING:
        mzn_metadata: ClassVar[dict[str, JSONLike]]
        mzn_tags: ClassVar[set[Tag]]

        # Methods provided by Pydantic model
        @classmethod
        async def model_validate(cls, value: T) -> T:
            """Validate a value."""
            ...

    _mzn_cache: ClassVar[Annotated[LRUCache[Any, Any] | None, "LRU cache for validation"]] = None

    @classmethod
    async def _get_cache(cls) -> Annotated[LRUCache[Any, Any], "LRU cache instance"]:
        """Get or create the validation cache for this asset type."""
        if cls._mzn_cache is None:
            cls._mzn_cache = Cache.get_lru_cache()
        return cls._mzn_cache

    @classmethod
    async def cached_validate(
        cls, value: Annotated[T, "The value to validate."]
    ) -> Annotated[T, "The validated value."]:
        """
        Validate a value using a class-local cache to optimize performance.

        Args:     value: The value to validate.

        Returns:     The validated value.

        Raises:     ValueError: If validation fails.

        """
        cache = await cls._get_cache()
        try:
            cache_key = orjson.dumps(value, option=orjson.OPT_SORT_KEYS)
        except TypeError:
            cache_key = repr(value).encode("utf-8")  # Ensure bytes for cache key

        if cache_key in cache:
            # Debug logging removed to fix import-time resource warnings

            return cast("T", cache[cache_key])

        # Debug logging removed to fix import-time resource warnings
        # Assumes the class has a `model_validate` method from the Pydantic model
        validated_value = await cls.model_validate(value)
        cache[cache_key] = validated_value
        return validated_value

    @classmethod
    async def clear_cache(cls) -> Annotated[None, "Clears the validation cache."]:
        """Clear the validation cache for this specific asset type."""
        (await cls._get_cache()).clear()
        # Info logging removed to fix import-time resource warnings

    @classmethod
    async def get_cache_stats(
        cls,
    ) -> Annotated[dict[str, Any], "A dictionary with cache statistics (size, maxsize, hits, misses)."]:
        """
        Get performance statistics for the validation cache.

        Returns:     A dictionary with cache statistics (size, maxsize, hits, misses).

        """
        cache = await cls._get_cache()
        return {
            "size": cache.currsize,
            "maxsize": cache.maxsize,
            "hits": getattr(cache, "hits", 0),
            "misses": getattr(cache, "misses", 0),
        }

# --- Public re-exports --------------------------------------------------------


__all__ = ["CachingMixin"]
