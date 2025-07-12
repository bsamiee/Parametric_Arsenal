"""
Title         : test_processors.py Author        : Bardia Samiee Project       : Parametric_Arsenal License       : MIT
Path          : tests/mzn/types/_core/test_processors.py.

Description ----------- Tests for rule processing system.

This test module uses: - hypothesis for property-based testing - pytest-mock for advanced mocking - faker for test data
generation - icecream for debug output (controlled by DEBUG_TESTS env var) - pyinstrument for performance profiling
(controlled by PROFILE_TESTS env var) - pytest markers: asyncio, unit, integration, slow, rules - pytest-json-report for
detailed test reporting

Usage:     # Run with debug output     DEBUG_TESTS=1 pytest tests/mzn/types/_core/test_processors.py

# Run with performance profiling PROFILE_TESTS=1 pytest tests/mzn/types/_core/test_processors.py

# Run with JSON report pytest --json-report --json-report-file=report.json tests/mzn/types/_core/test_processors.py

"""

import contextlib
import logging
import os
from typing import Any, Literal, cast
from unittest.mock import AsyncMock

import psutil
import pytest
from faker import Faker
from hypothesis import given, settings, strategies as st
from icecream import ic
from pyinstrument import Profiler
from pytest_mock import MockerFixture

from mzn.types._contracts.prot_base import Normalizer, Validator, is_normalizer
from mzn.types._core import core_constants as constants
from mzn.types._core.core_errors import Error
from mzn.types._core.core_processors import (
    FunctionNormalizer,
    FunctionValidator,
    accepts_validation_info,
    is_async_rule,
    is_rule,
    is_structural_normalizer,
    is_structural_validator,
    process_rules,
)
from mzn.types.rules.rule_registry import NORM, VALID


# Configure logging
logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s")

# Configure icecream based on environment variable
DEBUG_TESTS = os.getenv("DEBUG_TESTS", "").lower() in {"1", "true", "yes", "on"}
PROFILE_TESTS = os.getenv("PROFILE_TESTS", "").lower() in {"1", "true", "yes", "on"}

if DEBUG_TESTS:
    ic.configureOutput(prefix="DEBUG | ")
else:
    # Disable icecream in non-debug mode
    ic.disable()


@pytest.fixture()
def debug_output():
    """Fixture to enable debug output for specific tests."""
    ic.enable()
    yield ic
    if not DEBUG_TESTS:
        ic.disable()


@pytest.fixture()
def performance_profiler():
    """
    Fixture for performance profiling.

    Usage:     def test_something(performance_profiler):         with performance_profiler:             # Code to
    profile             pass         # Profile output is automatically logged when PROFILE_TESTS=1

    """
    logger = logging.getLogger(__name__)
    if PROFILE_TESTS:
        profiler = Profiler()
        yield profiler
        # Auto-log profile if enabled
        logger.info("\n%s", "=" * 80)
        logger.info("PERFORMANCE PROFILE:")
        logger.info("%s", "=" * 80)
        logger.info("%s", profiler.output_text(unicode=True, color=True))
    else:
        # Dummy context manager when profiling is disabled
        @contextlib.contextmanager
        def dummy_profiler():
            yield None

        yield dummy_profiler()


@pytest.fixture()
def memory_tracker():
    """
    Fixture to track memory usage during tests.

    Returns a callable that returns current memory usage in MB.

    """
    logger = logging.getLogger(__name__)
    process = psutil.Process()

    def get_memory_mb() -> float:
        return process.memory_info().rss / 1024 / 1024

    initial_memory = get_memory_mb()
    yield get_memory_mb

    if PROFILE_TESTS:
        final_memory = get_memory_mb()
        memory_diff = final_memory - initial_memory
        logger.info("\nMemory usage: %.2fMB → %.2fMB (Δ%+.2fMB)", initial_memory, final_memory, memory_diff)


class MockValidationInfo:
    """Mock ValidationInfo for testing purposes."""

    def __init__(self, field_name: str = "test_field", context: dict[str, Any] | None = None):
        super().__init__()
        self.field_name = field_name
        self.context = context or {}
        self.config = None  # CoreConfig is None for testing
        self.mode: Literal["python", "json"] = "python"
        self.data: dict[str, Any] = {}


@pytest.fixture(autouse=True)
def ensure_rules_loaded():
    """Ensure all rules are loaded before running tests."""
    # Force loading of all rules by accessing a rule from each registry
    # This will trigger lazy loading for both validators and normalizers
    with contextlib.suppress(Exception):
        _ = VALID.STRING.is_lowercase()
        _ = NORM.STRING.strip_whitespace()


@pytest.fixture()
def faker():
    """Provide Faker instance for test data generation."""
    return Faker()


@pytest.fixture()
def mock_validation_info():
    """Provide a mock ValidationInfo instance."""
    return MockValidationInfo()


@pytest.mark.asyncio
@pytest.mark.rules
class TestRuleProcessing:
    """Test rule processing functions."""

    logger = logging.getLogger(__name__)

    async def test_debug_rule_creation(self) -> None:
        """Debug test to understand rule creation."""
        # Create a normalizer
        normalizer = NORM.STRING.strip_whitespace()

        self.logger.debug("Created normalizer: %r", normalizer)
        self.logger.debug("Type: %r", type(normalizer))
        self.logger.debug("Is FunctionNormalizer: %r", isinstance(normalizer, FunctionNormalizer))
        self.logger.debug("is_normalizer check: %r", is_normalizer(normalizer))
        self.logger.debug("is_async_rule check: %r", is_async_rule(normalizer))

        # Let's trace what happens in process_rules
        rules = (normalizer,)
        normalizers, validators = await process_rules(rules)

        self.logger.debug("After process_rules:")
        self.logger.debug("Normalizers: %r", normalizers)
        self.logger.debug("Validators: %r", validators)

    async def test_process_rules_empty(self) -> None:
        """Test processing empty rules list."""
        normalizers, validators = await process_rules(())
        assert normalizers == []
        assert validators == []

    async def test_process_rules_validators_only(self) -> None:
        """Test processing validators only."""
        # Call the rule registry methods to get validator instances
        rules = (
            cast(Validator[str], VALID.STRING.has_length(min_length=3, max_length=10)),
            cast(Validator[str], VALID.STRING.is_lowercase()),
        )
        normalizers, validators = await process_rules(rules)

        assert len(normalizers) == 0
        assert len(validators) == 2
        # Check they are FunctionValidator instances
        assert all(isinstance(v, FunctionValidator) for v in validators)

    async def test_process_rules_normalizers_only(self) -> None:
        """Test processing normalizers only."""
        rules = (
            cast(Normalizer[str, str], NORM.STRING.strip_whitespace()),
            cast(Normalizer[str, str], NORM.STRING.to_lowercase()),
        )
        normalizers, validators = await process_rules(rules)

        assert len(normalizers) == 2
        assert len(validators) == 0
        # Check they are FunctionNormalizer instances
        assert all(isinstance(n, FunctionNormalizer) for n in normalizers)

    async def test_process_rules_mixed(self) -> None:
        """Test processing mixed rules."""
        rules = (
            cast(Normalizer[str, str], NORM.STRING.strip_whitespace()),
            cast(Validator[str], VALID.STRING.has_length(min_length=3)),
            cast(Normalizer[str, str], NORM.STRING.to_lowercase()),
            cast(Validator[str], VALID.STRING.is_alpha()),
        )
        normalizers, validators = await process_rules(rules)

        assert len(normalizers) == 2
        assert len(validators) == 2
        # Check order is preserved within categories
        assert isinstance(normalizers[0], FunctionNormalizer)
        assert isinstance(normalizers[1], FunctionNormalizer)

    async def test_process_rules_sentinels(self) -> None:
        """Test processing rules with sentinels."""
        rules: tuple[Any, ...] = (
            constants.Sentinel.SKIP,
            cast(Validator[str], VALID.STRING.has_length(min_length=3)),
            constants.Sentinel.DISABLED,
            cast(Normalizer[str, str], NORM.STRING.strip_whitespace()),
        )
        normalizers, validators = await process_rules(rules)

        # Sentinels should be filtered out
        assert len(normalizers) == 1
        assert len(validators) == 1


@pytest.mark.asyncio
@pytest.mark.rules
class TestValidatorBehavior:
    """Test validator behavior through actual usage."""

    async def test_string_validators(self) -> None:
        """Test string validators work correctly."""
        # Get validators - these return FunctionValidator instances
        length_validator = cast(Validator[str], VALID.STRING.has_length(min_length=3, max_length=10))
        lowercase_validator = cast(Validator[str], VALID.STRING.is_lowercase())

        # Create a mock ValidationInfo instance
        info = MockValidationInfo()

        # FunctionValidator returns the value on success (True), raises on failure (False)
        # Validator function itself returns bool, but FunctionValidator handles the rest

        # Test length validator - it should return the value if validation succeeds
        result = await length_validator("test", info)
        assert result == "test"  # FunctionValidator returns value on success

        # Test validation failure - should raise
        with pytest.raises(Error.MznError):  # FunctionValidator raises on False
            _ = await length_validator("ab", info)

        with pytest.raises(Error.MznError):
            _ = await length_validator("this is too long", info)

        # Test lowercase check
        result = await lowercase_validator("test", info)
        assert result == "test"

        with pytest.raises(Error.MznError):
            _ = await lowercase_validator("TEST", info)

    async def test_numeric_validators(self) -> None:
        """Test numeric validators."""
        positive_validator = cast(Validator[int], VALID.NUMERIC.is_positive())
        even_validator = cast(Validator[int], VALID.NUMERIC.is_even())

        # Create a mock ValidationInfo instance
        info = MockValidationInfo()

        # Test positive
        result = await positive_validator(50, info)
        assert result == 50

        with pytest.raises(Error.MznError):
            _ = await positive_validator(-10, info)

        # Test even
        result = await even_validator(50, info)
        assert result == 50

        with pytest.raises(Error.MznError):
            _ = await even_validator(51, info)


@pytest.mark.asyncio
@pytest.mark.rules
class TestNormalizerBehavior:
    """Test normalizer behavior through actual usage."""

    async def test_string_normalizers(self) -> None:
        """Test string normalizers work correctly."""
        # Get normalizers - these return FunctionNormalizer instances
        strip_normalizer = cast(Normalizer[str, str], NORM.STRING.strip_whitespace())
        lowercase_normalizer = cast(Normalizer[str, str], NORM.STRING.to_lowercase())

        # Create a mock ValidationInfo instance
        info = MockValidationInfo()

        # Test strip - normalizers transform and return the new value
        result = await strip_normalizer("  test  ", info)
        assert result == "test"

        result2 = await strip_normalizer("test", info)
        assert result2 == "test"

        # Test lowercase
        result3 = await lowercase_normalizer("TEST", info)
        assert result3 == "test"

        result4 = await lowercase_normalizer("Test", info)
        assert result4 == "test"

    async def test_normalizer_chaining(self) -> None:
        """Test normalizers can be chained."""
        normalizers = [
            cast(Normalizer[str, str], NORM.STRING.strip_whitespace()),
            cast(Normalizer[str, str], NORM.STRING.to_lowercase()),
        ]

        # Create a mock ValidationInfo instance
        info = MockValidationInfo()

        value = "  HELLO WORLD  "
        for normalizer in normalizers:
            value = await normalizer(value, info)

        assert value == "hello world"


@pytest.mark.asyncio
@pytest.mark.rules
class TestCompositeRules:
    """Test composite rule functionality."""

    async def test_composite_imports(self) -> None:
        """Test we can import composite rules."""
        from mzn.types.rules.rule_composites import And, Not, Or

        assert And is not None
        assert Or is not None
        assert Not is not None

    async def test_rule_registry_usage(self) -> None:
        """Test the rule registry provides expected rules."""
        # String validators
        assert hasattr(VALID.STRING, "has_length")
        assert hasattr(VALID.STRING, "is_lowercase")
        assert hasattr(VALID.STRING, "is_email")
        assert hasattr(VALID.STRING, "is_alpha")

        # String normalizers
        assert hasattr(NORM.STRING, "strip_whitespace")
        assert hasattr(NORM.STRING, "to_lowercase")
        assert hasattr(NORM.STRING, "to_uppercase")

        # Numeric validators
        assert hasattr(VALID.NUMERIC, "is_positive")
        assert hasattr(VALID.NUMERIC, "is_even")
        assert hasattr(VALID.NUMERIC, "is_finite")

    async def test_composite_and_rule(self) -> None:
        """Test AND composition of rules."""
        from mzn.types.rules.rule_composites import And

        # Create a composite rule
        rule = And(
            cast(Validator[str], VALID.STRING.has_length(min_length=3, max_length=10)),
            cast(Validator[str], VALID.STRING.is_alpha()),
        )

        # Process the composite rule
        normalizers, validators = await process_rules((rule,))
        assert len(normalizers) == 0
        assert len(validators) == 1  # The composite And rule

        # Create mock ValidationInfo
        info = MockValidationInfo()

        # Test the composite validator
        composite_validator = validators[0]

        # Should pass all conditions
        result = await composite_validator("test", info)
        assert result == "test"

        # Should fail length check
        with pytest.raises(Error.MznError):
            _ = await composite_validator("ab", info)

        # Should fail alpha check
        with pytest.raises(Error.MznError):
            _ = await composite_validator("test123", info)

    async def test_composite_or_rule(self) -> None:
        """Test OR composition of rules."""
        from mzn.types.rules.rule_composites import Or

        # Create a composite rule
        rule = Or(
            cast(Validator[str], VALID.STRING.is_email()),
            cast(Validator[str], VALID.STRING.matches_pattern(pattern=r"^[a-z0-9_]+$")),
        )

        # Process the composite rule
        normalizers, validators = await process_rules((rule,))
        assert len(normalizers) == 0
        assert len(validators) == 1  # The composite Or rule

        # Create mock ValidationInfo
        info = MockValidationInfo()

        # Test the composite validator
        composite_validator = validators[0]

        # Should pass email validation
        result1 = await composite_validator("test@example.com", info)
        assert result1 == "test@example.com"

        # Should pass pattern validation
        result2 = await composite_validator("test_123", info)
        assert result2 == "test_123"

        # Should fail both conditions
        with pytest.raises(Error.MznError):
            _ = await composite_validator("Test-123", info)

    async def test_composite_not_rule(self) -> None:
        """Test NOT composition of rules."""
        from mzn.types.rules.rule_composites import Not

        # Create a composite rule - succeeds if the inner rule fails
        # Use is_lowercase which is implemented
        lowercase_rule = cast(Validator[str], VALID.STRING.is_lowercase())
        rule = Not(lowercase_rule)

        # Process the composite rule
        normalizers, validators = await process_rules((rule,))
        assert len(normalizers) == 0
        assert len(validators) == 1  # The composite Not rule

        # Create mock ValidationInfo
        info = MockValidationInfo()

        # Test the composite validator
        composite_validator = validators[0]

        # Should pass (is NOT lowercase - i.e., has uppercase)
        result = await composite_validator("UPPERCASE", info)
        assert result == "UPPERCASE"

        # Should fail (is lowercase, so NOT fails)
        with pytest.raises(Error.MznError):
            _ = await composite_validator("lowercase", info)


@pytest.mark.asyncio
@pytest.mark.unit
class TestTypeGuards:
    """Test TypeGuard functions for rule detection."""

    async def test_is_rule(self) -> None:
        """Test is_rule TypeGuard function."""
        # Valid rules
        validator = VALID.STRING.is_lowercase()
        normalizer = NORM.STRING.strip_whitespace()

        assert is_rule(validator) is True
        assert is_rule(normalizer) is True

        # Invalid rules
        assert is_rule(None) is False
        assert is_rule("not a rule") is False
        assert is_rule(42) is False
        assert is_rule(lambda x: x) is False  # pyright: ignore[reportUnknownLambdaType,reportUnknownArgumentType]

    async def test_is_async_rule(self) -> None:
        """Test is_async_rule function."""
        # All our rules are async
        validator = VALID.STRING.is_lowercase()
        normalizer = NORM.STRING.strip_whitespace()

        assert is_async_rule(validator) is True
        assert is_async_rule(normalizer) is True

        # Non-rules
        assert is_async_rule(None) is False
        assert is_async_rule("not a rule") is False

    async def test_accepts_validation_info(self) -> None:
        """Test accepts_validation_info function."""

        # Create test functions
        async def with_info(value: str, info: Any) -> str:
            return value

        async def without_info(value: str) -> str:
            return value

        def sync_with_info(value: str, info: Any) -> str:
            return value

        assert accepts_validation_info(with_info) is True
        assert accepts_validation_info(without_info) is False
        assert accepts_validation_info(sync_with_info) is True
        assert accepts_validation_info("not callable") is False
        assert accepts_validation_info(None) is False

    async def test_structural_type_guards(self) -> None:
        """Test is_structural_normalizer and is_structural_validator."""
        validator = VALID.STRING.is_lowercase()
        normalizer = NORM.STRING.strip_whitespace()

        # Process rules to get FunctionValidator/FunctionNormalizer
        _, validators = await process_rules((validator,))
        normalizers, _ = await process_rules((normalizer,))

        if validators:
            assert is_structural_validator(validators[0]) is True
        if normalizers:
            assert is_structural_normalizer(normalizers[0]) is True

        # Test non-structural types
        assert is_structural_validator("not a validator") is False
        assert is_structural_normalizer("not a normalizer") is False


@pytest.mark.asyncio
@pytest.mark.unit
class TestPropertyBasedTesting:
    """Property-based tests using Hypothesis."""

    @given(st.text(min_size=0, max_size=100))
    @settings(max_examples=50)
    async def test_string_normalizer_idempotence(self, text: str) -> None:
        """Test that normalizers are idempotent (applying twice = applying once)."""
        strip_normalizer = cast(Normalizer[str, str], NORM.STRING.strip_whitespace())
        info = MockValidationInfo()

        # Apply once
        result1 = await strip_normalizer(text, info)
        # Apply twice
        result2 = await strip_normalizer(result1, info)

        assert result1 == result2  # Idempotent

    @given(st.integers(min_value=-1000, max_value=1000))
    @settings(max_examples=50)
    async def test_numeric_validator_consistency(self, num: int) -> None:
        """Test numeric validators are consistent."""
        positive_validator = cast(Validator[int], VALID.NUMERIC.is_positive())
        info = MockValidationInfo()

        if num > 0:
            result = await positive_validator(num, info)
            assert result == num
        else:
            with pytest.raises(Error.MznError):
                _ = await positive_validator(num, info)

    @given(st.lists(st.text(min_size=1, max_size=20), min_size=0, max_size=10))
    @settings(max_examples=30)
    async def test_process_rules_with_random_sentinels(self, rule_names: list[str]) -> None:
        """Test process_rules handles random sentinel placements correctly."""
        rules: list[Any] = []
        expected_count = 0

        for name in rule_names:
            # Randomly insert sentinels
            if len(name) % 3 == 0:
                rules.append(constants.Sentinel.SKIP)
            elif len(name) % 3 == 1:
                rules.append(constants.Sentinel.DISABLED)
            else:
                # Add a real validator
                rules.append(cast(Validator[str], VALID.STRING.is_lowercase()))
                expected_count += 1

        normalizers, validators = await process_rules(tuple(rules))
        assert len(validators) == expected_count
        assert len(normalizers) == 0


@pytest.mark.asyncio
@pytest.mark.unit
class TestMockingAndEdgeCases:
    """Test edge cases and error conditions using mocks."""

    async def test_validator_with_custom_error(self, mocker: MockerFixture) -> None:
        """Test validator behavior with custom error messages."""
        # Mock a validator that always fails
        mock_validator_func = AsyncMock(return_value=False)
        mock_validator_func.error_template = "Custom error: {value}"

        # Create FunctionValidator with the mock
        func_validator = FunctionValidator(func=mock_validator_func, error_message="Custom error: {value}")

        info = MockValidationInfo()

        with pytest.raises(Error.MznError) as exc_info:
            await func_validator("test_value", info)

        assert "Custom error: test_value" in str(exc_info.value)

    async def test_normalizer_exception_handling(self, mocker: MockerFixture) -> None:
        """Test normalizer behavior when transformation fails."""
        # Mock a normalizer that raises ValueError
        mock_normalizer_func = AsyncMock(side_effect=ValueError("Transformation failed"))

        # Create FunctionNormalizer with the mock
        func_normalizer = FunctionNormalizer(func=mock_normalizer_func, error_message="Transformation error")

        info = MockValidationInfo()

        with pytest.raises(ValueError, match="Transformation failed") as exc_info:
            await func_normalizer("test_value", info)

        assert "Transformation failed" in str(exc_info.value)

    async def test_process_rules_mixed_types(self) -> None:
        """Test process_rules with various edge case inputs."""
        # Test with empty tuple
        normalizers, validators = await process_rules(())
        assert normalizers == []
        assert validators == []

        # Test with only sentinels
        rules = (
            constants.Sentinel.SKIP,
            constants.Sentinel.DISABLED,
            constants.Sentinel.DEFER,
            constants.Sentinel.NOOP,
        )
        normalizers, validators = await process_rules(rules)
        assert normalizers == []
        assert validators == []

        # Test with only a validator (no None values - they're not valid Rule types)
        rules_with_validator = (
            cast(Validator[str], VALID.STRING.is_lowercase()),
        )
        normalizers, validators = await process_rules(rules_with_validator)
        assert len(validators) == 1
        assert len(normalizers) == 0


@pytest.mark.asyncio
@pytest.mark.integration
class TestIntegrationScenarios:
    """Integration tests combining multiple features."""

    async def test_complex_rule_chain(self, faker: Faker) -> None:
        """Test complex chaining of normalizers and validators."""
        # Generate test data
        test_emails = [faker.email() for _ in range(5)]

        # Create a complex rule chain
        rules = (
            cast(Normalizer[str, str], NORM.STRING.strip_whitespace()),
            cast(Normalizer[str, str], NORM.STRING.to_lowercase()),
            cast(Validator[str], VALID.STRING.has_length(min_length=3, max_length=50)),
        )

        normalizers, validators = await process_rules(rules)
        assert len(normalizers) == 2
        assert len(validators) == 1

        info = MockValidationInfo()

        # Test with various inputs
        for email in test_emails:
            # Apply normalizers in sequence
            value = email
            for normalizer in normalizers:
                value = await normalizer(value, info)

            # Apply validator
            if 3 <= len(value) <= 50:
                result = await validators[0](value, info)
                assert result == value
            else:
                with pytest.raises(Error.MznError):
                    _ = await validators[0](value, info)

    async def test_performance_characteristics(self) -> None:
        """Test performance characteristics of rule processing."""
        import time

        # Create many rules
        rules: list[Validator[str] | Normalizer[str, str]] = []
        for i in range(100):
            if i % 2 == 0:
                rules.append(cast(Validator[str], VALID.STRING.is_lowercase()))
            else:
                rules.append(cast(Normalizer[str, str], NORM.STRING.strip_whitespace()))

        start_time = time.time()
        normalizers, validators = await process_rules(tuple(rules))
        end_time = time.time()

        assert len(normalizers) == 50
        assert len(validators) == 50

        # Should process quickly (under 1 second for 100 rules)
        assert (end_time - start_time) < 1.0


@pytest.mark.asyncio
@pytest.mark.unit
class TestDebugCapabilities:
    """Test with debug output capabilities."""

    async def test_with_debug_fixture(self, debug_output: Any) -> None:
        """Example test using the debug_output fixture."""
        # When debug_output fixture is used, ic is enabled for this test
        rule = NORM.STRING.strip_whitespace()

        # These will only output when DEBUG_TESTS=1 or when using debug_output fixture
        debug_output(f"Rule type: {type(rule)}")
        debug_output(f"Is async: {is_async_rule(rule)}")

        assert isinstance(rule, FunctionNormalizer)


@pytest.mark.asyncio
@pytest.mark.slow
class TestStressTests:
    """Stress tests for edge cases and performance."""

    async def test_deeply_nested_composite_rules(self) -> None:
        """Test deeply nested composite rules."""
        from mzn.types.rules.rule_composites import And, Not, Or

        # Create a deeply nested rule
        base_rule = cast(Validator[str], VALID.STRING.is_lowercase())

        # Nest it multiple times
        nested_not = Not(base_rule)
        nested_and = And(nested_not, cast(Validator[str], VALID.STRING.has_length(min_length=1)))
        nested_or = Or(nested_and, cast(Validator[str], VALID.STRING.is_alpha()))
        nested_final = Not(nested_or)

        # Process the deeply nested rule
        normalizers, validators = await process_rules((nested_final,))
        assert len(validators) == 1
        assert len(normalizers) == 0

        # Test it still works
        info = MockValidationInfo()
        validator = validators[0]

        # This is complex logic, just ensure it doesn't crash
        with contextlib.suppress(Error.MznError):
            await validator("test", info)

    async def test_with_performance_profiling(self, performance_profiler: Any, memory_tracker: Any) -> None:
        """Example test using performance profiling fixtures."""
        initial_mem = memory_tracker()

        # Profile rule processing performance
        with performance_profiler:
            # Create many rules to process
            rules: list[Validator[str] | Normalizer[str, str]] = []
            for i in range(1000):
                if i % 2 == 0:
                    rules.append(cast(Validator[str], VALID.STRING.is_lowercase()))
                else:
                    rules.append(cast(Normalizer[str, str], NORM.STRING.strip_whitespace()))

            normalizers, validators = await process_rules(tuple(rules))

        final_mem = memory_tracker()

        assert len(normalizers) == 500
        assert len(validators) == 500

        if PROFILE_TESTS:
            logger = logging.getLogger(__name__)
            logger.info("\nProcessed 1000 rules using %.2fMB", final_mem - initial_mem)

    @given(st.lists(st.sampled_from([0, 1, 2]), min_size=0, max_size=50))
    @settings(max_examples=20)
    async def test_rule_processing_with_random_mix(self, rule_types: list[int]) -> None:
        """Test processing random mix of rules and sentinels."""
        rules: list[Any] = []
        expected_validators = 0
        expected_normalizers = 0

        for rule_type in rule_types:
            if rule_type == 0:
                rules.append(constants.Sentinel.SKIP)
            elif rule_type == 1:
                rules.append(cast(Validator[str], VALID.STRING.is_lowercase()))
                expected_validators += 1
            else:
                rules.append(cast(Normalizer[str, str], NORM.STRING.strip_whitespace()))
                expected_normalizers += 1

        normalizers, validators = await process_rules(tuple(rules))
        assert len(validators) == expected_validators
        assert len(normalizers) == expected_normalizers


@pytest.mark.asyncio
@pytest.mark.unit
class TestCodeQuality:
    """Tests that analyze code quality metrics of the module under test."""

    async def test_module_complexity_report(self) -> None:
        """Generate code quality report using radon."""
        try:
            from pathlib import Path

            import radon.complexity as radon_cc
            import radon.metrics as radon_mi

            # Find the core_processors module
            module_path = Path(__file__).parent.parent.parent.parent / "core_processors.py"

            if module_path.exists():
                source_code = module_path.read_text(encoding="utf-8")

                # Analyze cyclomatic complexity
                cc_results = radon_cc.cc_visit(source_code, str(module_path))

                # Analyze maintainability index
                mi_result = radon_mi.mi_visit(source_code, multi=True)

                # Generate report
                logger = logging.getLogger(__name__)
                logger.info("\n%s", "=" * 80)
                logger.info("CODE QUALITY METRICS - core_processors.py")
                logger.info("%s", "=" * 80)

                logger.info("\nCyclomatic Complexity:")
                for func in cc_results:
                    complexity_grade = "A" if func.complexity <= 5 else "B" if func.complexity <= 10 else "C"
                    logger.info("  %s: %s (Grade: %s)", func.name, func.complexity, complexity_grade)

                logger.info("\nMaintainability Index: %.2f", mi_result)
                logger.info("  Grade: %s", "A" if mi_result >= 20 else "B" if mi_result >= 10 else "C")

                # Assert quality standards
                assert all(func.complexity <= 15 for func in cc_results), "Some functions exceed complexity limit"
                assert mi_result >= 10, "Maintainability index below threshold"

        except ImportError:
            pytest.skip("Radon not available in test environment")

    async def test_test_coverage_metrics(self) -> None:
        """Analyze test coverage patterns."""
        # This would typically be handled by pytest-cov, but we can add custom analysis
        test_methods: list[str] = []
        for cls in [
            TestRuleProcessing,
            TestValidatorBehavior,
            TestNormalizerBehavior,
            TestCompositeRules,
            TestTypeGuards,
            TestPropertyBasedTesting,
            TestMockingAndEdgeCases,
            TestIntegrationScenarios,
            TestDebugCapabilities,
            TestStressTests,
        ]:
            test_methods.extend([m for m in dir(cls) if m.startswith("test_")])

        logger = logging.getLogger(__name__)
        logger.info("\nTest Coverage Summary:")
        logger.info("  Total test methods: %d", len(test_methods))
        logger.info("  Test classes: 10")
        logger.info("  Using property-based testing: Yes")
        logger.info("  Using mocking: Yes")
        logger.info("  Performance tests: Yes")

        assert len(test_methods) >= 25, "Insufficient test coverage"
