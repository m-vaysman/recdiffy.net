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
- **Only the four named projects are packed.** The workflows name them one by one. The samples project isn't marked `IsPackable=false` yet ([#86](https://github.com/m-vaysman/recdiffy.net/issues/86)).
- **The Excel add-in isn't a package.** It references `RecDiffy` and `RecDiffy.IO` from nuget.org by exact version, so a release doesn't change it. Moving it to a new version is its own PR.

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

### Check the release

The push uses `--skip-duplicate`, so pushing a version that already exists succeeds without publishing anything. A green run doesn't prove all four packages went out. After every release:

1. **Check nuget.org.** Each of the four packages should list the new version. This lists them:

   ```bash
   for id in recdiffy recdiffy.rulepacks.trading recdiffy.io recdiffy.excel; do echo "$id: $(curl -s https://api.nuget.org/v3-flatcontainer/$id/index.json | tr -d ' \n')"; done
   ```

   A new version can take a few minutes to appear while nuget.org validates and indexes it.
2. **Check the GitHub Release.** It should be marked as a prerelease, with four `.nupkg` and four `.snupkg` files.
3. **Check `dev`.** The repository deletes a pull request's branch when it merges, so the release merge from `dev` to `main` deletes `dev`. If `git ls-remote origin dev` prints nothing, recreate it at `main`: `git push origin origin/main:refs/heads/dev`.

## GitHub Packages

A push to `main` packs `1.1.0-preview.<run number>` and publishes those packages to GitHub Packages, not nuget.org. The run also uploads them as the `nuget-prerelease` workflow artifact. See [Two version lines](#two-version-lines).

## Release recovery

What to do when a release goes wrong. In every case, two facts set the options:

- **nuget.org never deletes a version, and never takes the same version twice.** You fix a bad release by publishing a higher version, then deprecating or unlisting the bad one.
- **The release job can be re-run safely.** It rebuilds, retests and repacks the same commit. `--skip-duplicate` skips each package that is already on nuget.org and pushes the rest. Each run gets a fresh one-hour key. `v1.2.0-preview.1` was published this way: the first attempt failed on a flaky test ([#104](https://github.com/m-vaysman/recdiffy.net/issues/104)), and re-running the failed job published it.

### The workflow failed before it pushed

If the run stopped at the build, the tests, the tag check, the pack or the nuget.org login, nothing was published.

1. **Read the failed step.**
2. **If the cause was transient,** such as a flaky test or a network error, re-run the failed job.
3. **If the code needs a fix,** merge the fix through `dev` to `main`, and tag the fixed commit with the next number. The failed tag has no packages and no Release. Leave it, or delete it.
4. **If the tag was wrong,** such as `v1.2.0` with no label, delete it with `git push origin --delete <tag>`, and push a correct one.

### The publish was interrupted

Some packages reached nuget.org and others didn't, or the packages went out but the GitHub Release wasn't created.

1. **Find out what was published.** Run the check in [Check the release](#check-the-release).
2. **Re-run the failed job.** The packages already on nuget.org are skipped, the rest are pushed, and the GitHub Release is created.
3. **If the re-run can't publish the missing packages,** for example because the commit no longer builds, treat the release as out of step. See below.

A re-run attaches freshly packed files to the GitHub Release. Builds are deterministic, but the packages on nuget.org are the ones the first run pushed.

### The packages are out of step

One version of `RecDiffy` is on nuget.org without the matching trading, IO or Excel package, or the other way round.

The risk: a consumer who updates only `RecDiffy` gets an engine that the other packages weren't built against. The trading pack calls the engine's internals, so it can fail at run time with `MissingMethodException` or `TypeLoadException`.

1. **Publish the missing packages at the same version,** by re-running the release job for that tag.
2. **If that isn't possible,** release all four at the next number. Then deprecate the incomplete version of each package that did go out, with the new version as the alternative.
3. **Say which versions belong together** in the new Release's notes.

### A published package is bad

A package on nuget.org has a defect, a wrong dependency, or the wrong contents.

1. **Fix it on `dev`,** merge to `main`, and release all four packages at the next number. Release all four even if only one was bad, so the versions stay in step.
2. **Deprecate the bad version on nuget.org,** for each of the four packages. Sign in to the nuget.org account that owns them, open each package's Manage page, select the version, and choose Deprecation. Give the reason, and name the fixed version as the alternative. nuget.org and IDE package managers then mark it as deprecated, and `dotnet list package --deprecated` reports it.
3. **Unlist it as well if new projects must not pick it up.** An unlisted version is hidden from search, and NuGet won't pick it for a new install, but a project that already references it still restores it.
4. **Mark the GitHub Release.** Edit its notes to say not to use it, and link the fixed Release. Keep it as a prerelease.
5. **Tell users.** Open an issue that names the affected versions, the symptom and the fixed version, and link it from the fixed Release's notes.

For a security defect, don't describe it in a public issue before the fix is out. A private way to report vulnerabilities is being set up in [#241](https://github.com/m-vaysman/recdiffy.net/issues/241).

A bad `1.1.0-preview` build on GitHub Packages can be deleted outright from the package's settings on GitHub. Nothing outside this repository should depend on it.

### A version doesn't match

- **The tag isn't `vX.Y.Z-label`.** The workflow stops before packing. Push a correct tag.
- **The tag names a version already on nuget.org,** for example after a tag was deleted and pushed again. Every push is skipped as a duplicate, so nothing new is published even when the run succeeds. Tag the commit with the next number.
- **The tag is lower than the latest release,** such as `v1.1.0-preview.9`. It publishes, but it sorts below the latest release, so NuGet never picks it as an update. Deprecate it, and release the next number.
- **A local pack has no version.** `RecDiffy.csproj` says `1.0.0`, and a pack without `-p:Version=X.Y.Z-label` fails the prerelease check. Pass the version, as the workflows do.
- **Two previews report the same engine version.** That is expected. See [The engine version in a result](#the-engine-version-in-a-result).
- **The Excel add-in uses an older preview.** It pins its version. Moving it is a separate PR.

## Known limitations of the preview

- **Prerelease API.** Names and behaviour can change between previews. The API review before 1.0 is [#45](https://github.com/m-vaysman/recdiffy.net/issues/45), and the compatibility promise is [#74](https://github.com/m-vaysman/recdiffy.net/issues/74).
- **Linux-only CI.** The library tests run only on Ubuntu ([#53](https://github.com/m-vaysman/recdiffy.net/issues/53)).
- **AOT not published.** The AOT host is built but never published or run ([#51](https://github.com/m-vaysman/recdiffy.net/issues/51)).
- **No package consumer test.** No workflow restores the packed packages in a clean project ([#50](https://github.com/m-vaysman/recdiffy.net/issues/50)).
- **No XML API documentation** in the packages. No project sets `GenerateDocumentationFile`.
- **Package metadata gaps,** such as links in the packed README, are tracked in [#46](https://github.com/m-vaysman/recdiffy.net/issues/46).
- **Attributions are built into the engine,** and an unknown attribution name compiles ([#235](https://github.com/m-vaysman/recdiffy.net/issues/235)). `AttributedTo` can name roots that don't explain the difference ([#254](https://github.com/m-vaysman/recdiffy.net/issues/254)).
- **The same engine version** for every preview of one release. See [The engine version in a result](#the-engine-version-in-a-result).
- **In memory.** Both sides are read fully into memory before matching.
- **Profile gaps** that compile but misbehave are listed in [PROFILES.md](PROFILES.md#known-gaps).
- **Deferred:** installing the Excel add-in ([#198](https://github.com/m-vaysman/recdiffy.net/issues/198)), profile authoring tools ([#143](https://github.com/m-vaysman/recdiffy.net/issues/143), [#144](https://github.com/m-vaysman/recdiffy.net/issues/144), [#145](https://github.com/m-vaysman/recdiffy.net/issues/145)) and typed columns from a sample file ([#122](https://github.com/m-vaysman/recdiffy.net/issues/122)).
