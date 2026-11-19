## Context

The motivation is in proposal.md (Why). This is the current state that shapes the approach, as checked on 2026-11-19 on `09c4dd2`:

- **Every text file is LF.** `git ls-files --eol` reports 136 files as `i/lf w/lf` and three empty `.gitkeep` files as `i/none`. No file is stored or checked out with CRLF.
- **No commit has ever stored a CR.** `git grep -I -l -P '\r' <rev>` finds nothing in any commit reachable from any branch or tag.
- **Nothing decides line endings.** There is no `.gitattributes` and no `.editorconfig`. The effective `core.autocrlf` is `false`: Git for Windows' system configuration sets `true`, and the global configuration overrides it.
- **No binary files are tracked.** The types are `.md` (80), `.yaml`/`.yml` (36), `.cs` (6), `.sh` (2), `.json` (3), `.csproj` (2), `.slnx`, `.tpl`, `.txt`, `Dockerfile` and six dotfiles. There are no `.bat`, `.cmd` or `.ps1` files.
- **Formatting this change leaves alone:**
  - Six `.cs` files start with a UTF-8 BOM.
  - Fourteen files have no final newline, among them `Dockerfile`, nine templates under `helm/templates/`, `Program.cs` and `launchSettings.json`.
- **The workflows.** Build-Dotnet, Check-Docker and Build-Helm run on pushes and pull requests to `main` only for the paths they list. Release runs on `v*` tags. No workflow runs for a change to a root-level dotfile, `README.md`, `docs/` or `openspec/`.
- **Release's gates.** `Check-Tag` runs first. `Test` and `Check-Helm` both need it, and `Build-and-Push-Docker` has `needs: [Test, Check-Helm]` (`release.yaml:57`). Everything after the image push already waits for it.
- **The label script works around CR.** `.github/scripts/check-chart-labels.sh` pipes `helm show chart` and `helm template` through `tr -d '\r'` (lines 24 and 34). Both were added in `0943829` together with the script.
- **`docs/ci-cd-best-practices.md` points into `release.yaml` by line number,** and asks for those numbers to be updated whenever a workflow changes. All of them are correct on `09c4dd2`.
- **EuGo-mcp's `normalize-line-endings-to-lf`** was applied while this change was being planned, in commits `e4897bc`, `bf9c3d6`, `933a0ce` and `f52a5bd`.
  - **Its root files.** Its `.gitattributes` has the rules of D2 below. Its comments name its own `ci.yml` job and `continuous-integration` spec. Its `.editorconfig` is D2's byte for byte.
  - **Its check** differs from D5 below; see D5.

## Goals / Non-Goals

**Goals:**

- The rule travels with the repository: the same bytes in every checkout on Windows, Linux and CI, whatever each machine's Git configuration says.
- A file stored with CRLF fails CI and names the file, on any change, before anything is published.
- The other repositories can copy the files and the check, changing only comments that name repository-specific files.

**Non-Goals:**

- Renormalizing anything: there is nothing to renormalize.
- Any formatting rule other than line endings.
- A local pre-commit hook. It would be per-machine configuration, the weakness `core.autocrlf` has.
- Applying the D5 finding below to EuGo-mcp. That belongs to that repository's change.

## Decisions

### D1. `* text=auto eol=lf` for the whole repository

This is the same decision as EuGo-mcp's D1, for the same reasons. Git normalizes every text file to LF on commit and writes LF on checkout. Attributes take precedence over `core.autocrlf` and `core.eol`.

Alternatives considered:

- **`* text=auto` alone.** The repository stays LF, but Windows checkouts get CRLF, because `core.eol` defaults to `native`. Local `helm template`, `docker build` and the label script would then read different bytes than CI does. A working tree opened from both Windows and WSL would show Linux tools CRLF files.
- **`core.autocrlf` per machine.** It is per machine, and on this machine it is already set in two places that disagree. Nothing would hold on a new clone.
- **Nothing, since the repository is LF anyway.** EuGo-mcp ended up with 300 CRLF files on the same machine and the same settings.

### D2. The same rules as EuGo-mcp, with this repository's comments

`.gitattributes`:

```
# Every text file is stored with LF and checked out with LF, on Windows and on Linux alike,
# whatever core.autocrlf or core.eol says on the machine. Files are edited on both platforms,
# and CI reads them on Linux: a line-anchored check or a version read with sed sees exactly the
# bytes a local checkout has. The Check-Line-Endings workflow fails on a committed CRLF file.
# See openspec/specs/ci-test-gate ("Text files reach verification with LF line endings").
* text=auto eol=lf

# text=auto calls a file binary only if it finds a NUL byte in its first 8,000 bytes. These types
# are binary whatever their content, so detection never decides for them. Later lines override
# earlier ones, which is why they follow the rule above. One pattern per line: a line holding
# several patterns is not a list.
*.ico binary
*.png binary
*.jpg binary
*.jpeg binary
*.gif binary
*.webp binary
*.pdf binary
*.zip binary
*.woff binary
*.woff2 binary
```

`.editorconfig`, which is EuGo-mcp's D8 exactly:

```
root = true

[*]
end_of_line = lf
```

- **The binary list.** example-net tracks no binary file. The list is kept anyway, so that one file serves every repository.
- **The rules match EuGo-mcp's exactly; the comments don't.**
  - **Why the comments differ.** EuGo-mcp's comments name its `ci.yml` job and its `continuous-integration` spec, and copied here they would point at files that don't exist. The comments above follow EuGo-mcp's text and name this repository's workflow and spec instead.
  - **How the match is checked.** The rule lines must be identical: comparing both files with their `#` lines removed must show no difference.
  - **`.editorconfig`** has no comments, so it matches byte for byte.
- **What `.editorconfig` leaves out.** Without `charset`, editors keep the six BOMs. Without `insert_final_newline`, they don't touch the fourteen files that lack one.
- **Roslyn reads `.editorconfig`.** The C# compiler reads it as analyzer configuration. `end_of_line` changes no compilation, so the file doesn't affect the build, and the Dockerfile doesn't need to copy it. A `dotnet_diagnostic.*` entry would change that: the file would become a root-level build file that needs its own `COPY` and entries in Check-Docker's and Build-Dotnet's `paths`. `CLAUDE.md` says so.

### D3. Nothing is renormalized, and no clone is rewritten

Every file is already stored LF, so `git add --renormalize .` stages nothing. Running it and checking that the staged diff lists only the two new files is the proof.

- **`.git-blame-ignore-revs`:** none, because no commit changes line endings.
- **This working tree:** already LF, so it needs no rewrite.
- **Other clones.** Git rewrites a file on pull only when its stored bytes change, and none do here. So adding `.gitattributes` doesn't rewrite any clone's working tree. A clone checked out with `core.autocrlf=true` keeps its CRLF files on disk, and `git status` shows them as clean. To check a clone, look for `w/crlf` in `git ls-files --eol`. To fix one, rewrite it from the index on a clean tree: delete the tracked files, then run `git checkout-index -a`. That is EuGo-mcp's D4 method; `git checkout .` alone rewrites nothing. The only other clone is on the user's Linux machine, where the default is LF.

### D4. A separate `Check-Line-Endings` workflow with no path filter

`.github/workflows/check-line-endings.yaml` holds one job, also named `Check-Line-Endings`.

- **Triggers:**
  - `push` and `pull_request` with `branches: [main]`. Without the branch filter, a bare `push:` would also start it on every `v*` tag, beside Release's own call.
  - `workflow_call`, for Release.
  - `workflow_dispatch`, as the other workflows have.
- **No `paths`.** Include lists fail open, as `docs/ci-cd-best-practices.md` explains. The rule covers every file, including `.gitattributes` itself, `README.md`, `docs/` and `openspec/`, which no other workflow watches. The cost is one checkout and one `git` command per push.
- **Conventions from `docs/ci-cd-best-practices.md`:**
  - `permissions: contents: read` at the top;
  - `timeout-minutes` on the job (5 minutes);
  - `defaults: run: shell: bash`, so the script runs with `-eo pipefail`;
  - `actions/checkout@v7`, the tag the other workflows use.
- **The script is inline.** The workflow file is the unit other repositories copy, and a separate `.sh` would make it two files.

Alternatives considered:

- **A step in each existing workflow.** It would run three times on some changes and never on others, such as a change to `docs/` only.
- **A step in Build-Helm or Check-Docker.** They are filtered to their own inputs.

### D5. What the check reads, and what it skips

The check reads `git ls-files --eol -z`. For each tracked file, that reports how the file is **stored** (`i/…`) and its attributes (`attr/…`), which is what the spec constrains. The check fails on `i/crlf` or `i/mixed` unless the attributes unset `text` (`attr/-text`, which `binary` also sets). It writes one `::error file=<path>::<path> is stored with CRLF line endings` per file, escaping `%`, `:` and `,` in the path as workflow commands require.

It also fails when `git ls-files` lists no files at all, because a check with nothing to read would pass. On success it prints how many files it read.

A probe repository on this machine showed which exemption is right. It was set up with `* text=auto eol=lf`, `*.cmd text eol=crlf`, `raw.txt -text` and `*.png binary`, then CRLF files were committed:

| File | Attributes | Stored | Checked out |
|---|---|---|---|
| `crlf.md` | `text=auto eol=lf` | `i/lf` | LF |
| `run.cmd` | `text eol=crlf` | `i/lf` | CRLF |
| `raw.txt` | `-text` | `i/crlf` | as stored |
| `img.png` | `binary` | `i/-text` | as stored |
| `legacy.cmd`, a CRLF blob added to the index with no conversion | `text eol=crlf` | `i/crlf` | — |

- **A file declared `eol=crlf` is stored LF.** The check never sees it as CRLF, so it needs no exemption. This is the spec's scenario "A file declared as CRLF in the working tree".
- **A `-text` file keeps its bytes.** It shows as `i/crlf` if it contains CRLF. The repository treats it as binary, so the check must skip it. This is the spec's scenario "A binary file is committed".
- **A file declared `eol=crlf` but stored CRLF** (`legacy.cmd`) is stored wrongly. The check must report it.

EuGo-mcp's D5 planned to skip files with `eol=crlf` instead. The probe shows that this gets both cases wrong: it would fail on a `-text` file that contains CRLF, and it would pass `legacy.cmd`. Its applied check, in `ci.yml` after `f52a5bd`, also skips `-text`, but it still skips `eol=crlf`, so it passes `legacy.cmd`. It also doesn't fail on an empty listing and doesn't escape the path in `file=`.

The user chose the check described here for example-net. EuGo-mcp catching up is a separate change in that repository.

Alternatives considered:

- **`git add --renormalize . && git diff --cached --name-only`.** It lists what Git would change, so it follows the attributes exactly. But it changes the runner's index, and its result depends on the working tree and the clean filter rather than on the stored bytes alone.
- **Searching files for CR bytes.** It reads the working tree, not what is stored, and it reports binary files.

### D6. Release calls the check as a third gate

`release.yaml` gets a job between `Check-Helm` and `Build-and-Push-Docker`:

```yaml
  # Line-ending check, alongside Test and Check-Helm.
  Check-Line-Endings:
    needs: Check-Tag
    uses: ./.github/workflows/check-line-endings.yaml
    permissions:
      contents: read
```

- **`Build-and-Push-Docker`** changes to `needs: [Test, Check-Helm, Check-Line-Endings]`.
- **The comment above `Test`** ("The image waits for this and for Check-Helm") names all three gates.
- **No `timeout-minutes` on this job.** A job that calls a reusable workflow can't take one, so the limit lives inside the called workflow.
- **`needs: Check-Tag`** keeps "Release tags name an exact version": a rejected tag checks nothing.

Placing the job at the end of the file would leave every line reference in `docs/ci-cd-best-practices.md` valid. It is placed with the other gates instead, because a reader of the job graph looks for gates there. The references are updated (D8).

### D7. The label script stops stripping CR

Both `tr -d '\r'` are removed from `check-chart-labels.sh`. With D1 and D5 in place, the chart the script reads is LF in CI and in every local checkout. If a CR ever reached the chart, the strip would let a CRLF chart pass the label check and be published. The line-ending check would report the file by name first. EuGo-mcp's D1 rejects stripping CR for the same reason.

The script is in Build-Helm's `paths`, so the push runs the chart checks against the change.

### D8. Documentation

**`CLAUDE.md`:**

- **A new section, "Line endings":**
  - the rule, and the two files that hold it;
  - Check-Line-Endings, and what it skips;
  - Git Bash's `grep` drops the CR before matching, so to see what Linux sees use `grep -U` or `od -c`;
  - `.editorconfig` holds line endings only, with the Roslyn caveat from D2.
- **"CI/CD and versioning":**
  - a fifth item for Check-Line-Endings;
  - Release's "Two gates … unless both pass" becomes three gates.

**`docs/ci-cd-best-practices.md`:**

- **A new section, "Repository",** before "Triggers and gating", with one practice: "Every text file is LF in every checkout ✅". It covers `.gitattributes`, `.editorconfig`, Check-Line-Endings and the release gate, and points to `CLAUDE.md` for how to rewrite a clone (D3). The table of contents gains the section.
- **Items the change makes stale:**
  - "Every input to a build triggers the check that covers it" (line 29): four of the five workflows use include lists, and Check-Line-Endings runs on every change.
  - "Every job has a time limit" (line 57): Check-Line-Endings' job has one too.
  - "The workflow token has the least privilege it needs" (line 67): Check-Line-Endings sets `contents: read` at the top.
  - "Nothing is published until every check passes" (line 257): the image push waits for three gates.
  - "The release reuses the CI workflows" (line 261): `Check-Line-Endings` is reused as well.
  - "Inline scripts run in strict mode" (line 265): Check-Line-Endings already sets `defaults: run: shell: bash`.
- **Line references into `release.yaml`.** The page has 16 of them, on 9 lines. Several have no file name and belong to the file named last before them on the same line, for example `(release.yaml:213, :219)` on line 139.
  - **They move.** The new job is inserted after line 55. The 14 references to later lines move by the number of lines inserted: `:84-86`, `:90-93`, `:107-108`, `:138` (twice), `:144-146`, `:145`, `:159`, `:173-180`, `:213` (twice), `:219-221`, `:219` and `:220`.
  - **They stay.** The two references to earlier lines are `:13-14` and `:29`.
  - **The check.** A script prints each referenced line before and after the edit, and the two listings must show the same content.

## Risks / Trade-offs

- **[The release gate first runs on a real tag.]** No push or pull request runs `release.yaml`, and `docs/ci-cd-best-practices.md` already marks the release path ❓.
  → actionlint checks the call and the `needs` graph. The called workflow itself runs on every push. Recorded as **not run** until the next `v*` tag.
- **[Every push and pull request runs one more workflow.]**
  → Accepted. It is a checkout and one `git` command.
- **[A clone with CRLF on disk isn't rewritten.]**
  → This working tree is LF. D3 gives the check and the fix for any other clone.
- **[An editor saves CRLF anyway,** for example VS Code without the EditorConfig extension.]
  → Git stores LF on `git add` and prints "CRLF will be replaced by LF". The working copy differs from the stored file until it is checked out again.
- **[The label check now fails if a CR reaches the chart.]**
  → Intended (D7). The line-ending check names the file in the same push.
- **[EuGo-mcp's applied check differs from D5 here.]**
  → Out of scope for this change. The user carries the finding to EuGo-mcp.
- **[Unrelated local edits.]** Eight files under `.claude/` are modified in this working tree and are not part of this change.
  → They are never staged. Every commit is checked with `git diff --cached --name-only`.

## Migration Plan

1. **Precondition.** `main` equals `origin/main`. The only working-tree changes are the eight `.claude/` files and this change's planning artifacts. Record the `git ls-files --eol` counts.
2. **Implement and verify locally,** following tasks.md:
   - the two root files;
   - the workflow and its negative controls;
   - the Release gate;
   - the label script;
   - the documentation.
3. **Commit, with the user's go-ahead.** One commit holds the implementation and this change's planning artifacts, as earlier changes did.
4. **Push, with the user's go-ahead.** On that push:
   - Check-Line-Endings runs and passes;
   - Build-Helm runs, because the label script changed, and passes;
   - Build-Dotnet and Check-Docker don't run.
5. **The next `v*` tag** runs the release gate. Until then it is recorded as **not run**.

Rollback: revert the commit. The files stay LF without `.gitattributes`, so nothing else needs undoing.
