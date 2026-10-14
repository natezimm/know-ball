#!/usr/bin/env bash
# Double-click in Finder to run the Phase 0 verification in Terminal.
cd "$(dirname "$0")/.." || exit 1
./scripts/verify.sh
status=$?
echo
echo "Finished with exit code $status. Full log: artifacts/verify.log"
exit $status
