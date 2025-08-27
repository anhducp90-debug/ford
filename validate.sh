#!/bin/bash

#=============================================================================
# Validation Script for WireGuard VPN Management System
# Tests all major functionality without requiring root privileges
#=============================================================================

set -uo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m'

print_success() {
    echo -e "${GREEN}✓ $*${NC}"
}

print_error() {
    echo -e "${RED}✗ $*${NC}"
}

print_warn() {
    echo -e "${YELLOW}⚠ $*${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ $*${NC}"
}

test_file_exists() {
    local file="$1"
    local description="$2"
    
    if [[ -f "$file" ]]; then
        print_success "$description exists: $file"
        return 0
    else
        print_error "$description missing: $file"
        return 1
    fi
}

test_file_executable() {
    local file="$1"
    local description="$2"
    
    if [[ -x "$file" ]]; then
        print_success "$description is executable: $file"
        return 0
    else
        print_error "$description is not executable: $file"
        return 1
    fi
}

test_script_help() {
    local script="$1"
    local description="$2"
    
    if "$script" help &>/dev/null; then
        print_success "$description help command works"
        return 0
    elif "$script" --help &>/dev/null; then
        print_success "$description help command works"
        return 0
    elif "$script" -h &>/dev/null; then
        print_success "$description help command works"
        return 0
    else
        print_error "$description help command failed"
        return 1
    fi
}

test_directory_structure() {
    print_info "Testing directory structure..."
    
    local templates_dir="$SCRIPT_DIR/templates"
    if [[ -d "$templates_dir" ]]; then
        print_success "Templates directory exists"
    else
        print_error "Templates directory missing"
        return 1
    fi
    
    return 0
}

test_template_files() {
    print_info "Testing template files..."
    
    local client_template="$SCRIPT_DIR/templates/wg-client.conf"
    local server_template="$SCRIPT_DIR/templates/wg-server.conf"
    
    test_file_exists "$client_template" "Client template"
    test_file_exists "$server_template" "Server template"
    
    # Check template content
    if [[ -f "$client_template" ]]; then
        if grep -q "CLIENT_PRIVATE_KEY" "$client_template" && grep -q "SERVER_PUBLIC_KEY" "$client_template"; then
            print_success "Client template has proper placeholders"
        else
            print_error "Client template missing required placeholders"
        fi
    fi
    
    return 0
}

test_script_syntax() {
    print_info "Testing script syntax..."
    
    local scripts=("vpn_manager.sh" "udp2raw_manager.sh" "install.sh")
    
    for script in "${scripts[@]}"; do
        local script_path="$SCRIPT_DIR/$script"
        if [[ -f "$script_path" ]]; then
            if bash -n "$script_path"; then
                print_success "$script syntax is valid"
            else
                print_error "$script has syntax errors"
            fi
        fi
    done
    
    return 0
}

test_non_root_functions() {
    print_info "Testing non-root functions..."
    
    # Test help commands
    test_script_help "$SCRIPT_DIR/vpn_manager.sh" "VPN Manager"
    test_script_help "$SCRIPT_DIR/udp2raw_manager.sh" "UDP2RAW Manager"
    
    # Test list-clients (should work without root)
    if "$SCRIPT_DIR/vpn_manager.sh" list-clients &>/dev/null; then
        print_success "list-clients command works"
    else
        print_warn "list-clients command failed (expected if no setup done)"
    fi
    
    return 0
}

test_documentation() {
    print_info "Testing documentation..."
    
    local docs=("README.md" "CLIENT_SETUP.md")
    
    for doc in "${docs[@]}"; do
        test_file_exists "$SCRIPT_DIR/$doc" "$doc documentation"
        
        # Check if documentation has reasonable content
        if [[ -f "$SCRIPT_DIR/$doc" ]]; then
            local word_count=$(wc -w < "$SCRIPT_DIR/$doc")
            if [[ $word_count -gt 100 ]]; then
                print_success "$doc has substantial content ($word_count words)"
            else
                print_warn "$doc has minimal content ($word_count words)"
            fi
        fi
    done
    
    return 0
}

test_gitignore() {
    print_info "Testing .gitignore configuration..."
    
    local gitignore="$SCRIPT_DIR/.gitignore"
    if [[ -f "$gitignore" ]]; then
        local vpn_entries=("clients/" "keys/" "logs/" "*.key" "*.conf")
        local missing_entries=()
        
        for entry in "${vpn_entries[@]}"; do
            if ! grep -q "$entry" "$gitignore"; then
                missing_entries+=("$entry")
            fi
        done
        
        if [[ ${#missing_entries[@]} -eq 0 ]]; then
            print_success "All VPN-related entries in .gitignore"
        else
            print_warn "Missing .gitignore entries: ${missing_entries[*]}"
        fi
    else
        print_error ".gitignore file not found"
    fi
    
    return 0
}

run_validation() {
    print_info "==================================================="
    print_info "WireGuard VPN Management System Validation"
    print_info "==================================================="
    echo
    
    local tests=(
        "test_file_exists '$SCRIPT_DIR/vpn_manager.sh' 'Main VPN manager script'"
        "test_file_exists '$SCRIPT_DIR/udp2raw_manager.sh' 'UDP2RAW manager script'"
        "test_file_exists '$SCRIPT_DIR/install.sh' 'Installation script'"
        "test_file_executable '$SCRIPT_DIR/vpn_manager.sh' 'VPN manager script'"
        "test_file_executable '$SCRIPT_DIR/udp2raw_manager.sh' 'UDP2RAW manager script'"
        "test_file_executable '$SCRIPT_DIR/install.sh' 'Installation script'"
        "test_directory_structure"
        "test_template_files"
        "test_script_syntax"
        "test_non_root_functions"
        "test_documentation"
        "test_gitignore"
    )
    
    local passed=0
    local total=${#tests[@]}
    
    for test in "${tests[@]}"; do
        echo
        if eval "$test" 2>/dev/null; then
            ((passed++))
        fi
    done
    
    echo
    print_info "==================================================="
    print_info "Validation Results: $passed/$total tests passed"
    print_info "==================================================="
    
    if [[ $passed -eq $total ]]; then
        print_success "All validation tests passed!"
        echo
        print_info "System is ready for deployment. Next steps:"
        echo "  1. Run: sudo ./install.sh"
        echo "  2. Add clients: sudo ./vpn_manager.sh add-client device-name"
        echo "  3. Check status: ./vpn_manager.sh server-status"
        return 0
    else
        print_error "Some validation tests failed. Please check the issues above."
        return 1
    fi
}

main() {
    cd "$SCRIPT_DIR"
    run_validation
}

main "$@"