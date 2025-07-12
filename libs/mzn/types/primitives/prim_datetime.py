"""
Title         : prim_datetime.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : libs/mzn/types/primitives/prim_datetime.py.

Description ----------- Primitives for date and time handling.

"""

from __future__ import annotations

import datetime as _dt

from mzn.types._core.core_builders import Build


@Build.primitive(_dt.date)
class PrimDate:
    """
    Immutable date primitive.

    A wrapper around datetime.date that provides enhanced type safety and validation capabilities. This primitive
    represents a calendar date (year, month, day) without time zone or time-of-day information.

    """


@Build.primitive(_dt.time)
class PrimTime:
    """
    Immutable time-of-day primitive.

    A wrapper around datetime.time that provides enhanced type safety and validation capabilities. This primitive
    represents a time of day (hour, minute, second, microsecond) without date or timezone information.

    """


@Build.primitive(_dt.datetime)
class PrimTimestamp:
    """
    Immutable timestamp primitive.

    A wrapper around datetime.datetime that provides enhanced type safety and validation capabilities. This primitive
    represents a specific point in time with date, time, and optionally timezone information.

    """


@Build.primitive(_dt.timedelta)
class PrimTimespan:
    """
    Immutable time duration primitive.

    A wrapper around datetime.timedelta that provides enhanced type safety and validation capabilities. This primitive
    represents a duration or time interval, such as the difference between two timestamps.

    """
