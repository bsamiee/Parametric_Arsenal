# =============================================================================
# Pre-commit Setup Script for Parametric Arsenal (Windows)
# =============================================================================
# This script installs and configures pre-commit hooks that enforce:
# - .NET analyzers from Directory.Build.props
# - .editorconfig settings
# - Python linting and type checking
#
# Usage: .\scripts\setup-precommit.ps1
# =============================================================================

$ErrorActionPreference = "Stop"

# Functions
function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Test-Command {
    param([string]$Command)
    
    try {
        $null = Get-Command $Command -ErrorAction Stop
        $path = (Get-Command $Command).Source
        Write-Info "$Command is installed: $path"
        return $true
    }
    catch {
        Write-Error "$Command is not installed. Please install it first."
        return $false
    }
}

# Main script
function Main {
    Write-Info "Setting up pre-commit hooks for Parametric Arsenal..."
    Write-Host ""

    # Check prerequisites
    Write-Info "Checking prerequisites..."
    $allInstalled = $true
    $allInstalled = $allInstalled -and (Test-Command "python")
    $allInstalled = $allInstalled -and (Test-Command "dotnet")
    $allInstalled = $allInstalled -and (Test-Command "git")
    
    if (-not $allInstalled) {
        Write-Error "Missing required prerequisites. Please install them first."
        exit 1
    }
    Write-Host ""

    # Install pre-commit if not installed
    try {
        $null = Get-Command pre-commit -ErrorAction Stop
        $version = (pre-commit --version)
        Write-Info "pre-commit is already installed: $version"
    }
    catch {
        Write-Warning "pre-commit not found. Installing..."
        try {
            python -m pip install --user pre-commit
            Write-Info "pre-commit installed successfully"
        }
        catch {
            Write-Error "Failed to install pre-commit. Please install it manually:"
            Write-Host "  pip install pre-commit"
            exit 1
        }
    }
    Write-Host ""

    # Install pre-commit hooks
    Write-Info "Installing pre-commit hooks..."
    pre-commit install
    Write-Host ""

    # Run pre-commit on all files
    Write-Info "Testing pre-commit hooks (this may take a while)..."
    try {
        pre-commit run --all-files
        Write-Info "All pre-commit checks passed!"
    }
    catch {
        Write-Warning "Some pre-commit checks failed. Please fix the issues and commit again."
        Write-Warning "You can run 'pre-commit run --all-files' to check all files manually."
    }
    Write-Host ""

    Write-Info "Setup complete! Pre-commit hooks are now active."
    Write-Info "Hooks will run automatically on git commit."
    Write-Host ""
    Write-Info "Useful commands:"
    Write-Host "  - Run on all files:     pre-commit run --all-files"
    Write-Host "  - Run on staged files:  pre-commit run"
    Write-Host "  - Update hooks:         pre-commit autoupdate"
    Write-Host "  - Skip hooks once:      git commit --no-verify"
    Write-Host ""
}

Main
