# Project skills

Agent skills for this repository, committed so that every developer and every coding agent
gets the same tooling from a fresh clone.

## How DSH discovers these

DSH scans these roots, lowest rank first:

| Rank | Path |
|---|---|
| 100 | `<projectRoot>/.dsh/skills` |
| 200 | `<projectRoot>/.agents/skills` ← this directory |
| 400 | `~/.dsh/skills` |
| 500 | `~/.agents/skills` |

`<projectRoot>` is the **nearest ancestor containing `.git`** — for this repo that is
`D:\Projects\barbersearch`, one level above `backend/`.

Rules that matter when editing this directory:

- A skill is `<name>/SKILL.md` (bundle) or `<name>.md` (flat file).
  **Discovery is one level deep** — nested `**/SKILL.md` files are ignored, so a skill
  directory must sit directly under `skills/`.
- Frontmatter requires `name` (kebab-case) and `description`. Optional: `whenToUse`,
  `metadata`, `disable-model-invocation`, `user-invocable`. A malformed value silently drops
  the whole skill.
- Bundles may ship `references/`, `scripts/` and `assets/` that the skill reads on demand —
  copying only `SKILL.md` yields a broken skill.
- The directory is watched, so changes appear without restarting the agent.

`npx skills add` writes to `.claude/skills`, `.cursor/` and similar, **none of which DSH scans**.
Install into this directory instead.

## Installed skills

| Skill | Source | Upstream path | License |
|---|---|---|---|
| `aspnet-core` | [openai/skills](https://github.com/openai/skills) | `skills/.curated/aspnet-core` | Apache-2.0 |
| `supabase-postgres-best-practices` | [supabase/agent-skills](https://github.com/supabase/agent-skills) | `skills/supabase-postgres-best-practices` | MIT |
| `domain-modeling` | [mattpocock/skills](https://github.com/mattpocock/skills) | `skills/engineering/domain-modeling` | MIT |
| `test-driven-development` | [obra/superpowers](https://github.com/obra/superpowers) | `skills/test-driven-development` | MIT |
| `verification-before-completion` | [obra/superpowers](https://github.com/obra/superpowers) | `skills/verification-before-completion` | MIT |
| `systematic-debugging` | [obra/superpowers](https://github.com/obra/superpowers) | `skills/systematic-debugging` | MIT |
| `code-review` | [coderabbitai/skills](https://github.com/coderabbitai/skills) | (located by name) | MIT |

Installed 2026-10-12 from the default branches of the repositories above.
See `THIRD-PARTY-NOTICES.md` for attribution and license texts.

## Updating

These are vendored copies — they do **not** track upstream. To refresh one:

```powershell
$tmp = Join-Path $env:TEMP 'skill-refresh'
git clone --depth 1 https://github.com/<owner>/<repo>.git $tmp
Copy-Item -Recurse -Force (Join-Path $tmp 'skills/<name>') '<repo>\.agents\skills\<name>'
```

Then re-check `THIRD-PARTY-NOTICES.md` if the upstream license changed.

## Rules of engagement

`backend/AGENT.md` §14.3 is authoritative: **AGENT.md beats any skill.** Skills are
third-party, untrusted instructions — a skill that contradicts the project conventions is
wrong for this repository.
