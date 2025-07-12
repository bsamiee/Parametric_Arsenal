"""
Title         : feat_documentation.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       :
MIT Path          : libs/mzn/types/_features/feat_documentation.py.

Description ----------- Documentation feature mixin for all asset types.

Provides methods for generating rich docstrings for assets and their components (rules, fields, enum members) by reading
from the unified metadata store.

"""

from __future__ import annotations

import textwrap
from typing import TYPE_CHECKING, Annotated, Any, ClassVar, cast


if TYPE_CHECKING:
    from collections.abc import Mapping

    from mzn.types._contracts.prot_assets import JSONLike
    from mzn.types._core.core_tags import Tag

# --- Mixin Definition ---------------------------------------------------------


class DocumentationMixin:
    """Composable mixin providing rich docstring generation capabilities."""

    # These attributes are provided by TypeAsset base class when the mixin is applied
    if TYPE_CHECKING:
        mzn_metadata: ClassVar[dict[str, JSONLike]]
        mzn_tags: ClassVar[set[Tag]]

        # Methods provided by MetadataMixin
        @classmethod
        async def get_metadata(cls) -> Mapping[str, JSONLike]:
            """Get metadata mapping."""
            ...

        # Methods provided by TaggingProvider
        @classmethod
        async def get_tags(cls) -> set[Tag]:
            """Get tags set."""
            ...

    @classmethod
    async def generate_docstring(
        cls,
        asset_type: Annotated[str, "The type of asset (e.g., 'alias', 'model')."],
        shell_cls: Annotated[type[Any], "The original user-defined shell class."],
    ) -> Annotated[str, "A comprehensive, generated docstring."]:
        """
        Generate a comprehensive docstring for the asset.

        Args:     asset_type: The type of asset (e.g., "alias", "model", "enum").     shell_cls: The original user-
        defined shell class.

        Returns:     A comprehensive docstring for the asset.

        """
        doc = shell_cls.__doc__ or f"Domain Asset: {cls.__name__}"
        doc = textwrap.dedent(doc).strip()

        # Add tags section first for high-level context
        doc += await cls._generate_tags_docstring()

        if asset_type in {"alias", "model"}:
            doc += await cls._generate_rules_docstring()
        if asset_type == "model":
            doc += await cls._generate_fields_docstring()
        if asset_type == "enum":
            doc += await cls._generate_enum_members_docstring()

        return doc

    @classmethod
    async def _generate_rules_docstring(cls) -> str:
        """Generate the docstring section for validation and normalization rules."""
        metadata = await cls.get_metadata()
        doc_parts: list[str] = []

        if normalizers := cast("list[dict[str, Any]]", metadata.get("normalizers", [])):
            norm_section = ["\n\nNormalization Steps:"]
            for rule in normalizers:
                rule_doc = textwrap.dedent(rule.get("doc", "")).strip().split("\n")[0]
                config = rule.get("config", {})
                config_str = f" ({config})" if config else ""
                norm_section.append(f"    - {rule['name']}:{config_str} {rule_doc}")
            doc_parts.append("\n".join(norm_section))

        if validators := cast("list[dict[str, Any]]", metadata.get("validators", [])):
            valid_section = ["\n\nValidation Rules:"]
            for rule in validators:
                rule_doc = textwrap.dedent(rule.get("doc", "")).strip().split("\n")[0]
                config = rule.get("config", {})
                config_str = f" ({config})" if config else ""
                valid_section.append(f"    - {rule['name']}:{config_str} {rule_doc}")
            doc_parts.append("\n".join(valid_section))

        return "".join(doc_parts)

    @classmethod
    async def _generate_fields_docstring(cls) -> str:
        """Generate the docstring section for model fields."""
        metadata = await cls.get_metadata()
        if not (fields := cast("dict[str, dict[str, Any]]", metadata.get("model_fields", {}))):
            return ""

        fields_section = ["\n\nFields:"]
        for name, field in sorted(fields.items()):
            type_name = getattr(field["annotation"], "__name__", str(field["annotation"]))
            desc = field.get("description", "No description provided.")
            pii_marker = " (PII)" if field.get("pii") else ""
            fields_section.append(f"    {name} ({type_name}){pii_marker}: {desc}")

        return "\n".join(fields_section)

    @classmethod
    async def _generate_tags_docstring(cls) -> str:
        """Generate the docstring section for asset tags."""
        # For enums and other special cases, we need to check if get_tags exists
        # When mixins are applied to enums, cls might still refer to DocumentationMixin
        tags: set[Tag]
        if hasattr(cls, "get_tags") and callable(getattr(cls, "get_tags", None)):
            tags = await cls.get_tags()
        else:
            # Try to get tags from mzn_tags attribute directly
            default_tags: set[Tag] = set()
            tags = cast("set[Tag]", getattr(cls, "mzn_tags", default_tags))

        if not tags:
            return ""

        # Sort tags by path for deterministic output
        sorted_tags = sorted(tags, key=lambda t: t.path)

        tags_section = ["\n\nTags:"]
        tags_section.extend([f"  - {tag.path}" for tag in sorted_tags])

        return "\n".join(tags_section)

    @classmethod
    async def _generate_enum_members_docstring(cls) -> str:
        """Generate the docstring section for enum members."""
        # Work directly with enum members instead of expecting metadata structure
        if not hasattr(cls, "__members__"):
            return ""

        members_section = ["\n\nMembers:"]
        # Cast to Any to handle dynamic enum class attributes
        enum_cls = cast("Any", cls)

        for name, member in sorted(enum_cls.__members__.items()):
            # Try to get EnumMember metadata if available
            if hasattr(member, "value") and hasattr(member.value, "description"):
                enum_member = member.value
                desc = getattr(enum_member, "description", None) or "No description."

                # Format tags if present
                tags_str = ""
                if hasattr(enum_member, "tags") and getattr(enum_member, "tags", None):
                    tags = enum_member.tags
                    tag_paths = sorted(
                        getattr(t, "path", str(t))
                        for t in tags
                    )
                    tags_str = f"(Tags: {', '.join(tag_paths)})"

                # Add deprecation info
                deprecated_str = "(DEPRECATED)" if getattr(enum_member, "deprecated", False) else ""

                # Combine all parts
                line_parts = [f"  - {name}: {desc}"]
                if tags_str:
                    line_parts.append(tags_str)
                if deprecated_str:
                    line_parts.append(deprecated_str)

                members_section.append(" ".join(line_parts).strip())
            else:
                # Fallback for simple enum values
                member_value = getattr(member, "value", member)
                members_section.append(f"  - {name}: Value: {member_value}")

        return "\n".join(members_section)


# --- Public re-exports --------------------------------------------------------


__all__ = ["DocumentationMixin"]
