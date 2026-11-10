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

### Two version lines

Two workflows publish packages, with different versions, to different feeds.

| Workflow | Trigger | Version | Feed |
|---|---|---|---|
| [ci.yml](../../.github/workflows/ci.yml) | Every push to `main` | `1.1.0-preview.<run number>` | GitHub Packages, `https://nuget.pkg.github.com/m-vaysman/index.json` |
| [release.yml](../../.github/workflows/release.yml) | A pushed `vX.Y.Z-label` tag | The tag without its `v`, such as `1.2.0-preview.3` | nuget.org |

What that means in practice:

- **One commit gets two versions.** A release merge to `main` publishes `1.1.0-preview.<run>` to GitHub Packages. Tagging the same commit then publishes `1.2.0-preview.N` to nuget.org.
- **GitHub Packages builds sort lower.** NuGet orders every `1.1.0-preview` below every `1.2.0-preview`. A consumer with both feeds gets the nuget.org release, even when a GitHub Packages build has newer code.
- **The run number counts every CI run,** including pull requests and pushes to `dev`, so the GitHub Packages numbers jump.
- **GitHub Packages builds are for this repository's own checks.** Point consumers at nuget.org.
- **A third version never leaves CI.** The [Packages workflow](../../.github/workflows/packages.yml) packs `0.0.0-ci.<run number>` on every pull request and push, to check the packages. It keeps them as a workflow artifact for seven days and publishes nothing.

Whether to align the two lines is an open decision for the maintainer. The workflows are unchanged until it is made.

### The engine version in a result

`result.Run.EngineVersion` is the `RecDiffy` assembly version. A prerelease version is dropped from it, so every `1.2.0` preview reports `1.2.0.0`, and the `1.1.0` builds report `1.1.0.0`. To tell previews apart, read the assembly's informational version, which carries the full version and the commit, such as `1.2.0-preview.3+<commit>`.

## Release to nuget.org

1. Merge the change to `main`.
2. Tag that commit `vX.Y.Z-label`, for example `v1.2.0-preview.1`.
3. Push the tag: `git push origin v1.2.0-preview.1`.

The workflow builds, runs the tests, packs all four projects as that version, pushes the packages to nuget.org, and opens a GitHub Release marked as a prerelease, with the packages attached.

The workflow publishes through nuget.org Trusted Publishing, so no API key is stored. GitHub gives the release job a signed token, `NuGet/login` exchanges it for a nuget.org key that lasts one hour, and the push uses that key. The nuget.org policy `RecDiffy` trusts only `release.yml` in `m-vaysman/recdiffy.net`, for the packages `RecDiffy` and `RecDiffy.*`, owned by `cobblerhill`.

Add a repository secret named `NUGET_USER` before the first tag. It holds the nuget.org username of the account that created the policy, not an API key.

## GitHub Packages

A push to `main` packs `1.1.0-preview.<run number>` and publishes those packages to GitHub Packages, not nuget.org.

## After a release

The push uses `--skip-duplicate`, so a version that is already on nuget.org is skipped without an error. A green run doesn't prove that all four packages went out.

1. **Check nuget.org.** Each of the four packages should list the new version. A new version can take a few minutes to appear while nuget.org validates and indexes it.
2. **Check the GitHub Release.** It should be marked as a prerelease, with four `.nupkg` and four `.snupkg` files.
3. **Check `dev`.** The repository deletes a pull request's branch when it merges, so the release merge from `dev` to `main` deletes `dev`. If `git ls-remote origin dev` prints nothing, recreate it at `main`: `git push origin origin/main:refs/heads/dev`.

## Known limitations of the preview

- **Prerelease API.** Names and behaviour can change between previews. The API review before 1.0 is [#45](https://github.com/m-vaysman/recdiffy.net/issues/45), and the compatibility promise is [#74](https://github.com/m-vaysman/recdiffy.net/issues/74).
- **Linux-only CI.** The library tests run only on Ubuntu ([#53](https://github.com/m-vaysman/recdiffy.net/issues/53)).
- **Release packages aren't smoke-tested.** The Packages workflow checks packages packed from each pull request. The release workflow packs again from the tag, and those files go to nuget.org without the consumer and AOT checks.
- **No XML API documentation** in the packages. No project sets `GenerateDocumentationFile`.
- **Package metadata gaps,** such as links in the packed README, are tracked in [#46](https://github.com/m-vaysman/recdiffy.net/issues/46).
- **`AttributedTo` can name roots that don't explain the difference** ([#254](https://github.com/m-vaysman/recdiffy.net/issues/254)).
- **The same engine version** for every preview of one release. See [The engine version in a result](#the-engine-version-in-a-result).
- **In memory.** Both sides are read fully into memory before matching.
- **Profile gaps** that compile but misbehave are listed in [PROFILES.md](PROFILES.md#known-gaps).
- **Deferred:** installing the Excel add-in ([#198](https://github.com/m-vaysman/recdiffy.net/issues/198)), profile authoring tools ([#143](https://github.com/m-vaysman/recdiffy.net/issues/143), [#144](https://github.com/m-vaysman/recdiffy.net/issues/144), [#145](https://github.com/m-vaysman/recdiffy.net/issues/145)) and typed columns from a sample file ([#122](https://github.com/m-vaysman/recdiffy.net/issues/122)).
