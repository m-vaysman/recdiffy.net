# Releasing RecDiffy.NET

Packages published by the release workflow are `RecDiffy`, `RecDiffy.RulePacks.Trading`, `RecDiffy.Excel` and `RecDiffy.IO`. Tests, benchmarks, samples and the AOT host are not packed.

Every package is a prerelease for now. A pack whose version has no prerelease label, such as `1.2.0`, fails the build, and the release workflow rejects a tag without one.

## Release to nuget.org

1. Merge the change to `main`.
2. Tag that commit `vX.Y.Z-label`, for example `v1.2.0-preview.1`.
3. Push the tag: `git push origin v1.2.0-preview.1`.

The workflow builds, runs the tests, packs all four projects as that version, pushes the packages to nuget.org, and opens a GitHub Release marked as a prerelease, with the packages attached.

Add a repository secret named `NUGET_API_KEY` before the first tag. The workflow reads it and does not print it.

## GitHub Packages

A push to `main` packs `1.1.0-preview.<run number>` and publishes those packages to GitHub Packages, not nuget.org.
