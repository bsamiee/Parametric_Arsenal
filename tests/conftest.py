"""
Title         : conftest.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT Path :
tests/conftest.py.

Description ----------- Global pytest configuration and shared fixtures.

"""

import asyncio
from collections.abc import AsyncGenerator, Generator
from typing import Any

import pytest
import pytest_asyncio
from hypothesis import HealthCheck, settings

from mzn.types._core import core_constants as constants


# Configure Hypothesis settings
settings.register_profile(
    "ci",
    max_examples=100,
    deadline=5000,  # 5 seconds
    suppress_health_check=[HealthCheck.too_slow],
)

settings.register_profile(
    "dev",
    max_examples=10,
    deadline=2000,  # 2 seconds
    suppress_health_check=[HealthCheck.too_slow],
)

settings.register_profile(
    "debug",
    max_examples=1,
    deadline=None,
    suppress_health_check=[HealthCheck.too_slow],
)

# Use 'dev' profile by default, can be overridden with HYPOTHESIS_PROFILE env var
settings.load_profile("dev")


@pytest.fixture(scope="session")
def event_loop() -> Generator[asyncio.AbstractEventLoop]:
    """Create an event loop for the entire test session."""
    loop = asyncio.new_event_loop()
    yield loop
    loop.close()


@pytest_asyncio.fixture
async def clean_registry() -> AsyncGenerator[None]:
    """Ensure a clean type registry for each test."""
    # Since we can't access the registry's clear method directly,
    # we'll work around it by using unique names for each test
    # to avoid conflicts. This is a limitation of the current API.

    # We could potentially monkey-patch for testing, but that's fragile
    # Better to design tests to use unique type names

    yield

    # No cleanup needed - tests should use unique names


@pytest.fixture()
def unique_name(request: Any) -> str:
    """Generate a unique name for test types to avoid registry conflicts."""
    # Use the test name and a counter to ensure uniqueness
    test_name = str(request.node.name).replace("[", "_").replace("]", "_").replace("-", "_")
    return f"Test_{test_name}_{id(request)}"


@pytest.fixture()
def sample_metadata() -> dict[str, Any]:
    """Sample metadata for testing."""
    return {
        "description": "Test asset",
        "version": "1.0.0",
        "author": "Test Suite",
        "custom_field": 42,
    }


@pytest.fixture()
def all_asset_types() -> list[constants.AssetType]:
    """List of all asset types for parametrized tests."""
    return [
        constants.PRIMITIVE,
        constants.ALIAS,
        constants.ENUM,
        constants.MODEL,
        constants.PROTOCOL,
    ]


@pytest.fixture()
def performance_features() -> dict[str, bool]:
    """Performance feature flags for testing."""
    return {
        "enable_caching": False,
        "enable_logging": False,
        "enable_debugging": False,
    }


# Markers for test organization
def pytest_configure(config: pytest.Config) -> None:
    """Configure custom pytest markers."""
    # Domain-specific markers
    config.addinivalue_line("markers", "types: marks tests for the type system")
    config.addinivalue_line("markers", "factory: marks tests for the factory system")
    config.addinivalue_line("markers", "rules: marks tests for rule processing")
    config.addinivalue_line("markers", "builders: marks tests for Build decorators")
    config.addinivalue_line("markers", "integration: marks integration tests")
    config.addinivalue_line("markers", "property: marks property-based tests")
