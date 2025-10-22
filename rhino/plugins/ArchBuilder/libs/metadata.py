"""
Title         : metadata.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/metadata.py

Description
----------------------------------------------------------------------------
Metadata utilities for storing and retrieving ArchSpec information from
Rhino geometry objects.
"""

from __future__ import annotations

import json
from typing import Any

import Rhino.DocObjects as rd
import Rhino.Geometry as rg
import scriptcontext as sc

from libs.specs import ArchFamily, ArchSpec


# --- Arch Metadata Handler ------------------------------------------------
class ArchMetadata:
    """Handle metadata serialization for arch specifications."""

    KEY = "arch_builder_spec"
    VERSION = "1.0"

    # --- Plane Serialization ----------------------------------------------
    @staticmethod
    def _plane_to_payload(plane: rg.Plane) -> dict[str, Any]:
        """Serialise a Rhino plane into a JSON-friendly mapping."""
        return {
            "origin": [plane.Origin.X, plane.Origin.Y, plane.Origin.Z],
            "xaxis": [plane.XAxis.X, plane.XAxis.Y, plane.XAxis.Z],
            "yaxis": [plane.YAxis.X, plane.YAxis.Y, plane.YAxis.Z],
        }

    @staticmethod
    def _plane_from_payload(payload: dict[str, Any]) -> rg.Plane:
        """Rebuild a Rhino plane from a JSON payload."""
        origin = rg.Point3d(*payload["origin"])
        xaxis = rg.Vector3d(*payload["xaxis"])
        yaxis = rg.Vector3d(*payload["yaxis"])
        return rg.Plane(origin, xaxis, yaxis)

    # --- JSON Serialization -----------------------------------------------
    @classmethod
    def to_json(cls, spec: ArchSpec) -> str:
        """Serialise an arch specification to JSON for storage."""
        payload = {
            "version": cls.VERSION,
            "family": spec.family.value,
            "span": spec.span,
            "rise": spec.rise,
            "plane": cls._plane_to_payload(spec.plane),
            "metadata": spec.metadata,
        }
        return json.dumps(payload)

    @classmethod
    def from_json(cls, raw: str) -> ArchSpec:
        """Deserialize JSON back into an arch specification."""
        payload = json.loads(raw)
        if payload.get("version") != cls.VERSION:
            raise ValueError("Unsupported ArchBuilder metadata version.")
        plane = cls._plane_from_payload(payload["plane"])
        return ArchSpec(
            family=ArchFamily.from_string(payload["family"]),
            span=payload["span"],
            rise=payload["rise"],
            plane=plane,
            metadata=payload.get("metadata", {}),
        )

    # --- Curve Metadata Operations ----------------------------------------
    @classmethod
    def attach_to_curve(cls, curve: rg.Curve, spec: ArchSpec) -> None:
        """Write specification JSON onto a curve's user dictionary."""
        curve.UserDictionary.Set(cls.KEY, cls.to_json(spec))

    @classmethod
    def try_get_spec(cls, curve: rg.Curve) -> ArchSpec | None:
        """Return the stored specification if the curve carries valid metadata."""
        success, raw = curve.UserDictionary.TryGetValue(cls.KEY)
        if not success or not raw:
            return None
        try:
            return cls.from_json(raw)
        except (ValueError, KeyError, TypeError):
            return None

    # --- Document Search --------------------------------------------------
    @classmethod
    def find_arch_curves(cls) -> list[tuple[rd.CurveObject, ArchSpec]]:
        """Locate curves in the active document that contain ArchBuilder metadata."""
        results: list[tuple[rd.CurveObject, ArchSpec]] = []
        for obj in sc.doc.Objects:
            if not isinstance(obj, rd.CurveObject):
                continue
            geometry = obj.Geometry
            if not isinstance(geometry, rg.Curve):
                continue
            spec = cls.try_get_spec(geometry)
            if spec is not None:
                results.append((obj, spec))
        return results
