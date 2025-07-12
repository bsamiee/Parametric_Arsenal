"""
Title         : core_factory_base.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_core/core_factory_base.py

Description
-----------
Base asset class for the type system.
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Annotated, Any, ClassVar

from pydantic import BaseModel, TypeAdapter


if TYPE_CHECKING:
    from mzn.types._contracts.prot_assets import JSONLike
    from mzn.types._core.core_tags import Tag


# --- TypeeAssets CLass --------------------------------------------------------

class TypeAsset(BaseModel):
    """
    The foundational base class for all type assets.

    This class inherits from `pydantic.BaseModel` and provides the core
    validation and serialization functionality that all assets created by the
    `AssetFactory` will share. It serves as the concrete implementation that
    satisfies the static type checker when analyzing the feature mixins.

    Provides the core attributes required by the CoreAsset protocol to ensure
    all mixins have access to metadata and tagging functionality.
    """

    adapter: ClassVar[Annotated[TypeAdapter[Any], "Pydantic type adapter for the asset"]]
    mzn_metadata: ClassVar[Annotated[dict[str, JSONLike], "The metadata mapping for the asset"]] = {}
    mzn_tags: ClassVar[Annotated[set[Tag], "The tags for the asset"]] = set()
