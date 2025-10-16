"""
Title         : project_config_tools.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/project_config_tools.py

Description
----------------------------------------------------------------------------
Project-level configuration management for document sets, scales, and metadata.
Stores configuration at document level using rs.SetDocumentUserText.
"""

from __future__ import annotations

import json
from typing import Any

import rhinoscriptsyntax as rs
import scriptcontext as sc

from .constants import Constants
from .exceptions import ProjectConfigError


# --- Project Configuration Tools ------------------------------------------
class ProjectConfigTools:
    """Tools for managing project-level configuration stored at document level."""

    @staticmethod
    def get_project_name() -> str | None:
        """Get project name from document user text.

        Returns:
            Project name string or None if not set.
        """
        return rs.GetDocumentUserText("project_config_name")

    @staticmethod
    def set_project_name(name: str) -> None:
        """Set project name in document user text.

        Args:
            name: Project name to store.

        Raises:
            ProjectConfigError: If name is empty or storage fails.
        """
        if not name or not name.strip():
            raise ProjectConfigError("Project name cannot be empty")  # noqa: TRY003

        result = rs.SetDocumentUserText("project_config_name", name)
        if not result:
            raise ProjectConfigError("Failed to set project name", context={"name": name})  # noqa: TRY003

    @staticmethod
    def get_document_set(discipline_code: str) -> dict[str, Any] | None:
        """Get document set configuration for a discipline code.

        Args:
            discipline_code: Discipline code (e.g., "A", "E", "S").

        Returns:
            Dictionary with keys: designation_level, default_scale, enabled.
            Returns None if document set not found or invalid JSON.
        """
        if not discipline_code:
            return None

        key = f"project_config_docset_{discipline_code}"
        json_str = rs.GetDocumentUserText(key)

        if not json_str:
            return None

        try:
            return json.loads(json_str)
        except (json.JSONDecodeError, ValueError):
            return None

    @staticmethod
    def set_document_set(discipline_code: str, config: dict[str, Any]) -> None:
        """Set document set configuration for a discipline code.

        Args:
            discipline_code: Discipline code (e.g., "A", "E", "S").
            config: Dictionary with keys: designation_level, default_scale, enabled.

        Raises:
            ProjectConfigError: If discipline code is empty or storage fails.
        """
        if not discipline_code or not discipline_code.strip():
            raise ProjectConfigError("Discipline code cannot be empty")  # noqa: TRY003

        key = f"project_config_docset_{discipline_code}"
        json_str = json.dumps(config)

        result = rs.SetDocumentUserText(key, json_str)
        if not result:
            raise ProjectConfigError(  # noqa: TRY003
                "Failed to set document set configuration",
                context={"discipline_code": discipline_code, "config": config},
            )

    @staticmethod
    def get_enabled_document_sets() -> list[tuple[str, dict[str, Any]]]:
        """Get all enabled document sets from document user text.

        Iterates through all document strings, filters keys matching
        "project_config_docset_*" pattern, and returns enabled sets.

        Returns:
            List of tuples: (discipline_code, config_dict).
            Only includes document sets where enabled=True.
        """
        if not sc.doc or not sc.doc.Strings:
            return []

        enabled_sets = []

        # Iterate through all document strings
        for i in range(sc.doc.Strings.Count):
            key = sc.doc.Strings.GetKey(i)

            # Filter for document set keys
            if key and key.startswith("project_config_docset_"):
                # Extract discipline code from key
                discipline_code = key.replace("project_config_docset_", "")

                # Get and parse configuration
                config = ProjectConfigTools.get_document_set(discipline_code)

                # Only include if enabled
                if config and config.get("enabled", False):
                    enabled_sets.append((discipline_code, config))

        return enabled_sets

    @staticmethod
    def validate_project_config() -> bool:
        """Check if project configuration exists.

        Validates that project name is set, which indicates the project
        has been configured.

        Returns:
            True if project name exists, False otherwise.
        """
        project_name = ProjectConfigTools.get_project_name()
        return project_name is not None and bool(project_name.strip())

    @staticmethod
    def get_template_config(template_name: str) -> dict[str, Any]:
        """Get template configuration from Constants.PROJECT_TEMPLATES.

        Args:
            template_name: Name of template (e.g., "Full AEC", "Arch Only").

        Returns:
            Template configuration dictionary or empty dict if not found.
        """
        return Constants.PROJECT_TEMPLATES.get(template_name, {})
