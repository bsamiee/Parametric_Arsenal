"""
Title         : norm_datetime.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/normalizers/norm_datetime.py

Description
-----------
Date and time normalization rules.
"""

from __future__ import annotations

import re
from datetime import UTC, datetime, timedelta
from typing import TYPE_CHECKING, Literal
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from dateutil import parser, relativedelta, rrule, tz

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import NORM


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Normalizer


@Build.normalizer(
    register_as=NORM.TEMPORAL.prepend_timestamp,
    description="Prepends a UTC timestamp to a string.",
    tags=(SYSTEM.INFRA.io,),
)
async def prepend_timestamp(value: str, info: ValidationInfo) -> str:
    """Prepends a UTC timestamp to a string."""
    now = datetime.now(UTC).isoformat()
    return f"{now} - {value}"


def to_timezone(*, tz: str) -> Normalizer[datetime, datetime]:
    """Factory for a normalizer that converts a timezone-aware datetime to a different timezone."""
    @Build.normalizer(
        description="Converts a timezone-aware datetime to a different timezone.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: datetime, info: ValidationInfo) -> datetime:
        try:
            target_tz = ZoneInfo(tz)
            return value.astimezone(target_tz)
        except (ZoneInfoNotFoundError, ValueError):
            return value

    return _normalizer


def snap_to_nearest_minute(*, minutes: int) -> Normalizer[datetime, datetime]:
    """Factory for a normalizer that rounds a time or datetime to the nearest minute interval."""
    @Build.normalizer(
        description="Rounds a time or datetime to the nearest minute interval.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: datetime, info: ValidationInfo) -> datetime:
        total_minutes = value.hour * 60 + value.minute
        rounded_total_minutes = (total_minutes + minutes // 2) // minutes * minutes
        new_hour, new_minute = divmod(rounded_total_minutes, 60)

        if new_hour >= 24:
            new_hour -= 24
            value += timedelta(days=1)

        return value.replace(hour=new_hour, minute=new_minute, second=0, microsecond=0)

    return _normalizer


def format_datetime(*, format_str: str = "%Y-%m-%d %H:%M:%S") -> Normalizer[datetime, str]:
    """Factory for a normalizer that formats a datetime object into a string."""
    @Build.normalizer(
        description="Formats a datetime object into a string.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _normalizer(value: datetime, info: ValidationInfo) -> str:
        return value.strftime(format_str)

    return _normalizer


@Build.normalizer(
    register_as=NORM.TEMPORAL.to_utc,
    description="Converts a naive datetime to an aware datetime, assuming UTC.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_utc(value: datetime, info: ValidationInfo) -> datetime:
    """Converts a naive datetime to an aware datetime, assuming UTC."""
    if value.tzinfo is None:
        return value.replace(tzinfo=UTC)
    return value.astimezone(UTC)


@Build.normalizer(
    register_as=NORM.TEMPORAL.to_unix_timestamp,
    description="Converts a datetime object to a Unix timestamp.",
    tags=(SYSTEM.INFRA.io,),
)
async def to_unix_timestamp(value: datetime, info: ValidationInfo) -> float:
    """Converts a datetime object to a Unix timestamp."""
    return value.timestamp()


@Build.normalizer(
    register_as=NORM.TEMPORAL.from_unix_timestamp,
    description="Converts a Unix timestamp to a datetime object.",
    tags=(SYSTEM.INFRA.io,),
)
async def from_unix_timestamp(value: float, info: ValidationInfo) -> datetime:
    """Converts a Unix timestamp to a datetime object."""
    return datetime.fromtimestamp(value, tz=UTC)


# --- New Normalizers Using python-dateutil ------------------------------------


@Build.normalizer(
    register_as=NORM.TEMPORAL.parse_human_date,
    description="Parse human-readable dates like 'tomorrow', 'next monday', '3 days ago'.",
    tags=(SYSTEM.COMMON.time,),
)
async def parse_human_date(value: str, info: ValidationInfo) -> datetime:
    """
    Parse human-readable date strings using dateutil's fuzzy parser.

    Examples:
        - "tomorrow at 3pm"
        - "next monday"
        - "3 days ago"
        - "last week"
        - "January 15th at 2:30 PM"
    """
    try:
        parsed = parser.parse(value, fuzzy=True)
    except (ValueError, parser.ParserError) as e:
        msg = f"Could not parse '{value}' as a date: {e}"
        raise ValueError(msg) from e
    else:
        # Ensure timezone awareness
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=UTC)
        return parsed


def add_business_days(*, days: int) -> Normalizer[datetime, datetime]:
    """
    Factory for a normalizer that adds business days (skipping weekends).

    Args:
        days: Number of business days to add (can be negative)
    """
    @Build.normalizer(
        description=f"Add {days} business days, skipping weekends.",
        tags=(SYSTEM.COMMON.time,)
    )
    async def _normalizer(value: datetime, info: ValidationInfo) -> datetime:
        """Add business days to a datetime, skipping weekends."""
        # Use rrule to calculate business days
        business_days_rule = rrule.rrule(
            rrule.DAILY,
            byweekday=(rrule.MO, rrule.TU, rrule.WE, rrule.TH, rrule.FR),
            dtstart=value,
            count=abs(days) + 1,
        )

        dates = list(business_days_rule)
        if days >= 0:
            return dates[days] if days < len(dates) else dates[-1]
        # For negative days, we need to go backwards
        business_days_rule = rrule.rrule(
            rrule.DAILY,
            byweekday=(rrule.MO, rrule.TU, rrule.WE, rrule.TH, rrule.FR),
            dtstart=value - timedelta(days=abs(days) * 2),  # Overshoot to ensure we have enough days
            until=value,
        )
        dates = list(business_days_rule)
        return dates[days] if abs(days) <= len(dates) else dates[0]

    return _normalizer


def next_occurrence(*, frequency: Literal["daily", "weekly", "monthly", "yearly"]) -> Normalizer[datetime, datetime]:
    """
    Factory for a normalizer that finds the next occurrence based on frequency.

    Args:
        frequency: The recurrence frequency
    """
    freq_map = {
        "daily": rrule.DAILY,
        "weekly": rrule.WEEKLY,
        "monthly": rrule.MONTHLY,
        "yearly": rrule.YEARLY,
    }

    @Build.normalizer(
        description=f"Find next {frequency} occurrence from the given date.",
        tags=(SYSTEM.COMMON.time,),
    )
    async def _normalizer(value: datetime, info: ValidationInfo) -> datetime:
        """Find the next occurrence based on the specified frequency."""
        rule = rrule.rrule(freq_map[frequency], dtstart=value, count=2)
        occurrences = list(rule)
        # Return the second occurrence (first is the start date itself)
        return occurrences[1] if len(occurrences) > 1 else occurrences[0]

    return _normalizer


@Build.normalizer(
    register_as=NORM.TEMPORAL.parse_timezone_name,
    description="Convert timezone names/abbreviations to timezone-aware datetime.",
    tags=(SYSTEM.COMMON.time,),
)
async def parse_timezone_name(value: tuple[datetime, str], info: ValidationInfo) -> datetime:
    """
    Handle timezone conversion with support for abbreviations like 'EST', 'PST'.

    Args:
        value: Tuple of (datetime, timezone_name)
        info: Validation context (unused)

    Returns:
        Timezone-aware datetime
    """
    dt, tz_name = value

    try:
        # First try standard timezone parsing
        timezone = tz.gettz(tz_name)
        if timezone:
            return dt.replace(tzinfo=timezone) if dt.tzinfo is None else dt.astimezone(timezone)

        # Try parsing as timezone abbreviation
        # This handles common abbreviations like EST, PST, GMT, etc.
        parsed_tz = parser.parse(f"2023-01-01 12:00 {tz_name}")
        if parsed_tz.tzinfo:
            return dt.replace(tzinfo=parsed_tz.tzinfo) if dt.tzinfo is None else dt.astimezone(parsed_tz.tzinfo)

        # Fall back to UTC if timezone cannot be determined
        return dt.replace(tzinfo=UTC) if dt.tzinfo is None else dt.astimezone(UTC)

    except (ValueError, TypeError, AttributeError):
        # If all parsing fails, return with UTC
        return dt.replace(tzinfo=UTC) if dt.tzinfo is None else dt.astimezone(UTC)


@Build.normalizer(
    register_as=NORM.TEMPORAL.parse_duration,
    description="Parse duration strings like '2 hours 30 minutes' into timedelta.",
    tags=(SYSTEM.COMMON.time,),
)
async def parse_duration(value: str, info: ValidationInfo) -> timedelta:
    """
    Parse human-readable duration strings into timedelta objects.

    Examples:
        - "2 hours 30 minutes"
        - "1 day 12 hours"
        - "45 minutes"
        - "3 weeks"
    """
    try:
        # Parse a future date with the duration added
        base = datetime.now(UTC)
        future = parser.parse(f"in {value}", default=base, fuzzy=True)
        return future - base
    except (ValueError, parser.ParserError):
        # Try parsing with relativedelta for more complex durations
        try:
            # Handle patterns like "2 hours 30 minutes"
            value_lower = value.lower()
            delta = relativedelta.relativedelta()

            # Simple pattern matching for common duration formats
            patterns = [
                (r"(\d+)\s*years?", "years"),
                (r"(\d+)\s*months?", "months"),
                (r"(\d+)\s*weeks?", "weeks"),
                (r"(\d+)\s*days?", "days"),
                (r"(\d+)\s*hours?", "hours"),
                (r"(\d+)\s*minutes?", "minutes"),
                (r"(\d+)\s*seconds?", "seconds"),
            ]

            for pattern, attr in patterns:
                matches = re.findall(pattern, value_lower)
                if matches:
                    setattr(delta, attr, sum(int(m) for m in matches))

            # Convert relativedelta to timedelta (approximate for months/years)
            base = datetime.now(UTC)
            future = base + delta
            return future - base

        except Exception as e:
            msg = f"Could not parse '{value}' as a duration: {e}"
            raise ValueError(msg) from e


def round_to_month_boundary(*, which: Literal["start", "end"]) -> Normalizer[datetime, datetime]:
    """
    Factory for a normalizer that rounds to start or end of month.

    Args:
        which: Whether to round to "start" or "end" of month
    """
    @Build.normalizer(
        description=f"Round datetime to {which} of month.",
        tags=(SYSTEM.COMMON.time,),
    )
    async def _normalizer(value: datetime, info: ValidationInfo) -> datetime:
        """Round to month boundary using dateutil.relativedelta."""
        if which == "start":
            # First day of the month at midnight
            return value.replace(day=1, hour=0, minute=0, second=0, microsecond=0)
        # Last day of the month at 23:59:59
        next_month = value + relativedelta.relativedelta(months=1, day=1)
        last_day = next_month - timedelta(days=1)
        return last_day.replace(hour=23, minute=59, second=59, microsecond=999999)

    return _normalizer


@Build.normalizer(
    register_as=NORM.TEMPORAL.to_business_day,
    description="Adjust datetime to next business day if it falls on a weekend.",
    tags=(SYSTEM.COMMON.time,)
)
async def to_business_day(value: datetime, info: ValidationInfo) -> datetime:
    """
    Adjust datetime to the next business day if it falls on a weekend.

    Saturday -> Monday
    Sunday -> Monday
    Weekday -> No change
    """
    weekday = value.weekday()
    if weekday == 5:  # Saturday
        return value + timedelta(days=2)
    if weekday == 6:  # Sunday
        return value + timedelta(days=1)
    return value
