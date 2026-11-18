#!/usr/bin/env bash
# PostToolUse hook: background compile check after .cs/.xaml edits. Skips if dotnet or a solution is missing.
f=$(cat | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)
case "$f" in *.cs|*.xaml|*.csproj) ;; *) exit 0 ;; esac
command -v dotnet >/dev/null || exit 0
sln=$(ls "$CLAUDE_PROJECT_DIR"/src/*.sln 2>/dev/null | head -1)
[ -n "$sln" ] || exit 0
out=$(dotnet build "$sln" -warnaserror --nologo -v q 2>&1) || { echo "$out" | tail -30 >&2; exit 2; }
