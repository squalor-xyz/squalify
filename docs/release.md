# Releasing squalify

squalify publishes two packages to nuget.org from a version tag: `squalor.squalify` (the library) and `squalor.squalify.Cli` (the `squalify` .NET tool). The tag must equal the `<Version>` in `Directory.Build.props`, and the workflow checks that.

## Cut a release

1. Bump `<Version>` in `Directory.Build.props` to the new version, for example `0.1.1` or `0.2.0-rc.1`. At v0.1.0, also edit the **Install** line in `README.md`, which now says v0.1.0 is not released yet, so the package's README on nuget.org reads correctly.
2. Commit the bump to `main` and wait for CI to pass.
3. Tag that commit `v<Version>` and push the tag:

   ```bash
   git tag v0.1.0
   git push origin v0.1.0
   ```

4. Watch the Release run in GitHub Actions.

The release is ungated by owner decision: there is no required reviewer on the `release` environment, so any `v*` tag on a commit that is on `main` publishes. The workflow fails, before it builds anything, if the tag version differs from `<Version>`.

The first release, v0.1.0, waits until slices sq-07 and sq-08 have landed.

## What the workflow does

`.github/workflows/release.yml` runs on every pushed tag that starts with `v`.

1. **Build job** (`contents: read`). It fails unless the tag parses as a SemVer version, the tagged commit is an ancestor of `origin/main`, and the tag version equals the `Version` that MSBuild reads (`dotnet msbuild -getProperty:Version`), string for string. It builds, runs the tests (everything except `Category=Corpus`, with the Package tests as their own step), and packs both projects with `-p:Version=<tag without v>` and `-p:ContinuousIntegrationBuild=true` into `artifacts/nuget`. It uploads the packages as a workflow artifact and outputs the version.
2. **Publish job** (`environment: release`, no reviewer; `id-token: write`, `contents: read`). It downloads the artifact, asks nuget.org for a temporary API key through `NuGet/login@v1`, and pushes the `.nupkg` files with `--skip-duplicate`, so re-running a release is safe. Each push also sends the `.snupkg` next to it. The key lives one hour, so the login step runs immediately before the push.
3. **GitHub release job** (`contents: write`). It runs only after both earlier jobs pass, downloads the artifact, and creates the GitHub release for the tag with generated notes and the `.nupkg` and `.snupkg` files attached. A version with a prerelease suffix (it contains `-`) is marked as a prerelease.

No long-lived nuget.org API key is stored anywhere. The workflow's GitHub OIDC token is exchanged for the temporary key.

## nuget.org trusted publishing policy

Set up once on nuget.org, under the `SqualorLLC` organization:

| Field | Value |
|---|---|
| Policy owner | `SqualorLLC` |
| Repository owner | `squalor-xyz` |
| Repository | `squalify` |
| Workflow file | `release.yml` |
| Environment | `release` |
| Scope | push new packages and package versions |
| Package glob | `squalor.squalify*` |

The `user` input of `NuGet/login@v1` is the nuget.org user profile name, `squalor`. It is written literally in the workflow because a profile name is public, not a secret. It is not the organization name.

## Adding a reviewer later

If the ungated decision changes, open Settings, then Environments, then `release` in the repository and turn on Required reviewers. The publish job then waits for approval.
