from typing import Annotated, ClassVar

from mzn.types._contracts.prot_assets import CoreAsset
from mzn.types._core.core_constants import AssetType

async def aregister_type[T](
    cls: Annotated[T, "Type to register"],
    *,
    key: Annotated[str | None, "Registry key"] = None,
    asset_type: Annotated[AssetType | None, "Asset type"] = None,
    metadata: Annotated[dict[str, object] | None, "Optional metadata"] = None,
) -> Annotated[T, "Registered type"]: ...

async def aget_type(
    key: Annotated[str, "Registry key"]
) -> Annotated[type[CoreAsset] | None, "Registered type or None"]: ...

async def aget_types_by_asset(
    asset_type: Annotated[AssetType, "Asset type"]
) -> Annotated[dict[str, type[CoreAsset]], "Mapping of keys to types"]: ...

async def aget_registry_stats() -> Annotated[dict[str, object], "Registry statistics"]: ...

async def aget_by_tag(
    tag: object
) -> Annotated[dict[str, CoreAsset], "Assets with the specified tag"]: ...

async def aget_by_tag_prefix(
    prefix: str
) -> Annotated[dict[str, CoreAsset], "Assets with tags matching the prefix"]: ...

async def aget_by_tags(
    tags: set[object],
    *,
    match_all: bool = False
) -> Annotated[dict[str, CoreAsset], "Assets with the specified tags"]: ...

async def aget_by_ancestor(
    ancestor: object
) -> Annotated[dict[str, CoreAsset], "Assets with tags descending from the ancestor"]: ...

class Registry:
    TypeRegistry: ClassVar[type]
    aregister_type: ClassVar[object]
    aget_type: ClassVar[object]
    aget_types_by_asset: ClassVar[object]
    aget_registry_stats: ClassVar[object]
    aget_by_tag: ClassVar[object]
    aget_by_tag_prefix: ClassVar[object]
    aget_by_tags: ClassVar[object]
    aget_by_ancestor: ClassVar[object]
