# Releasing RecDiffy.NET

Packages published by the release workflow are `RecDiffy`, `RecDiffy.RulePacks.Trading`, `RecDiffy.Excel` and `RecDiffy.IO`. Tests, benchmarks, samples and the AOT host are not packed.

Every package is a prerelease for now. A pack whose version has no prerelease label, such as `1.2.0`, fails the build, and the release workflow rejects a tag without one.

## Release scope

The four packages ship together, from one commit, at one version. The trading pack shipped with the first core preview, and still ships with every release.

| Package | What it is |
|---|---|
| `RecDiffy` | The engine: profiles, normalization, matching, comparison and results. |
| `RecDiffy.RulePacks.Trading` | Trading rules: party, side, quantity and 32nds price. |
| `RecDiffy.IO` | CSV and Excel record sources, read through Sylvan. |
| `RecDiffy.Excel` | Back- and middle-office Excel reports from a result. |

- **Each package depends on `RecDiffy` at its own version, as a minimum.** For example, `RecDiffy.RulePacks.Trading` 1.2.0-preview.3 depends on `RecDiffy` 1.2.0-preview.3 or later. NuGet lets a consumer take a newer `RecDiffy` than the other packages were built with.
- **The trading pack uses the engine's internals** (`InternalsVisibleTo` in [RecDiffy.csproj](../src/RecDiffy/RecDiffy.csproj)), so it may not work with an engine from another release. Tell consumers to keep all four packages on one version.
- **Only the four libraries are packed.** The release workflow names them one by one, and both sample projects set `IsPackable=false` ([#86](https://github.com/m-vaysman/recdiffy.net/issues/86)). The [Packages workflow](../../.github/workflows/packages.yml) packs the whole solution on every pull request, and fails if anything else comes out.
- **The Excel add-in isn't a package.** It references `RecDiffy` and `RecDiffy.IO` from nuget.org at a version it names itself, `1.2.0-preview.2` today, so a release doesn't change it. Moving it to a new version is its own PR.

What runs on which framework and operating system is in [SUPPORT.md](SUPPORT.md).

## Versions

### The version sequence

- **Releases are `1.2.0-preview.N`.** `1.2.0-preview.1`, `.2` and `.3` are on nuget.org, published on 9 and 10 November 2026. The next is `1.2.0-preview.4`.
- **Every number is used once.** nuget.org never deletes a version, and a version can't be pushed twice.
- **Numbers may skip.** A gap is harmless. A number lower than one already published isn't.
- **No stable version yet.** `v1.2.0` and other tags without a label fail the workflow before anything is packed. Lifting that rule is the maintainer's decision. The earlier plan for a `0.1.0` preview line was dropped in favour of `1.2.0-preview.N`.

### One public channel

nuget.org prereleases are the only public channel. Two other workflows pack the libraries for this repository's own checks. They keep the packages as workflow artifacts, and publish nothing.

| Workflow | Trigger | Version | Where the packages go |
|---|---|---|---|
| [release.yml](../../.github/workflows/release.yml) | A pushed `vX.Y.Z-label` tag | The tag without its `v`, such as `1.2.0-preview.3` | nuget.org, and the GitHub Release |
| [packages.yml](../../.github/workflows/packages.yml) | Every pull request, and every push to `dev` and `main` | `0.0.0-ci.<run number>` | The `packages` workflow artifact, kept for seven days |
| [ci.yml](../../.github/workflows/ci.yml) | Every push to `main` | `1.1.0-preview.<run number>` | The `nuget-prerelease` workflow artifact |

- **Point consumers at nuget.org.** Nothing else is a release.
- **GitHub Packages still holds older `1.1.0-preview` builds** from before CI stopped publishing there. NuGet orders every `1.1.0-preview` below every `1.2.0-preview`, so a consumer with both feeds still gets the nuget.org release.

### The engine version in a result

`result.Run.EngineVersion` is the package version, without build metadata: the assembly's informational version up to the `+`, such as `1.2.0-preview.3` from `1.2.0-preview.3+<commit>`. It is read from the assembly and works under Native AOT and trimming.

- **A prerelease label survives.** The assembly version drops it, so it is not used unless the informational version is missing. Every `1.2.0` preview before [#295](https://github.com/m-vaysman/recdiffy.net/issues/295) reported `1.2.0.0`, and a result could not say which preview made it.
- **A local build that sets no version** reports `1.0.0`, the `<Version>` in [RecDiffy.csproj](../src/RecDiffy/RecDiffy.csproj).
- **The PackageSmoke consumer check pins it.** `consume.sh` packs `0.0.0-ci.<run>`, runs the samples from the packages, and fails unless `EngineVersion` equals the packed version, on both frameworks. `publish-aot.sh` runs the same sample from the Native AOT and trimmed builds.

## Release to nuget.org

1. Merge the change to `main`.
2. Tag that commit `vX.Y.Z-label`, for example `v1.2.0-preview.1`.
3. Push the tag: `git push origin v1.2.0-preview.1`.

The workflow builds, runs the tests, packs all four projects as that version, checks those exact files the way a consumer gets them, pushes the packages to nuget.org, and opens a GitHub Release marked as a prerelease, with the packages attached.

**The pushed files are the checked files.** Before `Log in to nuget.org`, the job copies `artifacts/*.nupkg` into `RecDiffy.NET/tests/PackageSmoke/feed` and runs the two checks the [Packages workflow](../../.github/workflows/packages.yml) runs on every pull request:

- `RecDiffy.NET/tests/PackageSmoke/consume.sh "$VERSION"` restores each package from the local feed, checks its files, dependencies and metadata, runs every sample on `net8.0` and `net10.0`, and asserts that `EngineVersion` equals the packed version.
- `RecDiffy.NET/tests/PackageSmoke/publish-aot.sh "$VERSION"` publishes the AOT-safe samples with Native AOT and with trimming, on `net8.0` and `net10.0`, with every warning an error, and runs them.

Either script exits nonzero on a failure, and the job stops before anything is pushed. Before [#296](https://github.com/m-vaysman/recdiffy.net/issues/296) the release packed again from the tag and pushed without either check, so nuget.org got files no check had seen.

The workflow publishes through nuget.org Trusted Publishing, so no API key is stored. GitHub gives the release job a signed token, `NuGet/login` exchanges it for a nuget.org key that lasts one hour, and the push uses that key. The nuget.org policy `RecDiffy` trusts only `release.yml` in `m-vaysman/recdiffy.net`, for the packages `RecDiffy` and `RecDiffy.*`, owned by `cobblerhill`.

Add a repository secret named `NUGET_USER` before the first tag. It holds the nuget.org username of the account that created the policy, not an API key.

If a release goes wrong, follow [RELEASE-RECOVERY.md](RELEASE-RECOVERY.md).

## GitHub Packages

Nothing is published to GitHub Packages. The `1.1.0-preview.<run number>` builds there are from before CI stopped publishing them. See [One public channel](#one-public-channel).

## After a release

The push uses `--skip-duplicate`, so a version that is already on nuget.org is skipped without an error. A green run doesn't prove that all four packages went out.

1. **Check nuget.org.** Each of the four packages should list the new version. A new version can take a few minutes to appear while nuget.org validates and indexes it.
2. **Check the GitHub Release.** It should be marked as a prerelease, with four `.nupkg` and four `.snupkg` files.

**`dev` is never deleted or recreated.** Releases move `main` to `dev` by fast-forward only. The ruleset `dev-no-delete` blocks deleting `dev`.

## Known limitations of the preview

- **Prerelease API.** Names and behaviour can change between previews. The API review before 1.0 is [#45](https://github.com/m-vaysman/recdiffy.net/issues/45), and the compatibility promise is [#74](https://github.com/m-vaysman/recdiffy.net/issues/74).
- **Linux-only CI.** The library tests run only on Ubuntu ([#53](https://github.com/m-vaysman/recdiffy.net/issues/53)).
- **Release packages are smoke-tested before the push.** The release workflow copies the packed files into the PackageSmoke feed and runs the consumer and AOT checks on them ([#296](https://github.com/m-vaysman/recdiffy.net/issues/296)). They are not re-run on the files nuget.org serves after indexing, and the Excel add-in tests are not part of a release.
- **No XML API documentation** in the packages. No project sets `GenerateDocumentationFile`.
- **Package metadata gaps,** such as links in the packed README, are tracked in [#46](https://github.com/m-vaysman/recdiffy.net/issues/46).
- **`AttributedTo` can name roots that don't explain the difference** ([#254](https://github.com/m-vaysman/recdiffy.net/issues/254)).
- **In memory.** Both sides are read fully into memory before matching.
- **Profile gaps** that compile but misbehave are listed in [PROFILES.md](PROFILES.md#known-gaps).
- **Deferred:** installing the Excel add-in ([#198](https://github.com/m-vaysman/recdiffy.net/issues/198)), profile authoring tools ([#143](https://github.com/m-vaysman/recdiffy.net/issues/143), [#144](https://github.com/m-vaysman/recdiffy.net/issues/144), [#145](https://github.com/m-vaysman/recdiffy.net/issues/145)) and typed columns from a sample file ([#122](https://github.com/m-vaysman/recdiffy.net/issues/122)).
