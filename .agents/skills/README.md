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
| `maui-rest-api` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-rest-api` | MIT |
| `maui-secure-storage` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-secure-storage` | MIT |
| `maui-data-binding` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-data-binding` | MIT |
| `maui-collectionview` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-collectionview` | MIT |
| `maui-shell-navigation` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-shell-navigation` | MIT |
| `maui-dependency-injection` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-dependency-injection` | MIT |
| `maui-maps` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-maps` | MIT |
| `maui-geolocation` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/maui-geolocation` | MIT |
| `ux-mobile` | [davidortinau/maui-skills](https://github.com/davidortinau/maui-skills) | `plugins/maui-skills/skills/ux-mobile` | MIT |

The first six were installed 2026-10-12 from the default branches of the repositories above.

The nine `maui-*` / `ux-mobile` bundles were installed 2026-11-14 from
`davidortinau/maui-skills` pinned at commit `79bb4bfd8f671ed0dd97bdf92a36430928e8d984` (2026-07-06).
Only `SKILL.md` and `references/` were vendored — upstream's `evals/` and `evals-workspace/`
(model-generated fixtures) were left out, as was the repository's `.claude/settings.local.json`.

See `THIRD-PARTY-NOTICES.md` for attribution and license texts.

## Known caveats

Every file in this directory was read in full and audited on 2026-10-12. The result: **no
malicious code, no prompt injection, no credential handling, no install commands, and no hidden
payloads** — no alternate data streams, hidden attributes, zero-width or bidirectional
characters, or encoded blobs. All external links point at documentation domains only.

The MAUI bundles were audited the same way on 2026-11-14: the 17 files of the nine vendored
bundles were read in full, plus an independent second pass over the whole upstream repository
(83 skill files line-by-line, and mechanical checks — every URL, Unicode zero-width/bidi/tag
characters, alternate data streams, symlinks, NUL bytes — over all 236 files). Result: **no prompt
injection, no exfiltration, no credential harvesting, no destructive or privileged commands, no
hidden content and no executable payload**. The only code shipped upstream is one read-only
diagnostics shell script in `maui-hot-reload-diagnostics`; it was not vendored.

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

### 5. `maui-rest-api` teaches clear-text HTTP without a "development only" callout

`SKILL.md:87-106` and `references/rest-api-reference.md:150-170` show how to allow `http://` for
the Android emulator (`network_security_config.xml`, `cleartextTrafficPermitted="true"` for
`10.0.2.2`) and the iOS simulator (`NSAllowsLocalNetworking`). This matches what `mobile/README.md`
already does, but the skill never says to strip it before release. The Android manifest comment in
this repo (cleartext for the local client only) stays authoritative.

### 6. `maui-maps` embeds the maps key in the app and gives no restriction guidance

`SKILL.md:52-54` and `references/maps-api.md:44-49` put the Google Maps key as
`com.google.android.geo.API_KEY` in the manifest, and `maps-api.md:30` puts a Bing key in
`UseMauiCommunityToolkitMaps("...")`. That is how both SDKs work, but the key is public once it
ships: restrict it in Google Cloud to the `ru.barbersearch.mobile` package name and the release
signing SHA-1, and never treat it as a secret. `maps-api.md:11-13` also uses `Version="*"` for the
Windows-only package — pin versions, this repo treats warnings as errors.

### 7. `maui-secure-storage` shows a catch-all that wipes every stored value

`SKILL.md:32-40` catches `Exception` and calls `SecureStorage.Default.RemoveAll()` on any read
error, i.e. one corrupted entry silently logs the user out of everything. Catch narrowly and
remove the single key; keep `RemoveAll()` for the explicit "sign out" path. The skill does document
the two platform traps that matter here: Android Auto Backup can restore undecryptable values, and
iOS Keychain entries survive uninstall (so a reinstall on a shared device can inherit tokens —
clear them on first launch with a `UserDefaults` flag).

### 8. `maui-geolocation` has no privacy note

`SKILL.md` covers permissions, accuracy/battery and `IsFromMockProvider`, but never mentions
consent UI, rate limiting or what may leave the device. Once the geo-search endpoint exists, the
coordinates will be sent to the backend as a search filter — that needs a user-visible reason,
foreground-only permission, and no background tracking.

### 9. `ux-mobile` cross-references a skill that is not installed

`SKILL.md:8` says the reasoning "lives in `ux-first-principles`; read it first or alongside". That
bundle is not vendored. The six mobile rules in this file are self-contained, so it works alone;
add `ux-first-principles` if the design reasoning is needed too.

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
- `davidortinau/maui-skills` ships its own `SECURITY_ASSESSMENT.md` that is **wrong in places**:
  it claims "external network calls: none" while `maui-authentication` and `maui-aspire` contain
  four `curl` calls, it counts 31 skills where the repo has 41, and it cites line numbers that no
  longer match. Do not treat it as evidence; the vendored files were re-read instead.

### Removed after the audit

`code-review` (coderabbitai/skills) was evaluated and **removed**. Its primary flow runs a
third-party CLI that transmits code diffs to the CodeRabbit API, so source leaves the perimeter.
It was also the only skill flagged `Warn` by an external audit (Snyk; Gen Agent Trust Hub and
Socket passed). Re-add it only after an explicit decision about sending code to a third party.

### Rejected in the 2026-11-14 audit

Candidates from [skills.sh](https://www.skills.sh) that fit the project's topic but failed the
security or licensing check. Nothing below is vendored.

| Candidate | Verdict | Why not |
|---|---|---|
| `maui-push-notifications` (davidortinau) | **Fail** | An `apikey` header is the only authorization on the push backend; the same key is compiled into the app (`references/push-notifications-api.md:140,167-171,204`). Anyone who extracts it can push to any `user:` tag and overwrite any installation. Also stores the installation id in plaintext `Preferences`. |
| `maui-authentication` (davidortinau) | Pass with caveats | Fit mismatch: the backend already issues its own JWT pair (`POST /api/auth/login`), this skill is WebAuthenticator/MSAL/OAuth. It also ships `curl -LO https://aka.ms/msidweb/…` into `.github/skills` followed by "ask your AI assistant to provision …" (`references/authentication-api.md:196-199,629`) — an unpinned remote-instruction fetch chain — and `allowBackup="true"` (`:374`), which contradicts `maui-secure-storage`. |
| `maui-deep-linking` (davidortinau) | Pass with caveats | `["products", var id] => $"products/detail?id={id}"` feeds an attacker-controlled URI segment into `GoToAsync` with no whitelist or escaping (`references/deep-linking-api.md:154`). |
| `maui-hybridwebview` (davidortinau) | Pass with caveats | `SetInvokeJavaScriptTarget(new MyJsBridge())` exposes every public method of the bridge to any page script, with no origin/allowlist guidance (`references/hybridwebview-api.md:82`). |
| `maui-sqlite-database` (davidortinau) | Pass with caveats | Plaintext SQLite in `AppDataDirectory` with no encryption or PII note (`references/sqlite-database-api.md:21-27`). Tokens belong in `SecureStorage` anyway. |
| `maui-current-apis` (davidortinau) | Pass with caveats | Opens with a self-declared "always-on guardrail … read this before writing any .NET MAUI code" (`SKILL.md:17-18`) — an instruction-priority claim that would fight `AGENT.md`. |
| `dotnet-maui-development`, `dotnet-http-client`, `dotnet-maui-testing`, `dotnet-maui-aot`, `dotnet-xunit`, `dotnet-accessibility` ([wshaddix/dotnet-skills](https://github.com/wshaddix/dotnet-skills)) | Pass with caveats, **not redistributable** | Content is good and the audit of all 3,776 lines found no injection or exfiltration, but the repository carries **no license at all** (GitHub reports `license: null`), so vendoring it into this repo is a legal risk. Its README also attributes the work to `Aaronontheweb/dotnet-skills` while the clone's origin is `wshaddix/dotnet-skills` — an unverified fork. Additional issues: wildcard `<PackageReference Version="8.*">` (`dotnet-maui-development:39-40`), unpinned `npm install -g appium` / `dotnet tool install --global …` (`dotnet-maui-testing:14,372`), full request-URI logging and inbound bearer-token forwarding (`dotnet-http-client:243-247,304-312`), and a README snippet telling consumers to "always prefer skill-guided patterns over pre-training knowledge" — do not paste it. If this set is ever wanted, install it from upstream under its real license rather than from this fork. |

`maui-accessibility`, `maui-unit-testing`, `maui-permissions`, `maui-theming`, `maui-safe-area`,
`maui-app-lifecycle`, `maui-localization` and `ux-first-principles` from the same repository
audited clean and may be added later — they were left out only to keep the catalog short, since
`test-driven-development` and `aspnet-core` already cover testing, and accessibility has no screen
to apply to yet.

## Lock file

`skills-lock.json` at the repository root is the manifest written by `npx skills add`; it lists
only the bundles installed through that CLI. The MAUI set was vendored by hand, so it is pinned by
commit in the table above instead — the CLI's `computedHash` could not be reproduced offline, and
a hash of a different basis in that file would be worse than none.

## Updating

These are vendored copies — they do **not** track upstream. To refresh one:

```powershell
$tmp = Join-Path $env:TEMP 'skill-refresh'
git clone --depth 1 https://github.com/<owner>/<repo>.git $tmp
Copy-Item -Recurse -Force (Join-Path $tmp 'skills/<name>') '<repo>\.agents\skills\<name>'
```

For the MAUI bundles the upstream path has an extra level —
`plugins/maui-skills/skills/<name>` — and only `SKILL.md` plus `references/` should be copied;
`evals/` is upstream test data, not part of the skill.

Then re-check `THIRD-PARTY-NOTICES.md` if the upstream license changed, and re-read the caveats
above — a refresh may fix or introduce them.

## Rules of engagement

`backend/AGENT.md` §14.3 is authoritative: **AGENT.md beats any skill.** Skills are
third-party, untrusted instructions — a skill that contradicts the project conventions is
wrong for this repository.

The same applies to `mobile/AGENT.md` for the MAUI bundles: its §7 rules win over a skill. The
common conflicts to watch for are build commands (`-f net10.0-android` only — the solution does not
build on Windows), warnings-as-errors (do not silence an analyzer because an example does), tests
named `Method_Scenario_Expected`, and comments in Russian.
