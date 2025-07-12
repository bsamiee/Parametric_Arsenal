"""
Title         : norm_network.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_network.py

Description
-----------
Network-related normalization rules for IP addresses, URLs, and other network data.
"""

from __future__ import annotations

import ipaddress
import operator
import re
import urllib.parse
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


@Build.normalizer(
    register_as=NORM.PROTOCOLS.normalize_ipv4,
    description="Normalizes an IPv4 address to standard format.",
    tags=(SYSTEM.INFRA.network,),
)
async def normalize_ipv4(value: str, info: ValidationInfo) -> str:
    """Normalizes an IPv4 address to standard format."""
    try:
        return str(ipaddress.IPv4Address(value))
    except ValueError:
        return value


@Build.normalizer(
    register_as=NORM.PROTOCOLS.normalize_ipv6,
    description="Normalizes an IPv6 address to standard format.",
    tags=(SYSTEM.INFRA.network,),
)
async def normalize_ipv6(value: str, info: ValidationInfo) -> str:
    """Normalizes an IPv6 address to standard format."""
    try:
        return str(ipaddress.IPv6Address(value))
    except ValueError:
        return value


def normalize_url(
    *, normalize_case: bool = True, sort_params: bool = False
) -> Normalizer[str, str]:
    """Factory for a normalizer that normalizes a URL for consistent representation."""
    @Build.normalizer(
        description="Normalizes a URL for consistent representation.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        try:
            parsed = urllib.parse.urlparse(value)
            scheme = parsed.scheme.lower() if normalize_case else parsed.scheme
            netloc = parsed.netloc.lower() if normalize_case else parsed.netloc
            path = parsed.path
            if sort_params and parsed.query:
                query_params = urllib.parse.parse_qsl(parsed.query)
                query_params.sort(key=operator.itemgetter(0))
                query = urllib.parse.urlencode(query_params)
            else:
                query = parsed.query
            return urllib.parse.urlunparse(
                (scheme, netloc, path, parsed.params, query, parsed.fragment)
            )
        except (ValueError, TypeError, AttributeError):
            return value

    return _normalizer


def normalize_mac_address(*, output_format: str = ":") -> Normalizer[str, str]:
    """Factory for a normalizer that normalizes a MAC address to a consistent format."""
    @Build.normalizer(
        description="Normalizes a MAC address to a consistent format.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        cleaned_value = re.sub(r"[:.-]", "", value).lower()
        if len(cleaned_value) != 12 or not re.match(r"^[0-9a-f]{12}$", cleaned_value):
            return value
        if output_format == ":":
            return ":".join(cleaned_value[i : i + 2] for i in range(0, 12, 2))
        if output_format == "-":
            return "-".join(cleaned_value[i : i + 2] for i in range(0, 12, 2))
        if output_format == ".":
            return ".".join(cleaned_value[i : i + 4] for i in range(0, 12, 4))
        if not output_format:
            return cleaned_value
        return value

    return _normalizer


def ensure_scheme(
    *, default_scheme: str = "https", force_scheme: bool = False
) -> Normalizer[str, str]:
    """Factory for a normalizer that ensures a URL has a scheme (protocol) prefix."""
    @Build.normalizer(
        description="Ensures a URL has a scheme (protocol) prefix.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*://", value):
            if force_scheme:
                no_scheme = re.sub(r"^[a-zA-Z][a-zA-Z0-9+.-]*://", "", value)
                return f"{default_scheme}://{no_scheme}"
            return value
        return f"{default_scheme}://{value}"

    return _normalizer


def normalize_hostname(*, with_scheme: bool = True) -> Normalizer[str, str]:
    """Factory for a normalizer that normalizes a hostname to lowercase."""
    @Build.normalizer(
        description="Normalizes a hostname to lowercase.",
        tags=(SYSTEM.INFRA.network,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        if with_scheme and "://" in value:
            scheme, rest = value.split("://", 1)
            return f"{scheme.lower()}://{rest.lower()}"
        return value.lower()

    return _normalizer
