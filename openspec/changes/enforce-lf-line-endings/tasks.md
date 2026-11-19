All work stays in the main session, because there are no C# or project file changes. Commits go straight to `main`. Ask the user before every commit and every push.

Run commands from the repo root in Git Bash. Prefix `docker run` with `MSYS_NO_PATHCONV=1`, so that Git Bash doesn't rewrite its paths.

Never stage anything under `.claude/`. Eight files there are modified and are not part of this change. Stage files by name only, and never run `git add --renormalize .` or `git add -A` in this working tree.

## 1. Baseline

- [x] 1.1 Verify the starting state (design, Migration Plan, step 1):
  - `git rev-list --left-right --count main...origin/main` prints `0	0`;
  - `git status --short` lists only the eight `.claude/` files and `openspec/changes/enforce-lf-line-endings/`.

  If anything else shows, stop and ask the user how to handle it.

  *Result (2026-11-19):* after `git fetch`, the count prints `0	0`. The status lists only the eight `.claude/` files and `?? openspec/changes/enforce-lf-line-endings/`.
- [x] 1.2 Record these counts in this task's result (design, Context):
  - `git ls-files --eol | awk '{print $1, $2}' | sort | uniq -c`. Expected: `136 i/lf w/lf` and `3 i/none w/none`.
  - `git grep -I -l -P '\r' HEAD | wc -l`. Expected: `0`.

  *Result (2026-11-19):* `136 i/lf w/lf`, `3 i/none w/none`, and `0`. Planning first counted 130, which was measured on `6fcf9d6`, before the pull. `09c4dd2` adds six files. The proposal and the design Context were corrected to 136.

## 2. The repository rule (design D1 to D3)

- [x] 2.1 Create `.gitattributes` and `.editorconfig` at the root with exactly the content in design D2. Write them with LF line endings and a final newline.

  If EuGo-mcp has applied `normalize-line-endings-to-lf` by now, compare its two files with D2 first. If they match, copy them and verify with `cmp`. If they differ, stop and ask the user which one wins.

  Verify:
  - `tr -cd '\r' < .gitattributes | wc -c` and `tr -cd '\r' < .editorconfig | wc -c` both print `0`;
  - `git check-attr text eol -- Dockerfile helm/Chart.yaml .github/scripts/check-chart-labels.sh` reports `text: auto` and `eol: lf` for each path;
  - `git check-attr text -- logo.png` reports `text: unset`. The path doesn't need to exist.

  *Result (2026-11-19):* EuGo-mcp had applied its change, so its files were compared with D2.
  - **The rules matched** D2's, and its `.editorconfig` matched byte for byte.
  - **Its comments differ:** they name `ci.yml` and `continuous-integration`, which this repository doesn't have.
  - **Its check differs from D5:** it still skips `eol=crlf`.

  The user chose the same rules with this repository's comments, and the D5 check. Design D2, D5 and Context and the proposal were updated to match.

  Verification:
  - both files have 0 CR bytes;
  - `diff <(grep -v '^#' .gitattributes) <(grep -v '^#' <EuGo-mcp>/.gitattributes)` is empty;
  - `cmp` of the two `.editorconfig` files is silent;
  - `check-attr` reports `text: auto` and `eol: lf` for all three paths, and `text: unset` for `logo.png`.
- [x] 2.2 Prove in a throwaway clone that nothing needs renormalizing (design D3). Run:
  - `t=$(mktemp -d) && git clone -q . "$t/c"`;
  - copy the two files into `$t/c`;
  - inside `$t/c`, run `git add .gitattributes .editorconfig && git add --renormalize .`.

  Verify:
  - `git diff --cached --name-only` in `$t/c` lists exactly `.editorconfig` and `.gitattributes`;
  - `git ls-files --eol` there reports no `i/crlf` and no `i/mixed`.

  Keep `$t` for 2.3 and group 3.

  *Result (2026-11-19):* the clone was made in the session's scratch folder instead of `mktemp -d`. `git diff --cached --name-only` lists exactly `.editorconfig` and `.gitattributes`. No file is `i/crlf` or `i/mixed`.
- [x] 2.3 In the same clone, commit the two files, then check the spec scenario "A file saved with CRLF is committed":
  1. write `probe.md` with `printf 'a\r\nb\r\n'`;
  2. run `git add probe.md` and commit it.

  Verify:
  - `git add` warned that CRLF will be replaced by LF;
  - `git ls-files --eol probe.md` shows `i/lf`;
  - after `rm probe.md && git checkout -- probe.md`, `od -c probe.md` shows no `\r`.

  *Result (2026-11-19):*
  - `git add` printed `warning: CRLF will be replaced by LF in probe.md.`;
  - `git ls-files --eol probe.md` shows `i/lf w/crlf attr/text=auto eol=lf`;
  - after the checkout, `od -c` shows `a \n b \n`.
- [x] 2.4 Verify that this working tree needs no rewrite (design D3): `git ls-files --eol | grep -c 'w/crlf'` prints `0`.

  *Result (2026-11-19):* it prints `0`.

## 3. The Check-Line-Endings workflow (design D4, D5)

- [x] 3.1 Create `.github/workflows/check-line-endings.yaml` as design D4 describes, with the D5 script inline. Comments in the file say why it has no path filter, and why `push` names the branch.

  Verify by reading the file:
  - `name: Check-Line-Endings`;
  - `push` and `pull_request` both have `branches: [main]`, and the file has no `paths:`;
  - `workflow_call` and `workflow_dispatch` are present;
  - `permissions: contents: read` sits at the top;
  - `defaults: run: shell: bash` is set;
  - the job has `timeout-minutes: 5`, runs on `ubuntu-latest` and uses `actions/checkout@v7`.

  *Result (2026-11-19):* all six points hold. The step's comment says why `-text` is skipped and why `eol=crlf` needs no exemption.
- [x] 3.2 Lint every workflow: `MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W):/repo" -w /repo rhysd/actionlint:1.7.7 -color`. Verify:
  - no finding names `check-line-endings.yaml`;
  - any findings in the other workflows are recorded in this task's result as the baseline for 4.1. They are not fixed here.

  *Result (2026-11-19):* the run used `-no-color -oneline`. It reported 9 findings, all `shellcheck` SC2086 at info level ("Double quote to prevent globbing and word splitting"), and none in `check-line-endings.yaml`. This is the baseline for later runs:
  - `build-helm.yaml`: 6, in the `run:` blocks at lines 73 (3), 99 (2) and 150 (1);
  - `release.yaml`: 3, in the `run:` block at line 83, script lines 1 to 3, where `${{ }}` sits inside `run:`. `docs/ci-cd-best-practices.md` already lists these.
- [x] 3.3 Run the workflow's script locally in the throwaway clone from 2.2, for each case below (design D5). Copy it from the `run:` block into `$t/check.sh` and run it with `bash "$t/check.sh"`. Inject each blob without conversion: `b=$(printf '<bytes>' | git hash-object -w --stdin --no-filters)`, then `git update-index --add --cacheinfo 100644,$b,<path>`. Remove each probe with `git rm --cached -q <path>` before the next case, unless the case says otherwise.

  1. **Clean index:** exit 0, and the script prints how many files it read.
  2. **`probe-crlf.md` stored as `a\r\nb\r\n`:** exit 1, with `::error file=probe-crlf.md::`.
  3. **`probe-mixed.md` stored as `a\nb\r\n`:** exit 1, naming it.
  4. **`odd,na%me.md` stored with CRLF:** exit 1, with the file property escaped as `odd%2Cna%25me.md`. Git for Windows rejects `:` in an index path, so the `:` escape can't be probed here. The script escapes it all the same.
  5. **`probe-raw.txt` stored with CRLF, with `probe-raw.txt -text` added to `.gitattributes`:** exit 0.
  6. **`probe.png` stored as `\x89PNG\r\n\x1a\n\x00\x00`:** exit 0.
  7. **`probe.cmd` stored with CRLF, with `*.cmd text eol=crlf` added to `.gitattributes`:** exit 1, naming it. A file stored wrongly is reported even when the working tree is meant to be CRLF.
  8. **Cases 2 and 3 together:** exit 1, with one error for each file.
  9. **Empty index (`git rm --cached -r -q .`):** exit 1, with a message that no files were read.

  Revert `.gitattributes` in the clone between cases 5 and 7. Afterwards delete `$t`, and verify that `git status --short` in this working tree is unchanged since 2.4, apart from the new workflow file.

  *Result (2026-11-19):* the script was extracted from the `run:` block with `awk`, which removed the 8-space indent, and run with `bash -eo pipefail`, as `shell: bash` runs it.
  1. Exit 0: `Read 142 committed files. None is a text file stored with CRLF line endings.`
  2. Exit 1: `::error file=probe-crlf.md::… (i/crlf)`.
  3. Exit 1: `::error file=probe-mixed.md::… (i/mixed)`.
  4. Exit 1: `::error file=odd%2Cna%25me.md::`.
  5. `git ls-files --eol` showed `i/crlf … attr/-text`. Exit 0.
  6. It showed `i/-text … attr/-text`. Exit 0.
  7. It showed `i/crlf … attr/text eol=crlf`. Exit 1, naming `probe.cmd`.
  8. Exit 1, with two errors and `2 of 144 committed files are stored with CRLF line endings.`
  9. Exit 1: `::error::git ls-files listed no files, so there was nothing to check`.

  The clone was deleted afterwards. Apart from the 8 `.claude/` files, `git status --short` lists only `.editorconfig`, `.gitattributes`, `check-line-endings.yaml` and this change's folder.

## 4. Release's third gate (design D6)

- [x] 4.1 In `.github/workflows/release.yaml`:
  - add the `Check-Line-Endings` job from design D6, between `Check-Helm` and `Build-and-Push-Docker`;
  - change `Build-and-Push-Docker` to `needs: [Test, Check-Helm, Check-Line-Endings]`;
  - make the comment above `Test` name all three gates.

  Verify:
  - `grep -n -A1 'Check-Line-Endings:' .github/workflows/release.yaml` shows `needs: Check-Tag`;
  - `grep -n 'needs: \[Test' .github/workflows/release.yaml` shows all three gates;
  - actionlint, run as in 3.2, reports nothing for `release.yaml` beyond the 3.2 baseline;
  - `git diff -U0 .github/workflows/release.yaml` shows only these edits. Record how many lines were inserted before line 56, for 6.3.

  *Result (2026-11-19):*
  - **The new job and the gate.** `Check-Line-Endings:` is at line 57, followed by `needs: Check-Tag`. Line 64 reads `needs: [Test, Check-Helm, Check-Line-Endings]`.
  - **The diff** shows only the comment at line 39, the new job and the `needs` line.
  - **Lines inserted: 7,** after line 55. Old line 56 is now line 63.
  - **actionlint** reports the same 9 findings as the 3.2 baseline once line numbers are ignored.

## 5. The label script (design D7)

- [x] 5.1 Remove both `| tr -d '\r'` from `.github/scripts/check-chart-labels.sh`, and change nothing else. Verify:
  - `grep -c "tr -d" .github/scripts/check-chart-labels.sh` prints `0`;
  - `git diff --stat .github/scripts/check-chart-labels.sh` shows 2 lines changed.

  *Result (2026-11-19):* `0`, and `1 file changed, 2 insertions(+), 2 deletions(-)`.
- [x] 5.2 Run the label check locally as Build-Helm does:
  1. `t=$(mktemp -d) && helm package helm --version 1.3.1+662a06b343e7 --app-version 1.3.1 -d "$t"`. The app version has no build metadata, as in `build-helm.yaml:151-152`;
  2. both `check-chart-labels.sh` invocations from `build-helm.yaml:161-165`, on the one package in `$t` (`"$t"/*.tgz`).

  Verify:
  - both invocations exit 0 and print `Checking labels at chart version 1.3.1+662a06b343e7`;
  - `helm template t "$t"/*.tgz | tr -cd '\r' | wc -c` prints `0`.

  Delete `$t`.

  *Result (2026-11-19):*
  - **The first run used a wrong command.** The task as first written passed `--app-version 1.3.1+662a06b343e7`. Both invocations then failed on the `app.kubernetes.io/version` label: 6 of 32 values invalid, then 7 of 37. That is the script catching a real invalid label, not an effect of 5.1. Build-Helm passes the plain version as the app version, and the task was corrected to match.
  - **With Build-Helm's flags, both checks pass.** The script printed `All 32 label values are valid` (exit 0), then `All 37 label values are valid` (exit 0).
  - **No CR.** The render has 0 CR bytes, and so does `helm show chart`.

## 6. Documentation (design D8)

- [x] 6.1 Update `CLAUDE.md`:
  - add a "Line endings" section with the four points in design D8;
  - in "CI/CD and versioning", add Check-Line-Endings as a fifth item, and make Release's gates three.

  Verify:
  - `grep -n 'Check-Line-Endings' CLAUDE.md` shows both the fifth item and the Release line;
  - `grep -c 'Two gates' CLAUDE.md` prints `0`;
  - `grep -n 'grep -U' CLAUDE.md` shows the Git Bash note;
  - `grep -n 'dotnet_diagnostic' CLAUDE.md` shows the `.editorconfig` caveat.

  *Result (2026-11-19):*
  - **The new section.** "Line endings" sits between "Commands" and "Configuration flow". Besides the D8 points, it says how to find and rewrite a clone whose files are CRLF on disk (design D3), because the best-practices page points there.
  - **Check-Line-Endings** appears on lines 90 (Line endings), 151 (the Release gates) and 172 (item 5).
  - **The rest:** `Two gates` occurs 0 times, `grep -U` is on line 91, and `dotnet_diagnostic` on line 89.
- [x] 6.2 Update `docs/ci-cd-best-practices.md` as design D8 describes:
  - add the "Repository" section and its table of contents entry;
  - update the six stale items.

  Verify:
  - `grep -n '^## Repository$' docs/ci-cd-best-practices.md` finds the heading, and the table of contents links `#repository`;
  - `grep -c 'All four workflows' docs/ci-cd-best-practices.md` prints `0`;
  - `grep -n 'needs: \[Test, Check-Helm, Check-Line-Endings\]' docs/ci-cd-best-practices.md` finds the updated gate item.

  *Result (2026-11-19):*
  - **The new section.** `## Repository` is on line 24, and the table of contents links it on line 15. The section names the three things that hold the rule, and points to CLAUDE.md for a clone that is CRLF on disk.
  - **The stale items.** `All four workflows` occurs 0 times. The gate item is on line 276. The six items read:
    - "Four of the five workflows use include lists";
    - Check-Line-Endings' `timeout-minutes: 5`, referenced as `check-line-endings.yaml:29`, which was checked;
    - Check-Line-Endings in the permissions item;
    - the three gates;
    - the three `workflow_call` reuses;
    - the strict-mode item, which says Check-Line-Endings sets the `defaults`, and "add to every other workflow".
- [x] 6.3 Move the page's line references into `release.yaml` by the count recorded in 4.1 (design D8). Verify by script:
  - **Collect the references.** On each line of the page, a bare `:<n>` or `:<n>-<m>` belongs to the file named last before it on that line. For example, `(release.yaml:213, :219)` is two references to `release.yaml`.
  - **Count them.** Before the edit, the script must find 16 references to `release.yaml`, as design D8 lists. A different count means the parser is missing some, so fix the script before trusting the next check.
  - **Compare the lines.** For each reference after the edit, print its lines from the working tree. For the same reference before the edit, print its lines from `git show HEAD:.github/workflows/release.yaml`. `diff` of the two listings must be empty.

  References to `build-helm.yaml` and `check-docker.yaml` don't change, because those files don't.

  *Result (2026-11-19):* the script found 16 `release.yaml` references in the old page and 16 in the new one.
  - **14 moved by 7:** `84-86`→`91-93`, `90-93`→`97-100`, `138`→`145` (twice), `144-146`→`151-153`, `145`→`152`, `159`→`166`, `107-108`→`114-115`, `173-180`→`180-187`, `213`→`220` (twice), `219-221`→`226-228`, `219`→`226` and `220`→`227`.
  - **Two stayed:** `13-14` and `29`.
  - **The check:** `diff` of the old lines against the new lines is empty.

## 7. Commit and push

- [ ] 7.1 Before committing, verify:
  - `openspec validate enforce-lf-line-endings --strict` passes;
  - the script from 3.3, run against this working tree's index after staging, exits 0;
  - actionlint, run as in 3.2, reports nothing beyond the 3.2 baseline.

  Stage by name these 12 files:
  - `.gitattributes`
  - `.editorconfig`
  - `.github/workflows/check-line-endings.yaml`
  - `.github/workflows/release.yaml`
  - `.github/scripts/check-chart-labels.sh`
  - `CLAUDE.md`
  - `docs/ci-cd-best-practices.md`
  - the five files under `openspec/changes/enforce-lf-line-endings/`

  Verify that `git diff --cached --name-only` lists exactly those 12 files and nothing under `.claude/`. With the user's go-ahead, commit. Verify that `git status --short` then lists only the eight `.claude/` files.

  *Result before the commit (2026-11-19):*
  - **Validation.** `openspec validate --strict` reports the change valid.
  - **The script.** It was re-extracted from the workflow and matches the copy tested in 3.3. Run against the staged index, it printed `Read 147 committed files. None is a text file stored with CRLF line endings.` and exited 0.
  - **actionlint** reports the same 9 findings as the 3.2 baseline once line numbers are ignored.
  - **The index.** Exactly the 12 files are staged, none under `.claude/`, and all 12 are `i/lf`.
- [ ] 7.2 With the user's go-ahead, push. On the runs for the pushed commit (`gh run list --commit <sha>`), verify:
  - Check-Line-Endings succeeds, and its log prints the number of files it read;
  - Build-Helm runs, because the label script changed, and succeeds;
  - Build-Dotnet and Check-Docker don't run.

## 8. Live verification later (record **not run** until observed)

- [ ] 8.1 On the next `v*` tag, verify in the Release run:
  - `Check-Line-Endings` starts after `Check-Tag` and succeeds;
  - `Build-and-Push-Docker` starts only after all three gates.
- [ ] 8.2 On the user's Linux machine, pull with a clean tree. Verify that `git ls-files --eol | grep -c 'w/crlf'` prints `0`. If it doesn't, rewrite the tree as design D3 describes.
