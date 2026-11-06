#!/usr/bin/env python3
"""Fail when Civiti.Mcp/docs/tool-inventory.md and Civiti.Mcp/Tools/*.cs disagree.

Compares each tool's name, scope and endpoint. The scope comes from the auth helper the handler
calls, not from its description text, so a stale description can't hide a real change.

Runs in GitHub Actions rather than as an xUnit test on purpose: the root Dockerfile runs
Civiti.Tests during the Civiti-Server image build, so a stale doc row would fail that deploy.

Usage: python3 scripts/check-mcp-tool-inventory.py
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOLS_DIR = ROOT / "Civiti.Mcp" / "Tools"
INVENTORY = ROOT / "Civiti.Mcp" / "docs" / "tool-inventory.md"

SCOPE_BY_AUTH_HELPER = {
    "RequireCitizenReadAsync": "civiti.read",
    "ResolveCitizenAsync": "civiti.read",
    "RequireCitizenWriteAsync": "civiti.write",
    "ResolveCitizenWriteAsync": "civiti.write",
}

TOOL_ATTRIBUTE = re.compile(r'\[McpServerTool\(Name = "([a-z_]+)"\)\]')
# The leading dot keeps TryResolveCitizenAsync out: it is optional enrichment, not a gate.
AUTH_HELPER = re.compile(r"\.(" + "|".join(SCOPE_BY_AUTH_HELPER) + r")\(")
INVENTORY_ROW = re.compile(
    r"^\| `([a-z_]+)` \| (Public|Authenticated) \| (none|`civiti\.[a-z]+`) \| (?:Read|Write) \|",
    re.MULTILINE,
)


def tools_from_code() -> dict[str, tuple[str, str]]:
    """Return {tool name: (endpoint, scope)} parsed from the tool classes."""
    tools: dict[str, tuple[str, str]] = {}
    for path in sorted(TOOLS_DIR.glob("*.cs")):
        source = path.read_text(encoding="utf-8")
        # Each chunk after a split runs from one tool's attribute to the next one's.
        parts = TOOL_ATTRIBUTE.split(source)
        for name, body in zip(parts[1::2], parts[2::2]):
            helpers = {SCOPE_BY_AUTH_HELPER[h] for h in AUTH_HELPER.findall(body)}
            if len(helpers) > 1:
                sys.exit(f"{path.name}: {name} calls helpers for several scopes: {sorted(helpers)}")
            scope = helpers.pop() if helpers else "none"
            tools[name] = ("Public" if scope == "none" else "Authenticated", scope)
    return tools


def tools_from_inventory() -> dict[str, tuple[str, str]]:
    """Return {tool name: (endpoint, scope)} parsed from the inventory tables."""
    rows = INVENTORY_ROW.findall(INVENTORY.read_text(encoding="utf-8"))
    return {name: (endpoint, scope.strip("`")) for name, endpoint, scope in rows}


def main() -> int:
    code = tools_from_code()
    doc = tools_from_inventory()

    problems = [f"missing from tool-inventory.md: {name}" for name in sorted(code.keys() - doc.keys())]
    problems += [f"not in Civiti.Mcp/Tools: {name}" for name in sorted(doc.keys() - code.keys())]
    problems += [
        f"{name}: code has {code[name]}, tool-inventory.md has {doc[name]}"
        for name in sorted(code.keys() & doc.keys())
        if code[name] != doc[name]
    ]

    if problems:
        print("tool-inventory.md has drifted from Civiti.Mcp/Tools:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    print(f"tool-inventory.md matches Civiti.Mcp/Tools: {len(code)} tools.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
