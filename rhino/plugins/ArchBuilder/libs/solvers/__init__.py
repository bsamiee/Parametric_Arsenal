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

from solvers.catenary import solve_catenary_parameter
from solvers.four_center import solve_four_center_parameters
from solvers.horseshoe import solve_horseshoe_parameters
from solvers.multifoil import solve_multifoil_parameters
from solvers.ogee import solve_ogee_parameters
from solvers.three_center import solve_three_center_parameters


__all__ = [
    "solve_catenary_parameter",
    "solve_four_center_parameters",
    "solve_horseshoe_parameters",
    "solve_multifoil_parameters",
    "solve_ogee_parameters",
    "solve_three_center_parameters",
]
