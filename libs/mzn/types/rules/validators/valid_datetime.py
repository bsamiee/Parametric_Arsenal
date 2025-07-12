"""
Title         : valid_datetime.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/validators/valid_datetime.py

Description
-----------
Date and time validation rules.
"""

from __future__ import annotations

import calendar
from datetime import UTC, date, datetime, timedelta
from itertools import islice
from typing import TYPE_CHECKING, TypeVar

from dateutil import parser, rrule

from mzn.types._core.core_builders import Build
from mzn.types._core.core_tags import SYSTEM
from mzn.types.rules.rule_registry import VALID


if TYPE_CHECKING:
    from pydantic import ValidationInfo

    from mzn.types._contracts.prot_base import Validator

# --- Type Variables -----------------------------------------------------------

T_DateTime = TypeVar("T_DateTime", bound=date | datetime)

# --- Normalizers --------------------------------------------------------------


@Build.validator(
    register_as=VALID.TEMPORAL.is_in_future,
    error_template="Date must be in the future.",
    description="Checks if a date or datetime is in the future.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_in_future(value: date | datetime, info: ValidationInfo) -> bool:
    """Check if a date or datetime is in the future."""
    now = datetime.now(UTC)
    if isinstance(value, datetime):
        return value > now
    return value > now.date()


@Build.validator(
    register_as=VALID.TEMPORAL.is_in_past,
    error_template="Date must be in the past.",
    description="Checks if a date or datetime is in the past.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_in_past(value: date | datetime, info: ValidationInfo) -> bool:
    """Check if a date or datetime is in the past."""
    now = datetime.now(UTC)
    if isinstance(value, datetime):
        return value < now
    return value < now.date()


def is_before(*, timestamp: date | datetime) -> Validator[date | datetime]:
    """Factory for a validator that checks if a date is before a specific point in time."""
    @Build.validator(
        error_template="Date must be before {timestamp}.",
        description="Checks if a date is before a specific point in time.",
        tags=(SYSTEM.INFRA.io,),
        timestamp=timestamp,
    )
    async def _validator(value: date | datetime, info: ValidationInfo) -> bool:
        return value < timestamp

    return _validator


def is_after(*, timestamp: date | datetime) -> Validator[date | datetime]:
    """Factory for a validator that checks if a date is after a specific point in time."""
    @Build.validator(
        error_template="Date must be after {timestamp}.",
        description="Checks if a date is after a specific point in time.",
        tags=(SYSTEM.INFRA.io,),
        timestamp=timestamp,
    )
    async def _validator(value: date | datetime, info: ValidationInfo) -> bool:
        return value > timestamp

    return _validator


@Build.validator(
    register_as=VALID.TEMPORAL.is_weekday,
    error_template="Date must be a weekday.",
    description="Checks if a date is a Monday-Friday.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_weekday(value: date | datetime, info: ValidationInfo) -> bool:
    """Checks if a date is a Monday-Friday."""
    return value.weekday() < 5


@Build.validator(
    register_as=VALID.TEMPORAL.is_weekend,
    error_template="Date must be a weekend.",
    description="Checks if a date is a Saturday or Sunday.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_weekend(value: date | datetime, info: ValidationInfo) -> bool:
    """Checks if a date is a Saturday or Sunday."""
    return value.weekday() >= 5


@Build.validator(
    register_as=VALID.TEMPORAL.is_leap_year,
    error_template="Year in '{value}' must be a leap year.",
    description="Checks if a date/datetime is in a leap year.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_leap_year(value: date | datetime, info: ValidationInfo) -> bool:
    """Checks if a date/datetime is in a leap year."""
    return calendar.isleap(value.year)


@Build.validator(
    register_as=VALID.TEMPORAL.is_timezone_aware,
    error_template="Value must be a timezone-aware datetime.",
    description="Checks if a datetime object has timezone information.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_timezone_aware(value: datetime, info: ValidationInfo) -> bool:
    """Checks if a datetime object has timezone information."""
    return value.tzinfo is not None


@Build.validator(
    register_as=VALID.TEMPORAL.is_iso_format,
    error_template="Value must be a valid ISO 8601 format string.",
    description="Checks if a string is a valid ISO 8601 timestamp.",
    tags=(SYSTEM.INFRA.io,),
)
async def is_iso_format(value: str, info: ValidationInfo) -> bool:
    """Checks if a string is a valid ISO 8601 timestamp."""
    try:
        _ = datetime.fromisoformat(value)
    except (ValueError, TypeError):
        return False
    return True


def is_in_date_range(
    *, start_date: date | datetime, end_date: date | datetime
) -> Validator[date | datetime]:
    """Factory for a validator that checks if a date is within a given range."""
    @Build.validator(
        error_template=f"Date must be between {start_date} and {end_date}.",
        description="Checks if a date is within a given range.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: date | datetime, info: ValidationInfo) -> bool:
        return start_date <= value <= end_date

    return _validator


# --- Timedelta Validators -----------------------------------------------------


@Build.validator(
    register_as=VALID.TEMPORAL.is_positive_duration,
    error_template="Duration must be positive (greater than or equal to zero).",
    description="Checks if a timedelta is positive (>= 0).",
    tags=(SYSTEM.INFRA.io,),
)
async def is_positive_duration(value: timedelta, info: ValidationInfo) -> bool:
    """Check if a timedelta is positive (>= 0)."""
    return value.total_seconds() >= 0


def has_min_duration(*, seconds: float) -> Validator[timedelta]:
    """Factory for a validator that checks if a timedelta meets a minimum duration."""
    @Build.validator(
        error_template=f"Duration must be at least {seconds} seconds.",
        description="Checks if a timedelta meets a minimum duration.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: timedelta, info: ValidationInfo) -> bool:
        return value.total_seconds() >= seconds

    return _validator


def has_max_duration(*, seconds: float) -> Validator[timedelta]:
    """Factory for a validator that checks if a timedelta does not exceed a maximum duration."""
    @Build.validator(
        error_template=f"Duration must not exceed {seconds} seconds.",
        description="Checks if a timedelta does not exceed a maximum duration.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: timedelta, info: ValidationInfo) -> bool:
        return value.total_seconds() <= seconds

    return _validator


def is_duration_between(*, min_seconds: float, max_seconds: float) -> Validator[timedelta]:
    """Factory for a validator that checks if a timedelta is within a given range."""
    @Build.validator(
        error_template=f"Duration must be between {min_seconds} and {max_seconds} seconds.",
        description="Checks if a timedelta is within a given range.",
        tags=(SYSTEM.INFRA.io,),
    )
    async def _validator(value: timedelta, info: ValidationInfo) -> bool:
        total = value.total_seconds()
        return min_seconds <= total <= max_seconds

    return _validator


# --- New Validators Using python-dateutil -------------------------------------


@Build.validator(
    register_as=VALID.TEMPORAL.is_business_hours,
    error_template="Time must be within business hours (9 AM - 5 PM on weekdays).",
    description="Checks if a datetime falls within standard business hours.",
    tags=(SYSTEM.COMMON.time,)
)
async def is_business_hours(value: datetime, info: ValidationInfo) -> bool:
    """
    Check if datetime falls within standard business hours.

    Business hours are defined as:
    - Monday through Friday
    - 9:00 AM to 5:00 PM
    """
    # Check if it's a weekday (Monday = 0, Sunday = 6)
    if value.weekday() >= 5:  # Saturday or Sunday
        return False

    # Check if time is between 9 AM and 5 PM
    hour = value.hour
    return 9 <= hour < 17  # 9 AM to 4:59:59 PM


def matches_rrule(*, rule_string: str) -> Validator[datetime]:
    """
    Factory for a validator that checks if a datetime matches an rrule pattern.

    Args:
        rule_string: An RFC 2445 rrule string (e.g., "FREQ=WEEKLY;BYDAY=MO,WE,FR")
    """
    @Build.validator(
        error_template=f"Date does not match the recurring pattern: {rule_string}.",
        description="Validates if date matches an rrule pattern.",
        tags=(SYSTEM.COMMON.time,),
    )
    async def _validator(value: datetime, info: ValidationInfo) -> bool:
        """Check if the datetime matches the rrule pattern."""
        try:
            # Parse the rrule string
            rule = rrule.rrulestr(rule_string, dtstart=value)

            # Check if the value appears in the first 366 occurrences (covers a year)
            # This is a reasonable limit to avoid infinite sequences

            occurrences: list[datetime] = list(islice(rule, 366))

            # Check if our datetime matches any occurrence by comparing dates
            return any(occ.date() == value.date() for occ in occurrences)
        except (ValueError, TypeError, AttributeError):
            return False

    return _validator


@Build.validator(
    register_as=VALID.TEMPORAL.is_parseable_date,
    error_template="String cannot be parsed as a valid date.",
    description="Checks if a string can be parsed by dateutil.parser.",
    tags=(SYSTEM.COMMON.time,),
)
async def is_parseable_date(value: str, info: ValidationInfo) -> bool:
    """
    Check if string can be parsed as a date by dateutil.parser.

    This is useful for validating user input before attempting to parse it.
    """
    try:
        _ = parser.parse(value, fuzzy=False)
    except (ValueError, parser.ParserError, TypeError):
        return False
    else:
        return True


@Build.validator(
    register_as=VALID.TEMPORAL.is_parseable_date_fuzzy,
    error_template="String cannot be parsed as a date even with fuzzy matching.",
    description="Checks if a string can be parsed by dateutil.parser with fuzzy=True.",
    tags=(SYSTEM.COMMON.time,),
)
async def is_parseable_date_fuzzy(value: str, info: ValidationInfo) -> bool:
    """
    Check if string can be parsed as a date with fuzzy parsing enabled.

    This accepts strings like "Meeting on January 15th at 2:30 PM".
    """
    try:
        _ = parser.parse(value, fuzzy=True)
    except (ValueError, parser.ParserError, TypeError):
        return False
    else:
        return True


def is_within_business_days(*, days: int) -> Validator[datetime]:
    """
    Factory for a validator that checks if a date is within N business days from today.

    Args:
        days: Maximum number of business days from today
    """
    @Build.validator(
        error_template=f"Date must be within {days} business days from today.",
        description=f"Checks if date is within {days} business days from today.",
        tags=(SYSTEM.COMMON.time,)
    )
    async def _validator(value: datetime, info: ValidationInfo) -> bool:
        """Check if the date is within the specified number of business days."""
        today = datetime.now(UTC).replace(hour=0, minute=0, second=0, microsecond=0)
        target = value.replace(hour=0, minute=0, second=0, microsecond=0) if value.tzinfo else value

        if target < today:
            return False  # Past dates are not within future business days

        # Count business days between today and target
        business_days = 0
        current = today

        while current < target and business_days <= days:
            current += timedelta(days=1)
            if current.weekday() < 5:  # Monday through Friday
                business_days += 1

        return business_days <= days

    return _validator


@Build.validator(
    register_as=VALID.TEMPORAL.is_recurring_day,
    error_template="Date is not on the specified day of the week.",
    description="Checks if a date falls on specific days of the week.",
    tags=(SYSTEM.COMMON.time,),
)
async def is_recurring_day(value: tuple[datetime, list[int]], info: ValidationInfo) -> bool:
    """
    Check if a datetime falls on specific days of the week.

    Args:
        value: Tuple of (datetime, list of weekday numbers where Monday=0, Sunday=6)
        info: Validation context (unused)

    Example:
        # Check if date is on Monday, Wednesday, or Friday
        is_recurring_day((datetime.now(), [0, 2, 4]))
    """
    dt, allowed_days = value
    return dt.weekday() in allowed_days


@Build.validator(
    register_as=VALID.TEMPORAL.has_timezone_offset,
    error_template="Datetime must have a specific timezone offset.",
    description="Checks if a datetime has a specific UTC offset.",
    tags=(SYSTEM.COMMON.time,),
)
async def has_timezone_offset(value: tuple[datetime, int], info: ValidationInfo) -> bool:
    """
    Check if a datetime has a specific UTC offset in hours.

    Args:
        value: Tuple of (datetime, expected offset in hours)
        info: Validation context (unused)

    Example:
        # Check if datetime is in EST (UTC-5)
        has_timezone_offset((dt, -5))
    """
    dt, expected_offset = value

    if dt.tzinfo is None:
        return False

    # Get the UTC offset
    offset = dt.utcoffset()
    if offset is None:
        return False

    # Convert to hours
    offset_hours = offset.total_seconds() / 3600
    return offset_hours == expected_offset
