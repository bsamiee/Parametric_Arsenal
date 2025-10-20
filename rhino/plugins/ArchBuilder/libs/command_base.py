"""
Title         : command_base.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/command_base.py

Description
----------------------------------------------------------------------------
Base functionality for individual arch commands to minimize duplication.
"""

from __future__ import annotations

from typing import Any, Callable

import Rhino.Geometry as rg
import scriptcontext as sc

import Rhino

from .assemble import ArchAssembler
from .input_methods import ArchInputManager, InputMethod
from .metadata import ArchMetadata
from .specs import ArchFamily, ArchSpec
from .ui import ProfileSelection
from .utils import ArchBuilderUtils


# --- Base Command Class ---------------------------------------------------
class ArchCommandBase:
    """Base functionality shared by all arch commands."""

    def __init__(self, family: ArchFamily, builder: Callable[[ArchSpec], rg.Curve]) -> None:
        self.family = family
        self.builder = builder
        self.abs_tol = ArchBuilderUtils.model_absolute_tolerance()

    def get_profile_selection(self) -> ProfileSelection | None:
        """Collect a profile selection using the method chosen by the user."""
        method = ArchInputManager.get_input_method()
        if method is None:
            return None

        if method == InputMethod.CURVE:
            return ArchInputManager.get_curve_input(self.abs_tol)
        return ArchInputManager.get_points_input(self.abs_tol)

    def collect_parameters(self, profile: ProfileSelection) -> dict[str, Any] | None:  # noqa: PLR6301
        """Override in subclasses to collect arch-specific parameters."""
        return {}

    def add_to_document(self, curves: list[rg.Curve], spec: ArchSpec) -> None:
        """Add curves to document with metadata."""
        undo = sc.doc.BeginUndoRecord(f"ArchBuilder {self.family.value}")
        try:
            for curve in curves:
                ArchMetadata.attach_to_curve(curve, spec)
                sc.doc.Objects.AddCurve(curve)
        finally:
            sc.doc.EndUndoRecord(undo)
        sc.doc.Views.Redraw()

    def execute(self) -> bool:
        """Gather input, build geometry, and add results to the document."""
        profile = self.get_profile_selection()
        if profile is None:
            return False

        metadata = self.collect_parameters(profile)
        if metadata is None:  # User cancelled parameter collection
            return False

        spec = ArchSpec(
            family=self.family,
            span=profile.span,
            rise=profile.rise,
            plane=profile.plane,
            metadata=metadata,
        )

        profile_local = self.builder(spec)
        result = ArchAssembler.build(spec, profile_local)

        self.add_to_document(result.all_curves, spec)
        return True


# --- Command Factory Function ---------------------------------------------
def arch_command(family: ArchFamily, builder: Callable[[ArchSpec], rg.Curve]) -> Callable[[], int]:
    """Decorator to create arch command with minimal boilerplate."""

    def run_command() -> int:
        try:
            command = ArchCommandBase(family, builder)
            success = command.execute()
        except RuntimeError as exc:
            Rhino.RhinoApp.WriteLine(f"ArchBuilder cancelled: {exc}")
            return 0
        else:
            if success:
                Rhino.RhinoApp.WriteLine(f"ArchBuilder: {family.value} created.")
            return 0 if success else 1

    return run_command
