"""
Title         : feat_rules.py
Author        : Bardia Samiee
Project       : Parametric_Arsenal
License       : MIT
Path          : libs/mzn/types/_features/feat_rules.py

Description
-----------
Rule processing feature for validation and normalization.

This module provides a composable mixin that implements the HasRules protocol,
enabling consistent rule processing across all asset types. It integrates with
the existing rule processor utility and provides enhanced functionality.
"""

from __future__ import annotations

from typing import (
    TYPE_CHECKING,
    Annotated,
    Any,
    ClassVar,
    LiteralString,
    TypeVar,
    cast,
)

from mzn.types._contracts.prot_base import Normalizer
from mzn.types._core.core_constants import Sentinel
from mzn.types._core.core_processors import process_rules


if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

    from mzn.types._contracts.prot_assets import JSONLike
    from mzn.types._contracts.prot_base import Rule, RuleProcessingResult
    from mzn.types._contracts.prot_features import RulesProvider
    from mzn.types._core.core_tags import Tag

    T = TypeVar("T")

# --- Mixin Definition ---------------------------------------------------------


class RulesMixin:
    """
    Mixin for adding rule processing to validated assets.

    Provides class-level storage, management, and async processing of validation and normalization rules.
    Integrates with the rule processor utility for async rule categorization and inspection.
    """

    # These attributes are provided by TypeAsset base class when the mixin is applied
    if TYPE_CHECKING:
        mzn_metadata: ClassVar[dict[str, JSONLike]]
        mzn_tags: ClassVar[set[Tag]]

        # Methods provided by MetadataMixin
        @classmethod
        async def get_metadata(cls) -> Mapping[str, JSONLike]:
            """Get metadata mapping."""
            ...

    _inspection_cache: ClassVar[dict[LiteralString, Any] | None] = None
    _processed_rules_cache: ClassVar[RuleProcessingResult | None] = None
    _rules: ClassVar[list[Rule]] = []

    @classmethod
    def _ensure_own_rules_list(cls) -> None:
        """
        Ensures that the class has its own list of rules, copying from its
        parent if necessary. This implements copy-on-write for rule inheritance.
        """
        if "_rules" not in cls.__dict__:
            cls._rules = list(cls._rules)

    @classmethod
    async def add_rule(cls, rule: Annotated[Rule, "Validation or normalization rule"]) -> Annotated[None, "No return"]:
        """Add a validation or normalization rule. Async override of sync base method."""
        if Sentinel.is_sentinel(rule) and rule is Sentinel.NOOP:
            return
        cls._ensure_own_rules_list()
        cls._rules.append(rule)
        await cls.clear_rule_cache()

    @classmethod
    async def add_rules(cls, rules: Annotated[Sequence[Rule], "Multiple rules"]) -> Annotated[None, "No return"]:
        """Add multiple rules at once. Async override of sync base method."""
        if not rules:
            return
        cls._ensure_own_rules_list()
        cls._rules.extend(r for r in rules if not (Sentinel.is_sentinel(r) and r is Sentinel.NOOP))
        await cls.clear_rule_cache()

    @classmethod
    async def process_rules(
        cls,
    ) -> Annotated[RuleProcessingResult, "Processed rules"]:
        """Process rules into validators and normalizers. Async override of sync base method."""
        if cls._processed_rules_cache is not None:
            return cls._processed_rules_cache

        rules = await cls.get_rules()
        try:
            processed = await process_rules(tuple(rules))
        except Exception as e:
            msg = f"Error processing rules for {cls.__name__}"
            raise RuntimeError(msg) from e
        else:
            # Ensure the first element is a list to match RuleProcessingResult
            normalizers, validators = processed
            # Filter normalizers to only include instances of Normalizer
            filtered_normalizers: list[Normalizer[Any, Any]] = [n for n in normalizers if isinstance(n, Normalizer)]  # pyright: ignore[reportUnnecessaryIsInstance]
            cls._processed_rules_cache = (filtered_normalizers, validators)
            return cls._processed_rules_cache

    @classmethod
    async def get_rules(
        cls,
    ) -> Annotated[list[Rule], "Configured rules"]:
        """Get all configured rules, including from parent classes, by traversing the MRO."""
        all_rules: list[Rule] = []
        for base in reversed(cls.__mro__):
            if "_rules" in base.__dict__:
                rules = base.__dict__["_rules"]
                if isinstance(rules, list):
                    all_rules.extend(cast("list[Rule]", rules))
        return all_rules

    @classmethod
    async def clear_rules(
        cls,
    ) -> Annotated[None, "No return"]:
        """Clear all rules defined on this specific class, not from parent classes."""
        cls._rules = []
        await cls.clear_rule_cache()

    @classmethod
    async def clear_rule_cache(
        cls,
    ) -> Annotated[None, "No return"]:
        """Clear the processed rules cache."""
        cls._processed_rules_cache = None
        cls._inspection_cache = None

    @classmethod
    async def has_rules(
        cls,
    ) -> Annotated[bool, "Whether rules exist"]:
        """Check if the class has any rules configured. Async override of sync base method."""
        return bool(await cls.get_rules())

    @classmethod
    async def get_rule_count(
        cls,
    ) -> Annotated[int, "Number of rules"]:
        """Get the number of configured rules. Async override of sync base method."""
        return len(await cls.get_rules())

    @classmethod
    async def get_normalizer_count(
        cls,
    ) -> Annotated[int, "Number of processed normalizer rules."]:
        """Get the number of normalizers after processing."""
        if result := await cls.process_rules():
            normalizers, _ = result
            return len(normalizers)
        return 0

    @classmethod
    async def get_validator_count(
        cls,
    ) -> Annotated[int, "Number of processed validator rules."]:
        """Get the number of validators after processing."""
        if result := await cls.process_rules():
            _, validators = result
            return len(validators)
        return 0

    @classmethod
    async def inspect_rules(
        cls,
    ) -> Annotated[dict[LiteralString, Any], "A dictionary with rule inspection data."]:
        """Inspect the asset's rules."""
        if cls._inspection_cache is not None:
            return cls._inspection_cache

        rules = await cls.get_rules()
        try:
            normalizers, validators = await cls.process_rules()
        except ValueError:
            normalizers, validators = [], []

        raw_rules: Annotated[list[dict[LiteralString, Any]], "Raw rules"] = []
        processed_normalizers: Annotated[list[dict[LiteralString, Any]], "Processed normalizers"] = []
        processed_validators: Annotated[list[dict[LiteralString, Any]], "Processed validators"] = []
        inspection_data: Annotated[dict[LiteralString, Any], "Inspection data"] = {
            "total_rules": len(rules),
            "normalizer_count": len(normalizers),
            "validator_count": len(validators),
            "raw_rules": raw_rules,
            "processed_normalizers": processed_normalizers,
            "processed_validators": processed_validators,
        }
        sentinels = {Sentinel.DISABLED, Sentinel.DEFER, Sentinel.NOOP}
        for rule in rules:
            rule_info: Annotated[dict[LiteralString, Any], "Rule info"] = {}
            rule_info["sentinel"] = Sentinel.is_sentinel(rule) and rule in sentinels
            rule_info["name"] = getattr(rule, "__name__", str(rule))
            rule_info["type"] = type(rule).__name__
            rule_info["module"] = getattr(type(cast("object", rule)), "__module__", "unknown")
            rule_info["repr"] = repr(rule)
            if (doc := getattr(rule, "__doc__", None)) is not None:
                rule_info["doc"] = doc
            if (config := getattr(rule, "_config", None)) is not None:
                rule_info["config"] = config
            raw_rules.append(rule_info)
        for i, normalizer in enumerate(normalizers):
            norm_info: Annotated[dict[LiteralString, Any], "Normalizer info"] = {
                "index": i,
                "type": type(normalizer).__name__,
                "repr": repr(normalizer),
            }
            processed_normalizers.append(norm_info)
        for i, validator in enumerate(validators):
            val_info: Annotated[dict[LiteralString, Any], "Validator info"] = {
                "index": i,
                "type": type(validator).__name__,
                "repr": repr(validator),
            }
            processed_validators.append(val_info)

        cls._inspection_cache = inspection_data
        return inspection_data

    @classmethod
    async def validate_rule_configuration(
        cls,
    ) -> Annotated[dict[LiteralString, Any], "Results of rule-configuration validation."]:
        """Validate that the rule configuration is correct."""
        errors: list[BaseException] = []
        warnings: Annotated[list[str], "Warning list"] = []
        results: Annotated[dict[LiteralString, Any], "Results dict"] = {
            "valid": True,
            "errors": errors,
            "warnings": warnings,
            "rule_count": await cls.get_rule_count(),
        }
        try:
            _ = await cls.process_rules()
        except (ValueError, TypeError) as e:
            results["valid"] = False
            msg = f"Rule processing failed: {e}"
            raise RuntimeError(msg) from e
        return results

    @classmethod
    async def copy_rules_from(
        cls,
        source_cls: Annotated[RulesProvider, "Source class to copy rules from."],
    ) -> Annotated[None, "No return"]:
        """Copy rules from another class into this one."""
        if hasattr(source_cls, "get_rules"):
            try:
                source_rules = await source_cls.get_rules()
                await cls.add_rules(source_rules)
            except (ValueError, TypeError, AttributeError) as e:
                name = getattr(source_cls, "__name__", str(source_cls))
                msg = f"Failed to copy rules from {name}"
                raise RuntimeError(msg) from e

    @classmethod
    async def merge_rules_from(
        cls,
        *source_classes: Annotated[RulesProvider, "Source classes to merge rules from."],
    ) -> Annotated[None, "No return"]:
        """Merge rules from multiple source classes into this one."""
        all_rules: Annotated[list[Rule], "All rules"] = []
        for source_cls in source_classes:
            if hasattr(source_cls, "get_rules"):
                get_rules_method = source_cls.get_rules
                if callable(get_rules_method):
                    try:
                        source_rules = await get_rules_method()
                        all_rules.extend(source_rules)
                    except (ValueError, TypeError, AttributeError):
                        # Skip rules from this source class due to error
                        pass
        await cls.add_rules(all_rules)
