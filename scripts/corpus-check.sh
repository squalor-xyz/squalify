#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
#
# Runs squalify's private-corpus check over one directory of real files. Usage: ./scripts/corpus-check.sh <dir>
#
# The directory must lie outside the repository, or inside it and git-ignored. The script refuses any other directory
# inside the repository, before it creates anything. Every file under <dir> is profiled and checked for determinism (a
# repeat run, then runs under de-DE and tr-TR) and against <file>.squalify.json next to it, when that golden exists.
# Hidden files and directories, directory links and goldens are not checked. SQUALIFY_UPDATE_GOLDEN=1 rewrites goldens
# inside <dir>.
#
# The script writes no output derived from the data outside <dir>. Its run log goes to <dir>/.squalify-corpus/run.log,
# a hidden directory that the listing skips. The build output goes to the repository's bin/ and obj/, as for any test
# run. See "Tests" in AGENTS.md.
set -euo pipefail

if [[ $# -ne 1 || -z "$1" ]]; then
  echo "usage: corpus-check.sh <dir>" >&2
  exit 2
fi
if [[ ! -d "$1" ]]; then
  echo "corpus-check: not a directory: $1" >&2
  exit 2
fi

# Physical absolute paths, derived from this script's own location, so the caller's working directory and CDPATH
# do not matter. A leading "-" in the argument is safe because of "--".
corpus="$(CDPATH= cd -- "$1" && pwd -P)"
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
mkdir -p "$out"

set +e
SQUALIFY_CORPUS="$corpus" dotnet test "$root/tests/squalify.Tests/squalify.Tests.csproj" \
  -c Release --filter "Category=Corpus" --logger "console;verbosity=normal" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
set -e

echo
echo "corpus-check: per-file results (local output; the full log is $log)"
grep -E '^ +(Passed|Failed|Skipped) ' "$log" | sed -E 's/ \[[^]]*\]$//' || true
passed=$(grep -cE '^ +Passed ' "$log" || true)
failed=$(grep -cE '^ +Failed ' "$log" || true)
echo "corpus-check: passed $passed, failed $failed, dotnet test exit $status"
exit "$status"
