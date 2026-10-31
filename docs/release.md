# Releasing squalify

squalify publishes two packages to nuget.org from a version tag: `squalor.squalify` (the library) and `squalor.squalify.Cli` (the `squalify` .NET tool). The tag must equal the `<Version>` in `Directory.Build.props`, and the workflow checks that.

## Cut a release

1. Bump `<Version>` in `Directory.Build.props` to the new version, for example `0.1.1` or `0.2.0-rc.1`. The golden descriptors record the tool version, so re-record them (`SQUALIFY_UPDATE_GOLDEN=1 dotnet test squalify.sln -c Release`) and check that the diff changes only the tool version value (the `version` inside the one-line `"tool": { ... }`) and, in a minor release that changed the schema, the `schemaVersion` value (the first schema-changing commit after a release bumps it; see the [versioning policy](descriptor.md#versioning-policy)). If the guides show `squalify --version` output, update it too. Edit the `$id` of `schema/descriptor-v1.schema.json` to the raw URL of the new tag (`https://raw.githubusercontent.com/squalor-xyz/squalify/v<Version>/schema/descriptor-v1.schema.json`). Label the release's changes in the user guide, as described in [Label the changes in the user guide](#label-the-changes-in-the-user-guide).
2. Commit the bump to `main` and wait for CI to pass.
3. Tag that commit `v<Version>` and push the tag:

   ```bash
   git tag v0.1.1
   git push origin v0.1.1
   ```

4. Watch the Release run in GitHub Actions.

The release is ungated by owner decision: there is no required reviewer on the `release` environment, so any `v*` tag on a commit that is on `main` publishes. The workflow fails, before it builds anything, if the tag version differs from `<Version>`.

## Label the changes in the user guide

`docs/user-guide.md` lists the library changes of each release under "Guarantees and stability". The test `UserGuide_LabelsTheCurrentChangesWithTheVersionInDirectoryBuildProps` checks the labels for the `<Version>`, which this section calls X:

- A release with API additions or breaking changes has `- **API added in X (library).**` and `- **Breaking changes in X (library).**`, one entry of each. An empty kind is still labelled, for example `- **Breaking changes in X (library).** None.`
- A patch release with no API additions and no breaking changes has the single line `- **No API or breaking changes in X (library).**` and no entries for X.
- Neither the entries nor the line for X may say "unreleased", and none of them may name a version newer than X. So the release commit drops "unreleased" from its own labels.
- No entry for a version up to X may say "unreleased". An entry for a later version may, since that version is not released yet.

A prerelease X keeps its suffix, for example `0.3.1-rc.1`; the test compares the numbers before the suffix.

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
