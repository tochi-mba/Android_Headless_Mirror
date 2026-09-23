# URGENT: Re-enable desktop UI automation in CI

This file tracks a temporary release exception. Do not delete it until the work below is complete.

## What is temporarily disabled

The `Rex.Tests.AppUiTests` desktop UI-automation suite is temporarily not run by GitHub Actions and is temporarily not a release gate.

The tests themselves remain in the repository. Only their CI execution has been removed.

## Why

On GitHub-hosted Windows runners, the UI-automation suite can run indefinitely while driving the real WPF application through Windows UI Automation and repeated process launches. In the previous single `dotnet test` job this made the entire CI run appear hung.

The CI investigation isolated the suites:

- fast/unit/contract tests complete normally;
- `Rex.Tests.AppEndToEndTests` completes successfully;
- website/Playwright tests complete successfully;
- `Rex.Tests.AppUiTests` is the long-running/hanging suite.

The release must not remain permanently dependent on a test suite that can block without producing a useful failure.

## Release coverage that remains required

A release is still blocked on all of the following:

1. warnings-as-errors build;
2. fast/unit/contract tests;
3. installer build;
4. published CLI smoke checks;
5. desktop E2E tests in `Rex.Tests.AppEndToEndTests`;
6. website/Playwright tests;
7. release asset checksum verification.

## Required fix

Before removing this file, make the UI-automation suite deterministic on GitHub-hosted Windows runners.

At minimum:

1. identify the specific UI test or helper that blocks;
2. ensure every UI operation has a bounded deadline;
3. ensure every spawned process is terminated on timeout/cancellation;
4. keep UI Automation waits cancellation-aware;
5. run `Rex.Tests.AppUiTests` repeatedly on a GitHub-hosted Windows runner without hangs;
6. restore a dedicated `Desktop UI automation (Windows)` job in `.github/workflows/ci.yml`;
7. add that job back to the release job's `needs` list;
8. confirm the full release workflow completes green;
9. then delete this file.

## Useful command

```powershell
dotnet test --project tests/Rex.Tests/Rex.Tests.csproj -c Release --no-build -- --filter-class Rex.Tests.AppUiTests --long-running 60 --xunit-diagnostics on
```

Do not solve this by deleting the UI tests or weakening their assertions. The goal is to make the UI-automation coverage reliable enough to become a release gate again.
