"""
Title         : diagnostics.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_contracts/diagnostics.py

Description
-----------
Diagnostic and utility functions for the type system.

This module contains high-level functions for inspecting, validating, and
reporting on the protocol and type hierarchy. These are primarily used by
the AssetFactory and for debugging purposes.
"""
from __future__ import annotations

from typing import Annotated, Literal, TypedDict

from beartype import beartype
from returns.result import Failure, Result, Success

from mzn.types._contracts.prot_assets import (
    AdvancedAsset,
    CoreAsset,
    ValidatedAsset,
)


# --- Hierarchy Classes and Type Definitions -----------------------------------

class ProtocolHierarchyReport(TypedDict):
    """A structured report on the protocol hierarchy of a class."""

    class_name: Annotated[str, "The name of the class."]
    tier: Annotated[int, "The protocol tier."]
    implements_core: Annotated[bool, "Whether CoreAsset is implemented."]
    implements_validated: Annotated[bool, "Whether ValidatedAsset is implemented."]
    implements_advanced: Annotated[bool, "Whether AdvancedAsset is implemented."]
    required_mixins: Annotated[
        dict[Literal["core", "validated", "advanced"], list[str]],
        "Required mixins for each protocol."
    ]
    validation_errors: Annotated[list[str], "List of validation errors."]

# --- Diagnostic Functions -----------------------------------------------------


@beartype
async def aget_asset_tier(cls: Annotated[type, "The class to check for its protocol tier."]) -> int:
    """
    Determine the capability tier of an asset class with async logging.

    Args:
        cls: Class to check

    Returns:
        1 for CoreAsset, 2 for ValidatedAsset, 3 for AdvancedAsset
    """
    tier = 0
    if isinstance(cls, AdvancedAsset):
        tier = 3
    elif isinstance(cls, ValidatedAsset):
        tier = 2
    elif isinstance(cls, CoreAsset):
        tier = 1

    return tier


@beartype
async def aimplements_protocol(
    cls: Annotated[type, "The class to check."],
    protocol: Annotated[type, "The protocol to check against."],
) -> bool:
    """
    Check if a class implements a protocol with async logging.

    Args:
        cls: Class to check
        protocol: Protocol to check against

    Returns:
        True if class implements protocol, False otherwise
    """
    # Debug logging removed to fix import-time resource warnings
    return isinstance(cls, protocol)


async def aensure_protocol(
    cls: Annotated[type, "The class to check."],
    protocol: Annotated[type, "The protocol that must be implemented."],
) -> Result[type, Exception]:
    """
    Ensure a class implements a protocol with async error handling.

    Args:
        cls: Class to check
        protocol: Protocol that must be implemented

    Returns:
        A `Success` containing the class if it implements the protocol,
        or a `Failure` with a standard exception.
    """
    protocol_name = getattr(protocol, "__name__", str(protocol))
    cls_name = getattr(cls, "__name__", str(cls))

    if not isinstance(cls, protocol):
        error_msg = f"Class {cls_name} does not implement protocol {protocol_name}"
        return Failure(TypeError(error_msg))

    return Success(cls)


@beartype
async def aget_required_mixins(
    target_protocol: Annotated[type, "The protocol to analyze for required mixins."]
) -> list[str]:
    """
    Get the list of mixin names required for a target protocol with async caching and logging.

    Args:
        target_protocol: Protocol to analyze

    Returns:
        List of mixin class names needed to implement the protocol
    """
    # Base mixins that all assets should have
    base_mixins = ["ComparisonMixin", "MetadataMixin", "DocumentationMixin"]

    if target_protocol == AdvancedAsset:
        mixins = [
            *base_mixins,
            "RulesMixin",
            "CachingMixin",
        ]
    elif target_protocol == ValidatedAsset:
        mixins = [*base_mixins, "RulesMixin"]
    elif target_protocol == CoreAsset:
        mixins = base_mixins
    else:
        mixins = base_mixins  # Default fallback

    return mixins


@beartype
async def avalidate_protocol_hierarchy(
    cls: Annotated[type, "The class to analyze."]
) -> ProtocolHierarchyReport:
    """
    Validate and report on the protocol hierarchy of a class.

    Args:
        cls: Class to analyze

    Returns:
        Dictionary with hierarchy analysis
    """
    cls_name = cls.__name__

    # Properly typed variables
    tier = await aget_asset_tier(cls)
    implements_core = await aimplements_protocol(cls, CoreAsset)
    implements_validated = await aimplements_protocol(cls, ValidatedAsset)
    implements_advanced = await aimplements_protocol(cls, AdvancedAsset)

    validation_errors: list[str] = []
    required_mixins: dict[Literal["core", "validated", "advanced"], list[str]] = {}

    # Check protocol consistency
    if implements_advanced and not implements_validated:
        validation_errors.append("AdvancedAsset should also implement ValidatedAsset")

    if implements_validated and not implements_core:
        validation_errors.append("ValidatedAsset should also implement CoreAsset")

    # Get required mixins for each implemented protocol
    if implements_core:
        required_mixins["core"] = await aget_required_mixins(CoreAsset)

    if implements_validated:
        required_mixins["validated"] = await aget_required_mixins(ValidatedAsset)

    if implements_advanced:
        required_mixins["advanced"] = await aget_required_mixins(AdvancedAsset)

    # Build the analysis dict
    analysis: ProtocolHierarchyReport = {
        "class_name": cls_name,
        "tier": tier,
        "implements_core": implements_core,
        "implements_validated": implements_validated,
        "implements_advanced": implements_advanced,
        "required_mixins": required_mixins,
        "validation_errors": validation_errors,
    }

    return analysis

# --- Public re-exports --------------------------------------------------------


__all__ = [
    "aensure_protocol",
    "aget_asset_tier",
    "aget_required_mixins",
    "aimplements_protocol",
    "avalidate_protocol_hierarchy",
]
