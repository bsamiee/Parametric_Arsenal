"""
Title         : ui.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/ui.py

Description
----------------------------------------------------------------------------
Profile selection data structure for arch commands.
"""

from __future__ import annotations

from dataclasses import dataclass

import Rhino.Geometry as rg


# --- Profile Selection Data -----------------------------------------------
@dataclass
class ProfileSelection:
    """Construction inputs describing an arch profile selection.

    The optional `curve` retains the originating Rhino curve when the user
    selects an existing outline instead of numeric input.
    """

    span: float
    rise: float
    plane: rg.Plane
    curve: rg.Curve | None
