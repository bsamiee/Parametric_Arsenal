"""
Title         : rules.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/rules/rules.py.

Description ----------- Hierarchical, self-registering, and callable Enum-based system for all rules. Enhanced with
dynamic member creation similar to the tags system. Reorganized with core atomic rules and improved categorization.

"""

from __future__ import annotations

import contextlib
import importlib
from pathlib import Path
from typing import (
    TYPE_CHECKING,
    Annotated,
    Any,
    ParamSpec,
    Protocol,
    TypeIs,
    TypeVar,
    cast,
)

from aenum import Enum, EnumMeta, auto

from mzn.types._contracts.prot_base import (
    Normalizer,
    Rule as BaseRule,
    Validator,
)
from mzn.types._core.core_processors import FunctionNormalizer, FunctionValidator


if TYPE_CHECKING:
    from collections.abc import Callable


# --- Type Variables -----------------------------------------------------------

T_in = TypeVar("T_in")
T_out = TypeVar("T_out")
P = ParamSpec("P")
R_co = TypeVar("R_co", bound=BaseRule, covariant=True)

# --- Static Type Hinting for Dynamic Rules ------------------------------------


class CallableRule(Protocol[P, R_co]):
    """A generic protocol for a callable that returns a rule, preserving parameter types."""

    def __call__(self, *args: P.args, **kwargs: P.kwargs) -> R_co:
        """Allow the enum member to be called as a function, returning a rule instance."""
        ...


def define_rule() -> CallableRule[..., BaseRule]:
    """
    A factory function to bridge the gap between runtime `auto()` and static analysis.

    During static analysis (when `TYPE_CHECKING` is true), this function is treated as returning a `CallableRule`, which
    satisfies the type checker that the enum member is callable and returns a valid rule. At runtime, it returns
    `aenum.auto()`, allowing for dynamic member creation.

    """
    if TYPE_CHECKING:

        def placeholder(*_: Any, **__: Any) -> BaseRule:
            """A placeholder function to satisfy the type checker."""
            return cast("BaseRule", None)

        return placeholder

    # At runtime, return the actual auto() object for aenum to handle.
    return auto()


# --- Type Guards --------------------------------------------------------------

def is_rule_factory(
    obj: Validator[Any] | Normalizer[Any, Any] | Callable[..., Validator[Any] | Normalizer[Any, Any]]
) -> TypeIs[Callable[..., Validator[Any] | Normalizer[Any, Any]]]:
    """Type guard to check if an object is a rule factory (not a rule instance)."""
    return callable(obj) and not isinstance(obj, (Validator, Normalizer))


def is_rule_instance(
    obj: Validator[Any] | Normalizer[Any, Any] | Callable[..., Validator[Any] | Normalizer[Any, Any]]
) -> TypeIs[Validator[Any] | Normalizer[Any, Any]]:
    """Type guard to check if an object is a rule instance (not a factory)."""
    return isinstance(obj, (Validator, Normalizer))


# --- Rule Map -----------------------------------------------------------------

# The private registry mapping Enum members to their actual rule instances.
# This is populated at import time by the @Build.validator/normalizer decorators.
_rule_map: dict[Rule, Validator[Any] | Normalizer[Any, Any] | Callable[..., Validator[Any] | Normalizer[Any, Any]]] = {}


def register_rule(
    key: Any,  # noqa: ANN401 # Accept Any - at runtime these are Rule enum members
    rule: Annotated[
        Validator[Any]
        | Normalizer[Any, Any]
        | Callable[..., Validator[Any] | Normalizer[Any, Any]],
        "The rule instance to associate with the enum member."
    ]
) -> None:
    """
    Registers a rule instance against its corresponding Enum member.

    This function is called by the @Build decorators.

    """
    if key in _rule_map:
        # Warning: Overwriting rule for key
        pass
    _rule_map[key] = rule


# --- Dynamic Rule Metaclass ---------------------------------------------------


class _RuleMeta(EnumMeta):
    """
    Metaclass for dynamic rule creation, similar to the tags system.

    This metaclass enables creating enum members on-demand when they're accessed but don't exist, allowing for dynamic
    registration of rules without pre-defining them in this file.

    """

    def __new__(
        cls,
        name: str,
        bases: tuple[type, ...],
        classdict: dict[str, Any],
        **kwds: Any,
    ) -> type:
        """Create the new enum class and set up dynamic capabilities."""
        # Find and link nested rule classes before creating the enum
        nested_rules: dict[str, type] = {
            key: value
            for key, value in classdict.items()
            if isinstance(value, type) and len(bases) > 0 and issubclass(value, bases[0])
        }

        # Create the enum class as usual
        new_cls = super().__new__(cls, name, bases, classdict, **kwds)

        # Store nested classes for dynamic access
        if hasattr(new_cls, "__dict__"):
            # This protected attribute is intentionally used for internal bookkeeping.
            # Cast to Any to satisfy mypy since metaclass internals are complex
            cast("Any", new_cls)._nested_rule_classes = nested_rules  # noqa: SLF001

        return new_cls

    def __getattr__(self: type, name: str) -> Rule | type[Rule]:
        """
        Handle dynamic member creation when accessing non-existent enum members.

        This is called when someone tries to access an attribute that doesn't exist on the enum class, allowing us to
        create enum members on-demand.

        """
        # First check if it's an existing member
        members = getattr(self, "__members__", {})
        if name in members:
            return cast("Rule | type[Rule]", members[name])

        # Check if it's a nested class
        nested_classes = getattr(self, "_nested_rule_classes", {})
        for class_name, nested_cls in nested_classes.items():
            if class_name.lower() == name.lower():
                return cast("Rule | type[Rule]", nested_cls)

        # For now, raise AttributeError for dynamic creation until we implement
        # the full dynamic enum member creation properly
        msg = f"{self.__name__!r} object has no attribute {name!r}"
        raise AttributeError(msg)

# --- Rule Enum ----------------------------------------------------------------


class Rule(Enum, metaclass=_RuleMeta):
    """
    Base class for the hierarchical, callable rule enum.

    Provides the dynamic __call__ method to delegate to the real rule. Enhanced with dynamic member creation
    capabilities.

    """

    @classmethod
    def __class_getitem__(cls, params: tuple[type, ...] | type) -> type:
        """Support for Rule[T_in, T_out] notation, returning the class itself."""
        return cls

    def _get_module_path(self) -> str | None:
        """Extract module path from the rule's qualified name."""
        qualname = type(self).__qualname__

        if "." not in qualname:
            return None

        parts = qualname.split(".")
        if len(parts) != 2:
            return None

        rule_type_upper, category_upper = parts
        rule_type = rule_type_upper.lower()
        category = category_upper.lower()

        if rule_type not in {"valid", "norm"}:
            return None

        module_dir = "validators" if rule_type == "valid" else "normalizers"
        module_name = f"{rule_type}_{category}"

        if category in {"cache", "log"}:
            return f"mzn.types.rules.{module_dir}.core.{module_name}"
        return f"mzn.types.rules.{module_dir}.{module_name}"

    @staticmethod
    def _register_module_functions(module: Any) -> None:  # noqa: ANN401
        """Register factory functions from a module by scanning ALL enum members."""
        # Get all enum members from both VALID and NORM hierarchies
        all_enum_members: dict[str, Any] = {}

        def collect_enum_members(enum_class: type, prefix: str = "") -> None:
            """Recursively collect all enum members from nested enum classes."""
            # First collect direct members if this class has them
            if hasattr(enum_class, "__members__"):
                members_dict = getattr(enum_class, "__members__", {})
                all_enum_members.update(members_dict)
            # Then look for nested enum classes using __dict__ since dir() doesn't show them
            if hasattr(enum_class, "__dict__"):
                for attr_name, attr in enum_class.__dict__.items():
                    if attr_name.startswith("_"):
                        continue

                    try:
                        # Check if this is a nested enum class (has __members__ attribute)
                        if hasattr(attr, "__members__") and isinstance(attr, type):
                            # This is a nested enum class, recurse
                            collect_enum_members(attr, f"{prefix}{attr_name}.")
                    except (TypeError, AttributeError):
                        # Skip attributes that can't be accessed
                        continue

        # Collect from both VALID and NORM hierarchies
        for parent_enum in [globals().get("VALID"), globals().get("NORM")]:
            if parent_enum:
                collect_enum_members(parent_enum)

        # Now scan module functions and match by name
        for attr_name in dir(module):
            if attr_name.startswith("_"):
                continue

            func = getattr(module, attr_name)
            if callable(func) and not isinstance(func, type) and attr_name in all_enum_members:
                enum_member = all_enum_members[attr_name]
                if enum_member not in _rule_map:
                    typed_func = cast(
                        "Callable[..., Validator[Any] | Normalizer[Any, Any]]",
                        func
                    )
                    register_rule(enum_member, typed_func)

    @classmethod
    def _load_all_rules(cls) -> None:
        """
        Load ALL rule modules from validators/ and normalizers/ directories.

        This discovers and imports every .py file, letting @Build decorators register themselves automatically. Also
        registers factory functions by name matching. File names are completely irrelevant - we only care about the enum
        hierarchy.

        """
        # Get the rules directory
        rules_dir = Path(__file__).parent

        for module_dir in ["validators", "normalizers"]:
            dir_path = rules_dir / module_dir
            if not dir_path.exists():
                continue

            # Find all .py files recursively
            for py_file in dir_path.rglob("*.py"):
                if py_file.name.startswith("_"):
                    continue

                # Convert file path to module name
                rel_path = py_file.relative_to(rules_dir)
                module_name = str(rel_path.with_suffix("")).replace("/", ".")
                full_module = f"mzn.types.rules.{module_name}"

                # Import the module - decorators will auto-register
                with contextlib.suppress(ImportError):
                    module = importlib.import_module(full_module)
                    # Register any factory functions found in this module
                    # This ignores file names completely and only looks at function names
                    cls._register_module_functions(module)

    @classmethod
    def _ensure_rules_loaded(cls) -> None:
        """Ensure all rules are loaded from modules."""
        # Simple: just load everything once
        if not hasattr(cls, "_rules_loaded"):
            cls._load_all_rules()
            # Use setattr to avoid type checker complaints about dynamic attributes
            setattr(cls, "_rules_loaded", True)  # noqa: B010

    def __call__(self, *args: Any, **kwargs: Any) -> Validator[Any] | Normalizer[Any, Any]:
        """Unified dispatch for both factory and direct patterns with lazy loading."""
        rule_or_factory = _rule_map.get(self)

        if rule_or_factory is None:
            # Try to lazy-load the module that contains this rule
            type(self)._ensure_rules_loaded()  # noqa: SLF001
            rule_or_factory = _rule_map.get(self)

            if rule_or_factory is None:
                msg = f"Rule '{self!s}' is not registered."
                raise LookupError(msg)

        # Case 1: Factory function (callable that's not a concrete rule instance)
        if callable(rule_or_factory) and not isinstance(rule_or_factory, (FunctionValidator, FunctionNormalizer)):
            try:
                result = rule_or_factory(*args, **kwargs)
            except TypeError as e:
                msg = f"Invalid arguments for factory '{self!s}': {e}"
                raise TypeError(msg) from e
            else:
                # Ensure the factory returned a valid rule instance
                if not isinstance(result, (FunctionValidator, FunctionNormalizer)):
                    msg = f"Factory '{self!s}' returned invalid type: {type(result).__name__}"
                    raise TypeError(msg)
                return cast("Validator[Any] | Normalizer[Any, Any]", result)

        # Case 2: Direct validator/normalizer instance
        elif isinstance(rule_or_factory, (FunctionValidator, FunctionNormalizer)):  # pyright: ignore[reportUnnecessaryIsInstance]
            if args or kwargs:
                msg = f"Rule '{self!s}' is not configurable."
                raise ValueError(msg)
            return cast("Validator[Any] | Normalizer[Any, Any]", rule_or_factory)

        # Case 3: Unknown type
        else:
            msg = f"Rule '{self!s}' has invalid registration type: {type(rule_or_factory).__name__}"
            raise TypeError(msg)


# --- Validation Rules ---------------------------------------------------------

class VALID(Rule):
    """Hierarchical namespace for all validation rules."""

    class CORE(Rule):
        """Core foundational validation rules."""

        # Comparison operations
        is_equal_to = define_rule()
        is_not_equal_to = define_rule()
        is_one_of = define_rule()
        is_not_one_of = define_rule()

        # Type checking
        is_instance_of = define_rule()
        is_one_of_types = define_rule()
        has_attribute = define_rule()
        is_callable = define_rule()
        is_hashable = define_rule()
        is_json_serializable = define_rule()

        # Predicates
        satisfies_predicate = define_rule()

    class COLLECTIONS(Rule):
        """Validation rules for collections."""

        # Structure validation
        has_size = define_rule()
        is_not_empty = define_rule()
        has_length_between = define_rule()
        is_nested_dict = define_rule()
        has_depth = define_rule()
        has_no_cycles = define_rule()

        # Content validation
        items_are_unique = define_rule()
        has_required_keys = define_rule()
        contains_item = define_rule()
        all_items_are_of_type = define_rule()
        has_key = define_rule()
        has_no_duplicates = define_rule()
        is_subset_of = define_rule()
        is_superset_of = define_rule()
        is_homogeneous = define_rule()
        is_disjoint_from = define_rule()

        # Predicates
        satisfies_predicate = define_rule()
        all_satisfy = define_rule()
        any_satisfy = define_rule()

        # Additional collection validation
        keys_match_pattern = define_rule()
        values_are_serializable = define_rule()

    class STRING(Rule):
        """Validation rules for text/string values (renamed from STRING)."""

        # Basic string properties
        has_length = define_rule()
        is_alpha = define_rule()
        is_alnum = define_rule()
        is_lowercase = define_rule()
        is_uppercase = define_rule()
        is_titlecase = define_rule()
        is_ascii = define_rule()
        is_ascii_printable = define_rule()
        has_no_leading_trailing_whitespace = define_rule()

        # Pattern matching
        matches_pattern = define_rule()
        contains_substring = define_rule()
        is_palindrome = define_rule()
        has_balanced_brackets = define_rule()
        contains_only = define_rule()

        # Format validation
        is_email = define_rule()
        is_credit_card = define_rule()
        is_isbn = define_rule()

        # Encoding validation
        matches_language = define_rule()
        is_semver = define_rule()
        is_slug = define_rule()

        # Additional string validation
        does_not_end_with = define_rule()
        no_consecutive_chars = define_rule()
        is_internal_prefix = define_rule()

    class NUMERIC(Rule):
        """Validation rules for numeric values."""

        # Range validation
        is_in_range = define_rule()
        is_greater_than = define_rule()
        is_less_than = define_rule()
        is_between_exclusive = define_rule()

        # Sign validation
        is_positive = define_rule()
        is_non_negative = define_rule()  # Includes zero, unlike is_positive
        is_negative = define_rule()

        # Mathematical properties
        is_multiple_of = define_rule()
        is_even = define_rule()
        is_odd = define_rule()
        is_prime = define_rule()
        is_power_of = define_rule()

        # Special values
        is_finite = define_rule()
        is_nan = define_rule()
        is_integer_value = define_rule()

        # Geographic
        is_latitude = define_rule()
        is_longitude = define_rule()

        # Precision
        has_precision = define_rule()

        # Decimal
        has_max_digits = define_rule()
        has_max_decimal_places = define_rule()
        is_quantized = define_rule()

    class TEMPORAL(Rule):
        """Validation rules for datetime/temporal values (renamed from DATETIME)."""

        # Temporal relationships
        is_in_future = define_rule()
        is_in_past = define_rule()
        is_before = define_rule()
        is_after = define_rule()

        # Day/time properties
        is_weekday = define_rule()
        is_weekend = define_rule()
        is_leap_year = define_rule()

        # Timezone and format
        is_timezone_aware = define_rule()
        is_iso_format = define_rule()
        is_in_date_range = define_rule()

        # Timedelta/duration validation
        has_min_duration = define_rule()
        has_max_duration = define_rule()
        is_duration_between = define_rule()
        is_positive_duration = define_rule()

        # Business hours and days validation (using dateutil)
        is_business_hours = define_rule()
        is_within_business_days = define_rule()

        # Recurring patterns (using dateutil)
        matches_rrule = define_rule()
        is_recurring_day = define_rule()

        # Parsing validation (using dateutil)
        is_parseable_date = define_rule()
        is_parseable_date_fuzzy = define_rule()

        # Timezone offset validation (using dateutil)
        has_timezone_offset = define_rule()

    class DATA(Rule):
        """Validation rules for data values."""

        # Data formats
        is_json = define_rule()
        is_yaml = define_rule()
        is_toml = define_rule()
        is_csv_row = define_rule()
        has_schema = define_rule()
        is_base64 = define_rule()
        is_hex_encoded = define_rule()
        is_xml_well_formed = define_rule()

        # Binary data validation
        has_prefix = define_rule()
        has_suffix = define_rule()
        is_compressed_format = define_rule()
        is_within_size_range = define_rule()
        compression_ratio_acceptable = define_rule()

        # Archive and file signature detection (using python-magic)
        is_archive_format = define_rule()
        has_file_signature = define_rule()

    class FILESYSTEM(Rule):
        """Validation rules for file system components."""

        # File system properties
        exists = define_rule()
        is_file = define_rule()
        is_dir = define_rule()
        has_extension = define_rule()
        is_absolute = define_rule()
        is_relative = define_rule()
        is_readable = define_rule()
        is_writable = define_rule()

        # Path properties
        has_valid_path = define_rule()
        is_symlink = define_rule()
        is_hidden = define_rule()
        parent_exists = define_rule()

        # File content validation
        has_valid_mime_type = define_rule()
        has_valid_encoding = define_rule()
        has_min_size = define_rule()
        has_max_size = define_rule()

        # File type detection (using python-magic)
        is_text_file = define_rule()
        is_binary_file = define_rule()
        is_media_file = define_rule()
        is_document_file = define_rule()
        matches_extension = define_rule()
        is_encrypted_file = define_rule()

    class PROTOCOLS(Rule):
        """Validation rules for protocols and formats."""

        # Network protocols
        is_ipv4 = define_rule()
        is_ipv6 = define_rule()
        is_ip_address = define_rule()
        is_public_ip = define_rule()
        is_private_ip = define_rule()
        is_port_number = define_rule()
        is_valid_port_range = define_rule()
        is_hostname = define_rule()
        is_domain_name = define_rule()
        is_mac_address = define_rule()
        is_cidr = define_rule()
        is_url = define_rule()

        # Web formats
        is_css_selector = define_rule()
        is_sql_identifier = define_rule()

        # UUID
        is_uuid = define_rule()
        is_uuid_version = define_rule()

    class SECURITY(Rule):
        """Security-related validation rules."""

        # Password validation
        is_strong_password = define_rule()

        # Token and authentication validation
        is_jwt = define_rule()
        is_hmac = define_rule()

        # Data security validation
        has_no_sensitive_info = define_rule()

    class DOMAINS(Rule):
        """Domain-specific validation rules."""

        class CACHE(Rule):
            """Cache domain validation rules."""

            # Key validation
            is_valid_cache_key = define_rule()
            has_valid_namespace = define_rule()
            has_key_prefix = define_rule()
            key_within_length_limit = define_rule()

            # TTL validation
            is_valid_ttl_range = define_rule()
            has_ttl_jitter_range = define_rule()

            # Tag validation
            is_valid_tag_pattern = define_rule()
            has_valid_tag_hierarchy = define_rule()

            # Configuration validation
            has_compatible_features = define_rule()
            eviction_policy_supported = define_rule()

        class LOG(Rule):
            """Log domain validation rules."""

            # Level validation
            is_valid_level_transition = define_rule()

            # Context validation
            has_valid_context_depth = define_rule()
            has_valid_context_size = define_rule()

            # Message validation
            is_safe_for_output = define_rule()

            # Configuration validation
            has_valid_handler_chain = define_rule()
            has_valid_rotation_config = define_rule()
            is_valid_correlation_id = define_rule()
            has_valid_batch_config = define_rule()
            is_valid_timestamp_format = define_rule()


# --- Normalization Rules ------------------------------------------------------


class NORM(Rule):
    """Hierarchical namespace for all normalization rules."""

    class CORE(Rule):
        """Core foundational normalization rules."""

        # Type conversions
        ensure_type = define_rule()
        to_bool = define_rule()
        to_decimal = define_rule()
        ensure_list = define_rule()

        # Conditionals
        apply_if_not_none = define_rule()
        fallback_to = define_rule()
        apply_if = define_rule()

        # Composition
        chain = define_rule()
        select_by_type = define_rule()

        # Cleanup operations
        strip_whitespace = define_rule()
        to_lowercase = define_rule()
        to_uppercase = define_rule()
        remove_none_values = define_rule()
        remove_empty_strings = define_rule()
        empty_string_to_none = define_rule()
        normalize_whitespace = define_rule()

    class COLLECTIONS(Rule):
        """Normalization rules for collections."""

        # Structure operations
        sort_list = define_rule()
        remove_duplicates = define_rule()
        flatten = define_rule()
        reverse = define_rule()
        shuffle = define_rule()
        chunk = define_rule()
        interleave = define_rule()

        # Access operations
        get_value = define_rule()
        filter_by_key = define_rule()
        join_list = define_rule()
        group_by = define_rule()
        deep_merge = define_rule()

        # Additional collection normalization
        lowercase_keys = define_rule()

    class STRING(Rule):
        """Normalization rules for text/string values (renamed from STRING)."""

        # Case transformations
        to_lowercase = define_rule()
        to_uppercase = define_rule()
        capitalize_words = define_rule()
        to_title_case = define_rule()

        # Format transformations
        to_snake_case = define_rule()
        to_camel_case = define_rule()
        to_kebab_case = define_rule()
        to_pascal_case = define_rule()

        # Content transformations
        strip_whitespace = define_rule()
        remove_punctuation = define_rule()
        truncate = define_rule()
        replace = define_rule()
        pad_left = define_rule()
        reverse = define_rule()

        # Encoding transformations
        normalize_unicode = define_rule()
        ensure_ascii = define_rule()
        limit_words = define_rule()

        # Cleanup transformations
        empty_string_to_none = define_rule()
        normalize_whitespace = define_rule()
        slugify = define_rule()
        strip_html = define_rule()

        # Additional string normalization
        deduplicate_separators = define_rule()
        remove_control_characters = define_rule()
        hash_if_exceeds_length = define_rule()

    class NUMERIC(Rule):
        """Normalization rules for numeric values."""

        # Mathematical operations
        round_to = define_rule()
        absolute_value = define_rule()
        floor = define_rule()
        ceiling = define_rule()
        round_to_precision = define_rule()

        # Range operations
        clamp = define_rule()
        snap_to_grid = define_rule()

        # Type conversions
        to_int = define_rule()
        to_float = define_rule()

        # Decimal normalization
        quantize = define_rule()

    class TEMPORAL(Rule):
        """Normalization rules for temporal values (renamed from DATETIME)."""

        # Timezone operations
        to_timezone = define_rule()
        to_utc = define_rule()

        # Formatting operations
        prepend_timestamp = define_rule()
        format_datetime = define_rule()

        # Snapping operations
        snap_to_nearest_minute = define_rule()
        to_unix_timestamp = define_rule()
        from_unix_timestamp = define_rule()

        # Human-readable parsing (using dateutil)
        parse_human_date = define_rule()
        parse_duration = define_rule()

        # Business day operations (using dateutil)
        add_business_days = define_rule()
        to_business_day = define_rule()

        # Recurrence operations (using dateutil)
        next_occurrence = define_rule()

        # Month operations (using dateutil)
        round_to_month_boundary = define_rule()

        # Timezone parsing (using dateutil)
        parse_timezone_name = define_rule()

    class DATA(Rule):
        """Validation rules for data values."""

        # Data format normalization
        to_json = define_rule()
        from_json = define_rule()
        to_yaml = define_rule()
        from_yaml = define_rule()
        to_base64 = define_rule()
        from_base64 = define_rule()

        # Binary data normalization
        add_prefix = define_rule()
        add_suffix = define_rule()
        compress_if_large = define_rule()
        add_compression_header = define_rule()

        # File type detection (using python-magic)
        detect_mime_type = define_rule()
        detect_file_type = define_rule()
        extract_file_metadata = define_rule()

    class FILESYSTEM(Rule):
        """Validation rules for file system components."""

        # Path normalization
        resolve_path = define_rule()
        normalize_path = define_rule()
        to_posix_style = define_rule()

        # File content normalization
        ensure_file_extension = define_rule()
        ensure_directory_structure = define_rule()

        # File system operations
        ensure_file_exists = define_rule()
        ensure_directory_exists = define_rule()
        ensure_readable = define_rule()
        ensure_writable = define_rule()
        ensure_executable = define_rule()

        # File extension management (using python-magic)
        ensure_correct_extension = define_rule()
        add_extension_from_content = define_rule()

    class PROTOCOLS(Rule):
        """Normalization rules for protocols and formats."""

        # Network normalization
        normalize_ipv4 = define_rule()
        normalize_ipv6 = define_rule()
        normalize_url = define_rule()
        normalize_mac_address = define_rule()
        ensure_scheme = define_rule()
        normalize_hostname = define_rule()

        # Binary operations
        compress = define_rule()
        decompress = define_rule()

        # UUID normalization
        generate_if_none = define_rule()
        to_urn_format = define_rule()

    class SECURITY(Rule):
        """Security-related normalization rules."""

        # PII masking
        mask_pii = define_rule()
        redact_keywords = define_rule()
        partial_mask = define_rule()

    class DOMAINS(Rule):
        """Domain-specific normalization rules."""

        class CACHE(Rule):
            """Cache domain normalization rules."""

            # Key normalization
            normalize_cache_key = define_rule()
            hash_if_too_long = define_rule()
            add_namespace_prefix = define_rule()
            deduplicate_separators = define_rule()

            # TTL normalization
            add_ttl_jitter = define_rule()
            normalize_ttl_value = define_rule()

            # Tag normalization
            normalize_tag_name = define_rule()
            deduplicate_tags = define_rule()

        class LOG(Rule):
            """Log domain normalization rules."""

            # Message normalization
            sanitize_message = define_rule()

            # Context normalization
            flatten_context = define_rule()
            optimize_context_size = define_rule()

            # Enrichment
            enrich_with_caller = define_rule()

            # Exception handling
            format_exception_info = define_rule()

            # ID generation
            generate_record_id = define_rule()

            # Level normalization
            normalize_level_names = define_rule()

            # Metric normalization
            normalize_metric_names = define_rule()
