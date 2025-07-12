"""
Title         : valid_network.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/validators/valid_network.py

Description
-----------
Network-related validation rules for IP addresses, ports, hostnames, and URLs.
"""

from __future__ import annotations

import ipaddress
import re
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator


@Build.validator(
    register_as=VALID.PROTOCOLS.is_ipv4,
    error_template="Value must be a valid IPv4 address.",
    description="Validates IPv4 address format.",
    tags=(SYSTEM.INFRA.network,),
)
async def is_ipv4(value: str, info: ValidationInfo) -> bool:
    """Validates IPv4 address format."""
    try:
        _ = ipaddress.IPv4Address(value)
    except ValueError:
        return False
    return True


@Build.validator(
    register_as=VALID.PROTOCOLS.is_ipv6,
    error_template="Value must be a valid IPv6 address.",
    description="Validates IPv6 address format.",
    tags=(SYSTEM.INFRA.network,),
)
async def is_ipv6(value: str, info: ValidationInfo) -> bool:
    """Validates IPv6 address format."""
    try:
        _ = ipaddress.IPv6Address(value)
    except ValueError:
        return False
    return True


@Build.validator(
    register_as=VALID.PROTOCOLS.is_ip_address,
    error_template="Value must be a valid IP address (IPv4 or IPv6).",
    description="Validates IP address format (either IPv4 or IPv6).",
    tags=(SYSTEM.INFRA.network,),
)
async def is_ip_address(value: str, info: ValidationInfo) -> bool:
    """Validates IP address format (either IPv4 or IPv6)."""
    try:
        _ = ipaddress.ip_address(value)
    except ValueError:
        return False
    return True


@Build.validator(
    register_as=VALID.PROTOCOLS.is_public_ip,
    error_template="Value must be a public IP address.",
    description="Validates that an IP address is public.",
    tags=(SYSTEM.INFRA.network,),
)
async def is_public_ip(value: str, info: ValidationInfo) -> bool:
    """Validates that an IP address is public."""
    try:
        return ipaddress.ip_address(value).is_global
    except ValueError:
        return False


@Build.validator(
    register_as=VALID.PROTOCOLS.is_private_ip,
    error_template="Value must be a private IP address.",
    description="Validates that an IP address is private.",
    tags=(SYSTEM.INFRA.network,),
)
async def is_private_ip(value: str, info: ValidationInfo) -> bool:
    """Validates that an IP address is private."""
    try:
        return ipaddress.ip_address(value).is_private
    except ValueError:
        return False


def is_port_number(*, min_port: int = 1, max_port: int = 65535) -> Validator[int]:
    """Factory for a validator that checks if a value is a valid network port."""
    @Build.validator(
        error_template=f"Value must be a valid port number ({min_port}-{max_port}).",
        description="Checks if value is a valid network port.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _validator(value: int, info: ValidationInfo) -> bool:
        return min_port <= value <= max_port

    return _validator


def is_valid_port_range(*, min_port: int = 1, max_port: int = 65535) -> Validator[str]:
    """Factory for a validator that checks if a string is a valid port range."""
    @Build.validator(
        error_template=f"Value must be a valid port range ({min_port}-{max_port}).",
        description="Checks if a string is a valid port range.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        if "-" not in value:
            return False
        try:
            start, end = map(int, value.split("-"))
        except ValueError:
            return False
        else:
            return min_port <= start <= end <= max_port

    return _validator


def is_hostname(*, allow_ip: bool = True) -> Validator[str]:
    """Factory for a validator that checks for valid hostname format according to RFC 1123."""
    @Build.validator(
        error_template="Value must be a valid hostname.",
        description="Validates hostname format according to RFC 1123.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        if allow_ip:
            try:
                _ = ipaddress.ip_address(value)
            except ValueError:
                pass  # Not an IP, check for hostname
            else:
                return True
        if len(value) > 255:
            return False
        labels = value.split(".")
        if len(labels) < 2:
            return False
        for label in labels:
            if not (1 <= len(label) <= 63):
                return False
            if not re.match(r"^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", label):
                return False
        return True

    return _validator


@Build.validator(
    register_as=VALID.PROTOCOLS.is_domain_name,
    error_template="Value must be a valid domain name.",
    description="Validates domain name format.",
    tags=(SYSTEM.INFRA.network,),
)
async def is_domain_name(value: str, info: ValidationInfo) -> bool:
    """Validates domain name format."""
    if len(value) > 255:
        return False
    if value[-1] == ".":
        value = value[:-1]
    return all(
        re.match(r"^[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$", label)
        for label in value.split(".")
    )


def is_mac_address(*, formats: list[str] | None = None) -> Validator[str]:
    """Factory for a validator that checks for valid MAC address format."""
    allowed_formats = formats or [":", "-", "."]

    @Build.validator(
        error_template="Value must be a valid MAC address.",
        description="Validates MAC address format.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        if ":" in allowed_formats and re.match(r"^([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$", value):
            return True
        if "-" in allowed_formats and re.match(r"^([0-9a-fA-F]{2}-){5}[0-9a-fA-F]{2}$", value):
            return True
        if "." in allowed_formats and re.match(r"^([0-9a-fA-F]{4}\.){2}[0-9a-fA-F]{4}$", value):
            return True
        return "" in allowed_formats and re.match(r"^[0-9a-fA-F]{12}$", value) is not None

    return _validator


@Build.validator(
    register_as=VALID.PROTOCOLS.is_cidr,
    error_template="Value must be a valid CIDR notation.",
    description="Validates CIDR notation (IP address with subnet mask).",
    tags=(SYSTEM.INFRA.network,),
)
async def is_cidr(value: str, info: ValidationInfo) -> bool:
    """Validates CIDR notation (IP address with subnet mask)."""
    try:
        _ = ipaddress.ip_network(value, strict=False)
    except ValueError:
        return False
    return True


def is_url(
    *, allowed_schemes: list[str] | None = None, require_scheme: bool = True
) -> Validator[str]:
    """Factory for a validator that checks if a string is a valid URL."""
    schemes = allowed_schemes or ["http", "https"]

    @Build.validator(
        error_template="Value must be a valid URL.",
        description="Validates URL format.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _validator(value: str, info: ValidationInfo) -> bool:
        pattern = r"^"
        if require_scheme:
            scheme_part = f"({'|'.join(schemes)})://"
            pattern += scheme_part
        else:
            scheme_part = f"({'|'.join(schemes)}://)?"
            pattern += scheme_part
        pattern += (
            r"([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}"
            r"|localhost"
            r"|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}"
        )
        pattern += r"(:\d+)?"
        pattern += r"(/[^?\s#]*)?"
        pattern += r"(\?[^#\s]*)?"
        pattern += r"(#[^\s]*)?"
        pattern += r"$"
        return re.match(pattern, value) is not None

    return _validator
