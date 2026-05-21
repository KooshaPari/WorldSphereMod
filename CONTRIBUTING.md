# Contributing to PhenoHandbook
PhenoHandbook is the living documentation for design patterns, anti-patterns, guidelines, and methodologies across the Phenotype ecosystem. Contributions extend the patterns registry and improve cross-references with `PhenoSpecs` and `HexaKit`.

## Prerequisites
- Node.js LTS or later
- `pnpm` (preferred) or `npm`
- Python 3.10+ with `mkdocs` (for site builds): `pip install mkdocs mkdocs-material`

## Getting Started

```bash
git clone https://github.com/KooshaPari/PhenoHandbook.git
cd PhenoHandbook
pnpm install
pnpm test
mkdocs serve # local preview at http://localhost:8000
```

## Adding a Pattern

1. **Pick the right directory**:
   - `patterns//` for design patterns
   - `anti-patterns//` for what NOT to do
   - `guidelines/` for coding standards
   - `methodologies/` for workflows
   - `checklists/` for verification lists
2. **Use the canonical format** (see `README.md` "Pattern Format" section): Summary, Problem, Solution, Example, When to Use, When NOT to Use, Related Patterns.
3. **Cross-link** to relevant `PhenoSpecs` ADRs and `FUNCTIONAL_REQUIREMENTS.md` entries.
4. **Update `mkdocs.yml`** navigation if you add a new top-level section.

## Development Workflow

1. **Branch from `main`**: `git checkout -b docs/` or `feat/`.
2. **Run validation**:
   ```bash
   pnpm test # vitest
   pnpm lint # if configured
   mkdocs build --strict # catches broken internal links
   ```
3. **Commit style**: Conventional commits (`docs:`, `feat:`, `fix:`).
4. **Open a PR**: Describe the pattern, the problem it solves, and any specs it traces to.

## Code Standards
- TypeScript code under `tests/` follows project `tsconfig` rules.
- All Markdown is UTF-8, lint-clean (`markdownlint`).
- Examples use real, runnable code from the Phenotype ecosystem when possible.
- No vendor-specific patterns without ecosystem justification.

## Reporting Issues
Open a GitHub issue describing the gap (missing pattern, outdated guidance, broken example) and link to the affected file.

## License
By contributing you agree your work is licensed under the project `LICENSE` (dual MIT / Apache-2.0).
