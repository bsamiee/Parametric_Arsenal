"""
Title         : norm_security.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_security.py

Description
-----------
Security-related normalization rules for masking sensitive data.
"""

from __future__ import annotations

import re
from typing import TYPE_CHECKING

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


def mask_pii(  # noqa: PLR0913
    *,
    mask_emails: bool = True,
    mask_credit_cards: bool = True,
    mask_ssns: bool = True,
    mask_phone_numbers: bool = True,
    email_mask: str = "***@{domain}",
    credit_card_mask: str = "****-****-****-{last4}",
    ssn_mask: str = "***-**-{last4}",
    phone_mask: str = "(***) ***-{last4}",
) -> Normalizer[str, str]:
    """Factory for a normalizer that masks personally identifiable information (PII)."""
    @Build.normalizer(
        description="Masks personally identifiable information (PII) in a string.",
        tags=(SYSTEM.SECURITY,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        result = value
        if mask_emails:
            result = re.sub(
                r"([a-zA-Z0-9._%+-]+)@([a-zA-Z0-9.-]+\.[a-zA-Z]{2,})",
                lambda m: email_mask.replace("{domain}", m.group(2)),
                result,
            )
        if mask_credit_cards:
            result = re.sub(
                r"\b(?:\d[ -]*?){12}((?:\d[ -]*?){4})\b",
                lambda m: credit_card_mask.replace("{last4}", m.group(1).replace(" ", "").replace("-", "")),
                result,
            )
        if mask_ssns:
            result = re.sub(
                r"\b(\d{3})[-]?(\d{2})[-]?(\d{4})\b",
                lambda m: ssn_mask.replace("{last4}", m.group(3)),
                result,
            )
        if mask_phone_numbers:
            result = re.sub(
                r"\b(?:\+?1[-. ]?)?(?:\(?([0-9]{3})\)?[-. ]?)?([0-9]{3})[-. ]?([0-9]{4})\b",
                lambda m: phone_mask.replace("{last4}", m.group(3)),
                result,
            )
        return result

    return _normalizer


def redact_keywords(
    *,
    redacted_text: str = "[REDACTED]",
    words: list[str],
    case_sensitive: bool = False) -> Normalizer[str, str]:
    """Factory for a normalizer that redacts specified keywords from a string."""
    @Build.normalizer(
        description="Redacts specified keywords from a string.",
        tags=(SYSTEM.SECURITY,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        if not words:
            return value
        pattern = r"\b(" + "|".join(re.escape(word) for word in words) + r")\b"
        if case_sensitive:
            return re.sub(pattern, redacted_text, value)
        return re.sub(pattern, redacted_text, value, flags=re.IGNORECASE)

    return _normalizer


def partial_mask(
    *,
    chars_to_show_start: int = 4,
    chars_to_show_end: int = 4,
    replacement_char: str = "*") -> Normalizer[str, str]:
    """Factory for a normalizer that partially masks a string."""
    @Build.normalizer(
        description="Partially masks a string, showing only specified parts.",
        tags=(SYSTEM.SECURITY,),
    )
    async def _normalizer(value: str, info: ValidationInfo) -> str:
        str_len = len(value)
        if chars_to_show_start + chars_to_show_end >= str_len:
            return value
        masked_part_len = str_len - chars_to_show_start - chars_to_show_end
        masked_part = replacement_char * masked_part_len
        start = value[:chars_to_show_start]
        end = value[str_len - chars_to_show_end :]
        return f"{start}{masked_part}{end}"

    return _normalizer
