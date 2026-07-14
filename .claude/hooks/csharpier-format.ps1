# Claude Code PostToolUse hook: format C# files with the pinned CSharpier tool
# right after Claude edits them. Reads the hook payload (JSON) from stdin.
$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
$raw = $raw.TrimStart([char]0xFEFF, ' ', "`t", "`r", "`n")

try { $data = $raw | ConvertFrom-Json } catch { exit 0 }

$path = $data.tool_input.file_path
if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }
if ($path -notlike '*.cs') { exit 0 }
if (-not (Test-Path -LiteralPath $path)) { exit 0 }

# Run from the project root so the dotnet tool manifest and .csharpierignore resolve.
$root = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

Push-Location $root
try { dotnet csharpier format "$path" *> $null } finally { Pop-Location }
exit 0
