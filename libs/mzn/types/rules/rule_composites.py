"""
Title         : rule_composites.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/rules/rule_composites.py

Description
-----------
Composite validation rules for combining other rules with logical operators.
"""

from __future__ import annotations

from collections.abc import Awaitable, Callable, Sequence
from typing import Annotated, Any, cast, override

from pydantic import ValidationInfo

from mzn.types._contracts.prot_base import (
    Normalizer,
    Rule,
    Validator,
    is_normalizer,
    is_validator,
)
from mzn.types._core.core_processors import function_validator_factory


# --- Constants ----------------------------------------------------------------

ValidatorCallable = Callable[[Any, ValidationInfo], Awaitable[bool]]

# --- Base Composite Rule ------------------------------------------------------


class BaseCompositeRule(Validator[Any]):
    """Base class for composite rules to handle common initialization."""

    __slots__ = ("error_template", "rules")

    rules: Annotated[
        Sequence[Validator[Any]],
        "The sequence of validators that make up this composite rule."
    ]
    error_template: Annotated[
        str | None,
        "A custom error message template for the composite rule."
    ]

    def __init__(
        self,
        *rules: Annotated[Rule, "A sequence of rules to compose."],
        error_template: Annotated[str | None, "A custom error message template."] = None,
    ):
        """
        Initialize the composite rule with a sequence of rules and an optional error template.

        Args:
            *rules: A sequence of rules to compose.
            error_template: An optional custom error message template.
        """
        super().__init__()
        processed_rules: list[Validator[Any]] = []
        for r in rules:
            if is_validator(r):
                # TypeGuard ensures r is Validator[Any] here (Python 3.13+)
                processed_rules.append(r)
            elif is_normalizer(r):
                # TypeGuard ensures r is Normalizer[Any, Any] here (Python 3.13+)
                msg = f"Cannot use a {Normalizer.__name__} in a validation-only composite rule. Got: {r!r}"
                raise TypeError(msg)
            elif callable(r):
                processed_rules.append(function_validator_factory(cast("ValidatorCallable", r)))
            else:
                msg = f"Unsupported rule type in composite: {type(r)}"
                raise TypeError(msg)
        self.rules = processed_rules
        self.error_template = error_template

    @override
    async def __call__(
        self,
        value: Annotated[Any, "The value to validate."],
        info: Annotated[ValidationInfo, "Pydantic's validation context."],
    ) -> Any:
        """Abstract call method to satisfy the Validator protocol."""
        msg = "Composite rule must implement __call__."
        raise NotImplementedError(msg)

# --- Or Rule ------------------------------------------------------------------


class Or(BaseCompositeRule):
    """
    A composite validator that succeeds if at least one of its sub-rules succeeds.

    The validation stops at the first successful sub-rule. If all sub-rules fail,
    this validator fails.
    """

    @override
    async def __call__(
        self,
        value: Annotated[Any, "The value to validate."],
        info: Annotated[ValidationInfo, "Pydantic's validation context."],
    ) -> Any:
        """Execute sub-rules until one succeeds."""
        errors: list[str] = []
        for rule in self.rules:
            try:
                await rule(value, info)
            except ValueError as e:
                errors.append(str(e))
            else:
                return value  # Success, short-circuit

        # If all rules failed, raise a consolidated error
        error_message = self.error_template or "Value did not match any of the required patterns."
        detailed_errors = "; ".join(errors)
        msg = f"{error_message} (Details: {detailed_errors})"
        raise ValueError(msg)

# --- And Rule -----------------------------------------------------------------


class And(BaseCompositeRule):
    """
    A composite validator that succeeds only if all of its sub-rules succeed.

    This makes the default sequential validation behavior explicit and allows for grouping.

    """

    @override
    async def __call__(
        self,
        value: Annotated[Any, "The value to validate."],
        info: Annotated[ValidationInfo, "Pydantic's validation context."],
    ) -> Any:
        """Execute all sub-rules sequentially."""
        for rule in self.rules:
            # Any exception from a sub-rule will naturally bubble up and fail validation
            await rule(value, info)
        return value

# --- Not Rule -----------------------------------------------------------------


class Not(Validator[Any]):
    """A composite validator that succeeds if its sub-rule fails."""

    __slots__ = ("error_template", "rule")

    rule: Annotated[
        Validator[Any],
        "The validator to negate."
    ]
    error_template: Annotated[
        str | None,
        "A custom error message template for the Not rule."
    ]

    def __init__(
        self,
        rule: Annotated[Rule, "The rule to negate."],
        error_template: Annotated[str | None, "A custom error message template."] = None,
    ):
        """
        Initialize the Not validator with a rule to negate and an optional error template.

        Args:
            rule: The rule to negate.
            error_template: An optional custom error message template.
        """
        super().__init__()
        if is_validator(rule):
            self.rule = rule
        elif is_normalizer(rule):
            msg = f"Cannot use a {Normalizer.__name__} in a validation-only composite rule. Got: {rule!r}"
            raise TypeError(msg)
        elif callable(rule):
            self.rule = function_validator_factory(cast("ValidatorCallable", rule))
        else:
            msg = f"Unsupported rule type in composite: {type(rule)}"
            raise TypeError(msg)
        self.error_template = error_template

    @override
    async def __call__(
        self,
        value: Annotated[Any, "The value to validate."],
        info: Annotated[ValidationInfo, "Pydantic's validation context."],
    ) -> Any:
        """Execute the sub-rule and invert the result."""
        try:
            await self.rule(value, info)
        except ValueError:
            return value  # The rule failed as expected, so the 'Not' validator succeeds.

        # If the rule succeeded, the 'Not' validator must fail.
        error_message = self.error_template or "Value was expected to fail validation, but it succeeded."
        raise ValueError(error_message)
