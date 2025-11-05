#!/usr/bin/env bash
# =============================================================================
# Pre-commit Setup Script for Parametric Arsenal
# =============================================================================
# This script installs and configures pre-commit hooks that enforce:
# - .NET analyzers from Directory.Build.props
# - .editorconfig settings
# - Python linting and type checking
#
# Usage: ./scripts/setup-precommit.sh
# =============================================================================

set -euo pipefail

# Colors for output
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly NC='\033[0m' # No Color

# Functions
log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

check_command() {
    if ! command -v "$1" &> /dev/null; then
        log_error "$1 is not installed. Please install it first."
        return 1
    fi
    log_info "$1 is installed: $(command -v "$1")"
}

# Main script
main() {
    log_info "Setting up pre-commit hooks for Parametric Arsenal..."
    echo ""

    # Check prerequisites
    log_info "Checking prerequisites..."
    check_command python3 || exit 1
    check_command dotnet || exit 1
    check_command git || exit 1
    echo ""

    # Install pre-commit if not installed
    if ! command -v pre-commit &> /dev/null; then
        log_warn "pre-commit not found. Installing..."
        if command -v pip3 &> /dev/null; then
            pip3 install --user pre-commit
        elif command -v pip &> /dev/null; then
            pip install --user pre-commit
        else
            log_error "pip/pip3 not found. Please install pre-commit manually:"
            echo "  pip install pre-commit"
            exit 1
        fi
    else
        log_info "pre-commit is already installed: $(pre-commit --version)"
    fi
    echo ""

    # Install pre-commit hooks
    log_info "Installing pre-commit hooks..."
    pre-commit install
    echo ""

    # Run pre-commit on all files (optional - can be commented out)
    log_info "Testing pre-commit hooks (this may take a while)..."
    if pre-commit run --all-files; then
        log_info "All pre-commit checks passed!"
    else
        log_warn "Some pre-commit checks failed. Please fix the issues and commit again."
        log_warn "You can run 'pre-commit run --all-files' to check all files manually."
    fi
    echo ""

    log_info "Setup complete! Pre-commit hooks are now active."
    log_info "Hooks will run automatically on git commit."
    log_info ""
    log_info "Useful commands:"
    echo "  - Run on all files:     pre-commit run --all-files"
    echo "  - Run on staged files:  pre-commit run"
    echo "  - Update hooks:         pre-commit autoupdate"
    echo "  - Skip hooks once:      git commit --no-verify"
    echo ""
}

main "$@"
