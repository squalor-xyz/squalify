# Releasing squalify

squalify publishes two packages to nuget.org from a version tag: `squalor.squalify` (the library) and `squalor.squalify.Cli` (the `squalify` .NET tool). The tag must equal the `<Version>` in `Directory.Build.props`, and the workflow checks that.

## Cut a release

1. Bump `<Version>` in `Directory.Build.props` to the new version, for example `0.1.1` or `0.2.0-rc.1`. The golden descriptors record the tool version, so re-record them (`SQUALIFY_UPDATE_GOLDEN=1 dotnet test squalify.sln -c Release --filter "Category!=Corpus&Category!=Large"`) and check that the diff changes only the tool version value (the `version` inside the one-line `"tool": { ... }`) and, in a minor release that changed the schema, the `schemaVersion` value (the first schema-changing commit after a release bumps it; see the [versioning policy](descriptor.md#versioning-policy)). If the guides show `squalify --version` output, update it too. In a minor or major release also set the `Status (vX.Y.x)` banner of `docs/how-it-works.md` to the new series (the test `Docs_SpeakOfTheFreezeAndTheVersionInThePresentTense` pins it to the major.minor of `<Version>`). Edit the `$id` of `schema/descriptor-v1.schema.json` only in a release that writes a new `schemaVersion`, to the raw URL of the new tag (`https://raw.githubusercontent.com/squalor-xyz/squalify/v<Version>/schema/descriptor-v1.schema.json`): a release that leaves the schema as it is keeps the `$id` of the release that first wrote it, so the tag's major.minor equals `schemaVersion`. Label the release's changes in the user guide, as described in [Label the changes in the user guide](#label-the-changes-in-the-user-guide).
2. Commit the bump to `main` and wait for CI to pass.
3. Tag that commit `v<Version>` and push the tag:

   ```bash
   git tag v0.1.1
   git push origin v0.1.1
   ```

4. Watch the Release run in GitHub Actions.
5. When nuget.org lists the release, move the package validation baseline up: commit to `main` the new `PackageValidationBaselineVersion` in `src/squalify/squalify.csproj` (the release you just published), regenerate or edit `src/squalify/CompatibilitySuppressions.xml` so that it holds no suppression the pack reports as unnecessary, and wait for CI to pass. See [Package validation](#package-validation).

The release is ungated by owner decision: there is no required reviewer on the `release` environment, so any `v*` tag on a commit that is on `main` publishes. The workflow fails, before it builds anything, if the tag version differs from `<Version>`.

## Label the changes in the user guide

`docs/user-guide.md` lists the library changes of each release under "Guarantees and stability". The test `UserGuide_LabelsTheCurrentChangesWithTheVersionInDirectoryBuildProps` checks the labels for the `<Version>`, which this section calls X:

- A release with API additions or breaking changes has `- **API added in X (library).**` and `- **Breaking changes in X (library).**`, one entry of each. An empty kind is still labelled, for example `- **Breaking changes in X (library).** None.`
- A patch release with no API additions and no breaking changes has the single line `- **No API or breaking changes in X (library).**` and no entries for X.
- Neither the entries nor the line for X may say "unreleased", and none of them may name a version newer than X. So the release commit drops "unreleased" from its own labels.
- No entry for a version up to X may say "unreleased". An entry for a later version may, since that version is not released yet.

A prerelease X keeps its suffix, for example `0.3.1-rc.1`; the test compares the numbers before the suffix.

## Package validation

`squalor.squalify` is packed with package validation against a baseline release from nuget.org (`EnablePackageValidation` and `PackageValidationBaselineVersion` in `src/squalify/squalify.csproj`). A change that removes or renames a public member or changes a signature fails `dotnet pack` with an API compatibility error (`CP0002`, for example), in CI, in the release workflow and locally. The CLI tool project is not validated.

- **Where the baseline comes from.** The baseline package is fetched from nuget.org at restore, not at pack: the SDK adds it as a `PackageDownload`, so `dotnet restore squalify.sln` puts `squalor.squalify` at the baseline version in the NuGet cache, and a later `dotnet pack --no-restore` (the Package tests, the release workflow) reads it from there. A restore that cannot reach nuget.org and has no copy in the cache fails. A pack with `--no-restore` and no copy in the cache fails with a `FileNotFoundException`. The check is incremental: a pack that changed nothing in the package does not run it again.
- **What may take a suppression.** Only two kinds of change may take a suppression in 1.x: (a) constructors and `Deconstruct` of the positional model records, which are outside the compatibility promise (so that a record such as `Issue`, `EncodingSection`, `Limits` or `RowClassesInfo` can gain a trailing member in a minor release; validation reports the old constructor and `Deconstruct` as removed), and (b) the provisional model types that the user guide says "may change in a minor" (`TouchstoneDialect`, `KeyValueDialect`, `StdfDialect`, `KeyValueSection`, `BlockStats`, `StdfSection`, `StdfRecordCount`, `StdfTest`, `StdfHints`, `Descriptor.Sections` and `Descriptor.Stdf`, `BinarySection.Blocks` and `BinarySection.Segments`; see "Model records" in the [user guide](user-guide.md#guarantees-and-stability)). Any other `CP` error in 1.x is a bug to fix, not to suppress: removing, renaming or changing the signature of any other public member needs a major version.
- **Adding a suppression.** Run `dotnet pack src/squalify/squalify.csproj -c Release -p:ApiCompatGenerateSuppressionFile=true -o <folder>`, which writes the entries to `src/squalify/CompatibilitySuppressions.xml`; check that they name only members of the two kinds above; and commit them with the change and a `Breaking changes in X (library)` entry in the user guide in the same commit (X is the version that ships it). A suppression without that entry is a defect.
- **The baseline moves up with each release.** After each release is listed on nuget.org, the next commit to `main` sets `PackageValidationBaselineVersion` to that release and removes the suppressions the pack then reports as unnecessary. A suppression that is no longer needed fails the pack (`error : Unnecessary suppressions found`), so the bump cannot leave one behind; running the pack with `-p:ApiCompatGenerateSuppressionFile=true` rewrites the file without them (delete the file when no entry is left). A new major release moves the baseline to X.0.0 and drops the suppressions. So a member added in 1.1 cannot disappear in 1.2 unnoticed.

## What the workflow does

`.github/workflows/release.yml` runs on every pushed tag that starts with `v`.

1. **Build job** (`contents: read`). It fails unless the tag parses as a SemVer version, the tagged commit is an ancestor of `origin/main`, and the tag version equals the `Version` that MSBuild reads (`dotnet msbuild -getProperty:Version`), string for string. It builds, runs the tests (everything except `Category=Corpus`, `Category=Package` and `Category=Large`, then the Package tests as their own step), and packs both projects with `-p:Version=<tag without v>` and `-p:ContinuousIntegrationBuild=true` into `artifacts/nuget`. It uploads the packages as a workflow artifact and outputs the version.
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
