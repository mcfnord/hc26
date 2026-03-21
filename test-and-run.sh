#!/bin/bash
# test-and-run.sh — Run the full test suite, then start the server if everything passes.
# Usage:
#   ./test-and-run.sh          # Test, then serve
#   ./test-and-run.sh --test   # Test only (no server)
#   ./test-and-run.sh --serve  # Skip tests, just serve (development shortcut)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

MODE="${1:-full}"

run_tests() {
    echo -e "${CYAN}============================================${NC}"
    echo -e "${CYAN}  HexChess Test Suite${NC}"
    echo -e "${CYAN}============================================${NC}"

    echo ""
    echo -e "${YELLOW}[1/3] Restoring packages...${NC}"
    dotnet restore --verbosity quiet

    echo -e "${YELLOW}[2/3] Building solution...${NC}"
    dotnet build --no-restore --verbosity quiet

    echo -e "${YELLOW}[3/3] Running tests...${NC}"
    echo ""

    # Run with verbose output so LLM can read individual test results
    if dotnet test HexC.Tests/ \
        --no-build \
        --verbosity normal \
        --logger "console;verbosity=detailed" \
        2>&1; then
        echo ""
        echo -e "${GREEN}============================================${NC}"
        echo -e "${GREEN}  ALL TESTS PASSED${NC}"
        echo -e "${GREEN}============================================${NC}"
        return 0
    else
        echo ""
        echo -e "${RED}============================================${NC}"
        echo -e "${RED}  TESTS FAILED — SERVER NOT STARTED${NC}"
        echo -e "${RED}============================================${NC}"
        return 1
    fi
}

start_server() {
    echo ""
    echo -e "${CYAN}Starting HexChess Server...${NC}"
    cd HexC.Server
    dotnet run
}

case "$MODE" in
    --test)
        run_tests
        ;;
    --serve)
        start_server
        ;;
    *)
        if run_tests; then
            start_server
        else
            echo -e "${RED}Fix the failing tests before serving.${NC}"
            exit 1
        fi
        ;;
esac
