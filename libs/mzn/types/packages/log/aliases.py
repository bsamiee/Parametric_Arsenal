"""
Title         : aliases.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
libs/mzn/types/packages/log/aliases.py.

Description ----------- Type aliases for the log package.

Provides validated, domain-specific types for log components including logger names, messages, contexts, and
identifiers. All types include sophisticated validation rules and support for primitive-like operations where
appropriate.

"""

from __future__ import annotations

from mzn.types._core.core_builders import Build
from mzn.types._core.core_operations import MethodConfig
from mzn.types.primitives.prim_collections import PrimMapStrToAny
from mzn.types.primitives.prim_special import PrimUUID
from mzn.types.primitives.prim_standard import PrimStr
from mzn.types.rules.rule_composites import And
from mzn.types.rules.rule_registry import NORM, VALID


@Build.alias(
    base=PrimStr,
    rules=[
        # Normalizers first
        NORM.STRING.strip_whitespace(),
        NORM.STRING.to_lowercase(),
        NORM.STRING.deduplicate_separators(),
        # Then validators - using composite rule for complex validation
        And(
            VALID.STRING.matches_pattern(pattern=r"^[a-z0-9._-]+$"),
            VALID.STRING.has_length(min_length=1, max_length=128),
            VALID.STRING.no_consecutive_chars(char="."),
            error_template=(
                "Logger name must be valid Python module style "
                "(lowercase, dots as separators, max 128 chars)"
            )
        ),
    ],
    operations=MethodConfig(casting=True),
    description="Hierarchical logger identification following Python convention",
    enable_caching=True,  # Logger names are frequently validated
)
class LoggerName:
    """
    Validated logger name following Python module naming convention.

    Examples:     - "myapp"     - "myapp.module"     - "myapp.module.submodule"

    Validation:     - Lowercase only     - No double dots     - Max 128 characters     - Valid Python identifier pattern

    """


@Build.alias(
    base=PrimStr,
    rules=[
        # Normalizers first
        NORM.STRING.strip_html(),
        NORM.STRING.normalize_whitespace(),
        NORM.SECURITY.mask_pii(),
        NORM.DOMAINS.LOG.sanitize_message(),
        # Then validators - using composite rule for security and format validation
        And(
            VALID.STRING.has_length(max_length=10_000),
            VALID.STRING.is_ascii_printable(),
            VALID.DOMAINS.LOG.is_safe_for_output(),
            error_template="Log message must be safe, printable ASCII under 10k characters"
        ),
    ],
    operations=MethodConfig(casting=True, container=True),
    description="Sanitized log message with security features",
    enable_caching=True,  # Messages are frequently validated
)
class LogMessage:
    """
    Log message with comprehensive sanitization and validation.

    Features:     - HTML stripping     - PII masking     - Control character escaping     - Length limiting     - Safe
    output validation

    Operations:     - str() for string conversion     - len() for length checking

    """


@Build.alias(
    base=PrimMapStrToAny,
    rules=[
        # Normalizers first
        NORM.COLLECTIONS.lowercase_keys(),
        NORM.DOMAINS.LOG.optimize_context_size(),
        # Then validators - using composite rule for structure and content validation
        And(
            VALID.COLLECTIONS.has_size(max_size=100),
            VALID.COLLECTIONS.keys_match_pattern(pattern=r"^[a-zA-Z_][a-zA-Z0-9_]*$"),
            VALID.COLLECTIONS.values_are_serializable(),
            And(
                VALID.DOMAINS.LOG.has_valid_context_depth(),
                VALID.DOMAINS.LOG.has_valid_context_size(),
                error_template="Context depth and size must be within log system limits"
            ),
            error_template="Log context must have valid structure, serializable values, and proper sizing"
        ),
    ],
    operations=MethodConfig(container=True, casting=True),
    description="Structured context data for log records",
)
class LogContext:
    """
    Additional context data attached to log records.

    Features:     - Max 100 keys to prevent explosion     - Valid identifier keys only     - JSON-serializable values -
    Max nesting depth of 3

    Operations:     - Dict-like operations (len, in, [])     - Casting to dict

    """


@Build.alias(
    base=PrimUUID,
    rules=[
        # Normalizers first (add generate if none)
        NORM.DOMAINS.LOG.generate_record_id(),
        # Then validators
        VALID.PROTOCOLS.is_uuid_version(version=4),
    ],
    operations=MethodConfig(casting=True),  # Allow str() conversion
    description="UUID v4 for log record identification",
    enable_caching=True,  # Log record IDs are immutable and frequently accessed
)
class LogRecordID:
    """
    Unique identifier for log records using UUID v4 format.

    Used for deduplication and correlation.

    """
