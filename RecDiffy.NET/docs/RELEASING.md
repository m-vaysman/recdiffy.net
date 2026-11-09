# Releasing RecDiffy.NET

Packages published by the release workflow are `RecDiffy`, `RecDiffy.RulePacks.Trading`, `RecDiffy.Excel` and `RecDiffy.IO`. Tests, benchmarks, samples and the AOT host are not packed.

Every package is a prerelease for now. A pack whose version has no prerelease label, such as `1.2.0`, fails the build, and the release workflow rejects a tag without one.

## Release to nuget.org

1. Merge the change to `main`.
2. Tag that commit `vX.Y.Z-label`, for example `v1.2.0-preview.1`.
3. Push the tag: `git push origin v1.2.0-preview.1`.

The workflow builds, runs the tests, packs all four projects as that version, pushes the packages to nuget.org, and opens a GitHub Release marked as a prerelease, with the packages attached.

The workflow publishes through nuget.org Trusted Publishing, so no API key is stored. GitHub gives the release job a signed token, `NuGet/login` exchanges it for a nuget.org key that lasts one hour, and the push uses that key. The nuget.org policy `RecDiffy` trusts only `release.yml` in `m-vaysman/recdiffy.net`, for the packages `RecDiffy` and `RecDiffy.*`, owned by `cobblerhill`.

Add a repository secret named `NUGET_USER` before the first tag. It holds the nuget.org username of the account that created the policy, not an API key.

## GitHub Packages

A push to `main` packs `1.1.0-preview.<run number>` and publishes those packages to GitHub Packages, not nuget.org.
