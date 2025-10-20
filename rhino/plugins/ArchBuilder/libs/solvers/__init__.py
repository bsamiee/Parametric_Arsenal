"""
Title         : __init__.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/ArchBuilder/libs/solvers/__init__.py

Description
----------------------------------------------------------------------------
Solvers package for arch construction calculations.
Exports general-purpose solvers, arch-specific parameter solvers, and
mathematical utilities for geometric computations.
"""

from .catenary import solve_catenary_parameter
from .four_center import solve_four_center_parameters
from .horseshoe import solve_horseshoe_parameters
from .multifoil import solve_multifoil_parameters
from .ogee import solve_ogee_parameters
from .three_center import solve_three_center_parameters


__all__ = [
    "solve_catenary_parameter",
    "solve_four_center_parameters",
    "solve_horseshoe_parameters",
    "solve_multifoil_parameters",
    "solve_ogee_parameters",
    "solve_three_center_parameters",
]
