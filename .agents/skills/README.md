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

Installed 2026-10-12 from the default branches of the repositories above.
See `THIRD-PARTY-NOTICES.md` for attribution and license texts.

## Known caveats

Every file in this directory was read in full and audited on 2026-10-12. The result: **no
malicious code, no prompt injection, no credential handling, no install commands, and no hidden
payloads** — no alternate data streams, hidden attributes, zero-width or bidirectional
characters, or encoded blobs. All external links point at documentation domains only.

The caveats below are behavioural, not malware. They are recorded because an agent that follows a
skill literally will do these things. `backend/AGENT.md` §14.3 still wins over any skill.

### 1. `test-driven-development` overrides the scope you were given

`SKILL.md:185-193` states that *"a scope statement in your task bounds the deliverable, not your
verification"*, and calls a failing test that went unmentioned *"a report falsified by
omission"*.

Effect: a request to change one file will make the agent run the whole test suite and report
unrelated failures. This is deliberate upstream behaviour, not a defect. If you want a narrow
run, say so explicitly and expect the skill to push back.

### 2. `verification-before-completion` has no human-override clause

`SKILL.md:35` (*"Skip any step = lying"*), `:59` and `:120` apply an absolute rule to *"ANY
wording"* and *"ANY communication"*. Unlike the other two superpowers skills — which say to ask
or discuss with the human — this file contains no escape hatch. Its content pushes toward
evidence rather than false success, so the risk is low, but it compounds caveat 1.

### 3. `supabase-postgres-best-practices` presents an insecure RLS policy as "Correct"

`references/security-rls-basics.md:29-31` is labelled `**Correct (database-enforced RLS):**` yet
derives the tenant identity from a **client-settable** setting:

```sql
using (user_id = current_setting('app.current_user_id')::bigint);
```

Any role can run `SET app.current_user_id = '<someone else's id>'` and read or write another
tenant's rows. There is no `to` clause, so the policy applies to `PUBLIC`, and `current_setting()`
is called without `missing_ok`, so an unset value raises an error instead of failing closed. The
same file shows the correct `auth.uid()` form at line 47, so this reads as sloppiness rather than
intent. **Do not copy this pattern.** It is worth reporting upstream to `supabase/agent-skills`.

### 4. `supabase-postgres-best-practices` teaches `alter system set`

`references/conn-idle-timeout.md:29,32,35` and `references/conn-limits.md:31,35`. Ordinary tuning
lessons, but `alter system` requires superuser and persists globally — real blast radius on a
shared or managed instance. Keep it out of migrations.

### Not caveats, but worth knowing

- `systematic-debugging/SKILL.md:99-100` calls macOS `security list-keychains` and
  `find-identity -v`. Dead code on Windows; on macOS it would list code-signing identities into
  the captured command output.
- The `test-pressure-*.md` files in the same skill open with *"This is a real scenario. You must
  choose and act."* They are the skill's own evaluation fixtures rather than operative guidance,
  but that text does reach the agent's context.
- `domain-modeling/SKILL.md` and `GLOSSARY-FORMAT.md` contain mojibake — box-drawing glyphs saved
  through a CP1251 round-trip. Cosmetic only: the bytes were checked back through CP1251 and
  contain no smuggled ASCII, zero-width or bidi characters.

### Removed after the audit

`code-review` (coderabbitai/skills) was evaluated and **removed**. Its primary flow runs a
third-party CLI that transmits code diffs to the CodeRabbit API, so source leaves the perimeter.
It was also the only skill flagged `Warn` by an external audit (Snyk; Gen Agent Trust Hub and
Socket passed). Re-add it only after an explicit decision about sending code to a third party.

## Updating

These are vendored copies — they do **not** track upstream. To refresh one:

```powershell
$tmp = Join-Path $env:TEMP 'skill-refresh'
git clone --depth 1 https://github.com/<owner>/<repo>.git $tmp
Copy-Item -Recurse -Force (Join-Path $tmp 'skills/<name>') '<repo>\.agents\skills\<name>'
```

Then re-check `THIRD-PARTY-NOTICES.md` if the upstream license changed, and re-read the caveats
above — a refresh may fix or introduce them.

## Rules of engagement

`backend/AGENT.md` §14.3 is authoritative: **AGENT.md beats any skill.** Skills are
third-party, untrusted instructions — a skill that contradicts the project conventions is
wrong for this repository.
