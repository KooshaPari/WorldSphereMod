# Git Hooks

These are repo-tracked git hooks for WorldSphereMod3D development.

## Installation

After cloning, run:

```powershell
git config core.hooksPath .githooks
```

Or use the CLI:

```powershell
./Tools/wsm3d.ps1 hooks install
```

## Hooks

### pre-commit

Runs sanity checks before each commit:

- **Build validation**: Runs `dotnet build` if any `.cs` files are staged.
- **Test validation**: Runs `dotnet test` if any test files are staged.
- **PowerShell validation**: Parses `Tools/wsm3d.ps1` if staged.
- **YAML validation**: Checks `.github/workflows/` and `.coderabbit.yaml` if staged.

**Performance**: <15 seconds for no-op, <60 seconds for full diff.

## Bypassing Hooks

For WIP commits, bypass the pre-commit hook with:

```bash
git commit --no-verify
```

## Testing

Test the hook locally:

```powershell
# Configure hooks path (one-time)
git config core.hooksPath .githooks

# Test by adding a file and running the hook directly
$null = New-Item -ItemType File -Force tmp_test.txt -Value "x"
git add tmp_test.txt
& .githooks/pre-commit.ps1

# Clean up
git reset HEAD tmp_test.txt
Remove-Item tmp_test.txt
```
