# Releasing RecDiffy.NET

Packages published by the release workflow are `RecDiffy` and `RecDiffy.RulePacks.Trading`. Tests, benchmarks, and the AOT host are not packed.

## Stable release

1. Merge the change to `main`.
2. Tag that commit `vX.Y.Z`, for example `v1.2.3`.
3. Push the tag: `git push origin vX.Y.Z`.

The tag must be `v` followed by three numbers. The workflow rejects any other shape. It builds, runs the tests, packs both projects as that version, pushes the packages to nuget.org, and opens a GitHub Release with the packages attached.

Add a repository secret named `NUGET_API_KEY` before the first tag. The workflow reads it and does not print it.

## Prerelease

A push to `main` packs `1.1.0-preview.<run number>` and publishes those packages to GitHub Packages, not nuget.org.
