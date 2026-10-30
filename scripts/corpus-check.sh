#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
#
# Runs squalify's private-corpus check over one directory of real files.
# Usage: ./scripts/corpus-check.sh [--require-golden] <dir>
#
# The directory must lie outside the repository, or inside it and git-ignored. The script refuses any other directory
# inside the repository, before it creates anything. Every file under <dir> is profiled and checked for determinism (a
# repeat run, then runs under de-DE and tr-TR) and against <file>.squalify.json next to it, when that golden exists.
# Hidden files and directories, directory links and goldens are not checked. SQUALIFY_UPDATE_GOLDEN=1 rewrites goldens
# inside <dir>.
#
# A file with no golden is reported as "no golden", not as passed: it was profiled and found deterministic, but compared
# with nothing. The summary line counts passed, failed and no-golden entries apart and says so when any has no golden.
# By default that does not change the exit status. With --require-golden (or SQUALIFY_REQUIRE_GOLDEN=1 in the
# environment) an entry with no golden fails the run: the exit status is non-zero and the summary says why.
#
# The script writes no output derived from the data outside <dir>. Its run log goes to <dir>/.squalify-corpus/run.log,
# a hidden directory that the listing skips, and so does the summary (<dir>/.squalify-corpus/summary.txt). The build
# output goes to the repository's bin/ and obj/, as for any test run. See "Tests" in AGENTS.md.
set -euo pipefail

usage="usage: corpus-check.sh [--require-golden] <dir>"
dir=""
for arg in "$@"; do
  case "$arg" in
    --require-golden) export SQUALIFY_REQUIRE_GOLDEN=1 ;;
    *)
      if [[ -n "$dir" || -z "$arg" ]]; then
        echo "$usage" >&2
        exit 2
      fi
      dir="$arg"
      ;;
  esac
done
if [[ -z "$dir" ]]; then
  echo "$usage" >&2
  exit 2
fi
if [[ ! -d "$dir" ]]; then
  echo "corpus-check: not a directory: $dir" >&2
  exit 2
fi

# Physical absolute paths, derived from this script's own location, so the caller's working directory and CDPATH
# do not matter. A leading "-" in the directory is safe because of "--".
corpus="$(CDPATH= cd -- "$dir" && pwd -P)"
root="$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"

# A directory inside the repository is allowed only when git ignores it, so real files cannot be committed by accident.
case "$corpus/" in
  "$root/"*)
    if ! git -C "$root" check-ignore -q "$corpus"; then
      echo "corpus-check: refusing $corpus: it is inside the repository and not git-ignored." >&2
      echo "corpus-check: move the corpus outside $root, or add it to .gitignore." >&2
      exit 2
    fi
    ;;
esac

out="$corpus/.squalify-corpus"
log="$out/run.log"
summary="$out/summary.txt"
mkdir -p "$out"
# A summary left by an earlier run must not be mistaken for this one.
rm -f "$summary"

set +e
SQUALIFY_CORPUS="$corpus" dotnet test "$root/tests/squalify.Tests/squalify.Tests.csproj" \
  -c Release --filter "Category=Corpus" --logger "console;verbosity=normal" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
set -e

echo
echo "corpus-check: per-file results (local output; the full log is $log)"
# An entry with no golden runs in its own test (Corpus_EachFile_HasNoGolden_NotCompared). That test passes unless a
# golden is required, but the entry is not passed: label it "No golden". A "none" row is a placeholder for an empty
# theory, not an entry: drop it.
grep -E '^ +(Passed|Failed|Skipped) ' "$log" | sed -E 's/ \[[^]]*\]$//' \
  | grep -v 'kind: "none"' \
  | sed -E '/Corpus_EachFile_HasNoGolden_NotCompared/ s/^( +)Passed /\1No golden /' || true
if [[ -f "$summary" ]]; then
  tail -n 1 "$summary"
else
  echo "corpus-check: no summary was written (the run did not reach the corpus tests; see the log)"
fi
echo "corpus-check: dotnet test exit $status"
exit "$status"
