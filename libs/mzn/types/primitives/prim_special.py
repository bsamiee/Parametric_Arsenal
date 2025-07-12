"""
Title         : prim_special.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/primitives/prim_special.py.

Description ----------- Specialized primitives that include helper methods.

"""
# pyright: reportUninitializedInstanceVariable=false
from __future__ import annotations

import pathlib as _pl
import uuid as _uuid

from mzn.types._core.core_builders import Build


@Build.primitive(str)
class PrimPath:
    """
    Immutable path-like string primitive.

    A wrapper around the built-in str type that provides enhanced type safety and path manipulation capabilities. This
    primitive is designed for representing filesystem paths as immutable value objects with convenient conversion to
    pathlib.Path objects.

    """

    root: str

    def as_path(self) -> _pl.Path:
        """Convert this path primitive to a pathlib.Path object."""
        return _pl.Path(self.root)


@Build.primitive(str)
class PrimUUID:
    """
    Immutable UUID-like string primitive.

    A wrapper around the built-in str type that provides enhanced type safety and UUID conversion capabilities. This
    primitive is designed for representing UUID identifiers as immutable string value objects with convenient conversion
    to uuid.UUID.

    """

    root: str

    def as_uuid(self) -> _uuid.UUID:
        """Convert this UUID primitive to a uuid.UUID object."""
        return _uuid.UUID(self.root)
