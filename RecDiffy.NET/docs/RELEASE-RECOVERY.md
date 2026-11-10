# Recovering from a bad release

[RELEASING.md](RELEASING.md) covers a release that goes well. This page covers one that doesn't:

- [a publication that stopped partway](#interrupted-publication);
- [the core or trading package didn't publish](#the-core-or-trading-package-didnt-publish);
- [a bad package](#a-bad-package);
- [a version mismatch](#a-version-mismatch).

It ends with [how to tell users which versions are affected](#tell-users-which-versions-are-affected).

The examples use `1.2.0-preview.4` as the version in trouble and `1.2.0-preview.5` as the next one.

## Rules that shape every recovery

- **A version on nuget.org is permanent.** nuget.org doesn't delete packages, except in cases such as copyright infringement or harmful content[^deleting]. A push of a version that already exists is rejected with 409 Conflict[^publish-api]. So a corrected package always ships under a new version. The bad version is deprecated, and also unlisted if new users must not pick it up.
- **The four packages ship together, at one version.** `RecDiffy.RulePacks.Trading`, `RecDiffy.Excel` and `RecDiffy.IO` each depend on `RecDiffy` at a minimum version (`>= 1.2.0-preview.4`), not at exactly that version. So NuGet restores a mixed set without complaint. A mixed set can still fail at run time. The trading pack is built against internal types of `RecDiffy` (through `InternalsVisibleTo`), so it can break with any other version of the core. A fix ships all four packages at the new version, even if only one of them was bad.
- **A re-run publishes only what is missing.** The push uses `--skip-duplicate`, which turns a 409 into a warning and carries on with the other packages[^push]. So re-running a release is safe. The flip side is that a run whose packages were all on nuget.org already passes, and publishes nothing.
- **A re-run builds the tagged commit again.** It uses the same commit and ref as the original run, so it uses that commit's code and that commit's `release.yml`. A re-run is possible for 30 days after the first run[^rerun]. If the cause is in the code or in the workflow, a re-run fails the same way, and the fix needs a new commit and a new tag.
- **The version is free until a package reaches nuget.org.** If the run stopped before "Push to nuget.org", you can delete the tag and tag again with the same version.

## Find out what was published

Find the release run (`gh run list --workflow release.yml`) and the step that failed.

| Step that failed | On nuget.org | GitHub Release |
|---|---|---|
| Any step up to and including "Pack" | Nothing | None |
| "Log in to nuget.org" | Nothing | None |
| "Push to nuget.org" | Any of the four packages, or none | None |
| "Create GitHub Release" | All four | None, or one with files missing |

Then check nuget.org. The version list for a package includes unlisted versions[^flat-container], so this shows every version that reached nuget.org:

```bash
for id in recdiffy recdiffy.rulepacks.trading recdiffy.excel recdiffy.io; do
  echo "$id: $(curl -s "https://api.nuget.org/v3-flatcontainer/$id/index.json")"
done
```

A pushed package is validated and indexed before it shows up there. That usually takes less than 15 minutes, and until then nuget.org lists it under "Unlisted Packages" in your account. If a package isn't published after an hour, use "Contact support" on its package page. If validation fails, nuget.org shows the error on the package page and sends an email[^publish].

To see which commit a published package was built from, read the `commit` attribute of `<repository>` in its nuspec:

```bash
curl -s https://api.nuget.org/v3-flatcontainer/recdiffy/1.2.0-preview.4/recdiffy.nuspec | grep '<repository'
```

## Interrupted publication

### The run stopped before "Push to nuget.org"

Nothing was published, so the version is still free.

- **A transient failure,** such as a flaky test or a runner or network problem: re-run the failed job, from the run page or with `gh run rerun <run-id> --failed`. This happened to `v1.2.0-preview.1`. The first attempt failed on a flaky allocation test ([#104](https://github.com/m-vaysman/recdiffy.net/issues/104)), and the re-run published it.
- **A defect in the code or in `release.yml`:** a re-run fails the same way. Delete the tag, fix the defect through `dev` and a release merge into `main` as usual, then tag the new `main` commit with the same version.

  ```bash
  git push origin :refs/tags/v1.2.0-preview.4
  git tag -d v1.2.0-preview.4
  ```

- **"Log in to nuget.org" failed:** see [Trusted Publishing fails](#trusted-publishing-fails).

To stop a run that is still going, for example one started from the wrong tag, use `gh run cancel <run-id>` before it reaches "Push to nuget.org".

### The push stopped partway

Some packages are on nuget.org at the new version and some aren't. Re-run the failed job. It builds the same commit at the same version, `--skip-duplicate` skips the packages that are already published, and the rest are pushed. Then the job creates the GitHub Release. Check nuget.org again afterwards.

Don't move the tag and don't change the version. The packages that did publish are permanent, and the missing ones must match them.

After 30 days a run can't be re-run[^rerun]. Delete the tag and push it again at the same commit instead. That starts a new run of the same commit and version.

If the missing packages still won't publish, see [The core or trading package didn't publish](#the-core-or-trading-package-didnt-publish).

### Published, but the GitHub Release is missing or incomplete

- **No release:** re-run the failed job. The push skips all four packages, and the job creates the release.
- **A release with files missing,** because `gh release create` failed while uploading: delete the release, keep the tag, and re-run the failed job. `gh release delete` keeps the tag unless you pass `--cleanup-tag`.

  ```bash
  gh release delete v1.2.0-preview.4 --yes
  gh run rerun <run-id> --failed
  ```

### Trusted Publishing fails

"Log in to nuget.org" exchanges GitHub's signed token for a nuget.org API key. If the step fails, nothing is published. Check these, in this order:

1. **The secret.** `NUGET_USER` exists and holds the nuget.org profile name of the account that created the policy. That's the profile name, not the email address[^trusted].
2. **The policy is active.** On nuget.org, select your username, then "Trusted Publishing"[^trusted]. The policy `RecDiffy` must name the owner `m-vaysman`, the repository `recdiffy.net` and the workflow file `release.yml`. The workflow is matched by its file name only. A policy becomes inactive if the organization that owns it is locked or deleted, or if its creator is removed from that organization[^trusted]. This policy is owned by `cobblerhill`.
3. **A new policy.** A new or re-created policy can start out temporarily active for 7 days, and it becomes permanently active at its first successful publish. If nothing is published within the 7 days, it becomes inactive. You can restart the 7 days from the same page[^trusted].
4. **The workflow.** `release.yml` still has `permissions: id-token: write`, and the file is still named `release.yml`. If the file is renamed, edit the policy to match before you tag.

A fix on nuget.org or to the secret needs only a re-run. A fix to `release.yml` needs a new commit, so delete the tag and tag the new commit. Nothing was published, so the version is still free.

The key from the exchange lasts one hour, and each GitHub token can be exchanged for one key only[^trusted]. The login step runs just before the push, and a re-run logs in again.

## The core or trading package didn't publish

The other packages need the core, and the trading pack needs the core at its own version. So a missing core or trading package hurts users more than a missing `RecDiffy.Excel` or `RecDiffy.IO`.

| Missing at the new version | What users see |
|---|---|
| `RecDiffy` | `RecDiffy.RulePacks.Trading`, `RecDiffy.Excel` and `RecDiffy.IO` at the new version need `RecDiffy` at that version or later, and there is none. Restore fails for anyone who installs them. |
| `RecDiffy.RulePacks.Trading` | Trading users can't move to the new version. If they move `RecDiffy` anyway, the old trading pack runs against a core it wasn't built with, and it can fail at run time with `MissingMethodException` or `TypeLoadException`. |
| `RecDiffy.Excel` or `RecDiffy.IO` | Their users can't move to the new version. |

Recover in this order:

1. **Re-run the failed job,** as in [The push stopped partway](#the-push-stopped-partway). The missing package goes out at the same version, and the set is whole again. This is the normal fix.
2. **Don't announce the version** until the set is complete. Don't point the READMEs at it either.
3. **If the package can't publish at that version,** don't publish it on its own under another version. That happens, for example, when nuget.org rejects it in validation and support can't clear it. The docs don't say whether a version that failed validation can be pushed again[^publish], so treat the whole version as bad. Release all four packages at the next version. Then deprecate and unlist the packages that did publish at the broken version, as in [A bad package](#a-bad-package).

## A bad package

The package is on nuget.org, and it's wrong. It might have a regression, give wrong results, miss a file or have a security flaw. It can't be replaced. Issue a corrected version:

1. **Fix it** through `dev` and a release merge into `main`, as usual. For a security flaw, read [Security flaws](#security-flaws) first.
2. **Tag the next prerelease,** `v1.2.0-preview.5`, and push the tag, as in [RELEASING.md](RELEASING.md). All four packages go out at the new version.
3. **Check** that all four reached nuget.org, as in [Find out what was published](#find-out-what-was-published).
4. **[Deprecate](#deprecate)** the bad version of all four packages, with the fixed version as the alternate.
5. **[Unlist](#unlist)** the bad version too, if new users must not pick it up.
6. **[Tell users](#tell-users-which-versions-are-affected).**

Keep the `preview.N` form, so the fix sorts after the bad version. NuGet compares the dot-separated parts of a prerelease label, and compares the numeric parts as numbers. So `preview.10` sorts after `preview.9`, but `preview10` sorts before `preview9`.

### Deprecate

On nuget.org, go to "Manage packages", choose the package, open "Deprecation" and select the bad version or versions[^deprecate]. Then set:

- **Reason:** critical bugs, or "Other" for anything else.
- **Alternate:** the same package at the fixed version.
- **Message:** one line that names the issue and the fixed version, for example "Wrong totals for grouped rows (#123). Use 1.2.0-preview.5."

Only nuget.org shows the message. Visual Studio and `dotnet list package --deprecated` show the reason and the alternate[^deprecate]. Deprecate all four packages. Deprecating a version doesn't unlist it[^deprecate].

### Unlist

Unlisting hides a version from search on nuget.org and in Visual Studio. It still restores when a project names its exact version. A floating version such as `1.2.0-*` can still pick it if it's the newest match[^deleting]. Once the fix is published, the fix is the newest match.

| The bad version | Deprecate | Unlist |
|---|---|---|
| Has a bug with a workaround, or is just superseded | Yes | No |
| Gives wrong reconciliation results without an error, or has a security flaw | Yes | Yes |
| Is part of an incomplete set, or was built from the wrong commit | Yes | Yes |

To unlist on nuget.org[^deleting]:

1. Select your account name, then "Manage packages".
2. Under "Published packages", select the package's "Manage package" icon.
3. Expand "Listing" and select the version.
4. Clear "List in search results" and select "Save".

To relist, select the box again. The docs describe relisting only through the API[^publish-api].

On nuget.org, `dotnet nuget delete` unlists a version; it doesn't delete it[^nuget-delete]. Releases publish through Trusted Publishing, so no API key is stored. To unlist from the command line, create a short-lived key on nuget.org. Keys can be limited to the unlist operation and to a glob such as `RecDiffy*`, and given an expiry[^publish]. Delete the key when you're done.

```bash
for id in RecDiffy RecDiffy.RulePacks.Trading RecDiffy.Excel RecDiffy.IO; do
  dotnet nuget delete "$id" 1.2.0-preview.4 --source https://api.nuget.org/v3/index.json --api-key "$NUGET_UNLIST_KEY" --non-interactive
done
```

For four packages, the web page is usually quicker.

### Security flaws

Keep the details private until the fix is out. Open the repository's advisories at `https://github.com/m-vaysman/recdiffy.net/security/advisories` and create a new draft security advisory[^gh-create]:

- **Private until published.** A draft is visible to the repository's maintainers and to the collaborators you add. You can prepare the fix in the draft's temporary private fork, which GitHub deletes when you publish[^gh-about][^gh-publish].
- **Affected products.** Choose the ecosystem NuGet and add one entry for each package. Enter the affected versions as a range, such as `<= 1.2.0-preview.4`, and the patched version, `1.2.0-preview.5`. Use `< n` only when `n` itself is not affected[^gh-write].
- **Publish** when the fixed version is on nuget.org. Fill in the patched version before you publish. Otherwise Dependabot alerts users without offering a safe version. You can request a CVE from the advisory[^gh-about][^gh-publish].

GitHub reviews a published advisory and adds it to the GitHub Advisory Database. That can take up to 72 hours[^gh-about]. nuget.org's vulnerability data comes from that database, so restore (NuGet Audit) warns about the affected versions, and `dotnet list package --vulnerable` lists them[^audit].

Then deprecate and unlist the affected versions as above.

## A version mismatch

### Users have mixed versions

A project that mixes versions of the four packages can fail at run time with `MissingMethodException`, `TypeLoadException` or `MissingFieldException`. The likeliest source is `RecDiffy.RulePacks.Trading`. NuGet allows the mix, because each package needs `RecDiffy` at a minimum version only. The README already says to use the same version of each package.

On our side, make sure every version has all four packages, as in [Find out what was published](#find-out-what-was-published). A gap forces users to mix. Tell affected users to pin all four packages to one version.

### The tag names the wrong version

The workflow rejects a tag that has no prerelease label, and a pack fails on a version without one. So a stable version can't go out by accident.

- **The run hasn't reached "Push to nuget.org":** cancel the run, delete the tag and tag again.
- **The packages are published:** the version is permanent.
  - **It skips a number,** such as `preview.6` after `preview.4`: leave it and carry on from it. Nothing is broken.
  - **It sorts below an earlier release,** such as `1.1.0-preview.1` after `1.2.0-preview.4`, or `preview10` after `preview9`: `dotnet add package --prerelease` and floating versions keep taking the earlier release. Release the intended code at a version above both, then deprecate and unlist the misnumbered one.

### The tag is on the wrong commit

The workflow publishes whatever commit the tag points to. It doesn't check that the commit is on `main`. Compare the nuspec's commit, as in [Find out what was published](#find-out-what-was-published), with `git rev-parse origin/main`.

- **The run hasn't reached "Push to nuget.org":** cancel the run, delete the tag and tag the right commit.
- **The packages are published:** treat them as [a bad package](#a-bad-package). Release the right commit as the next version, then deprecate and unlist the wrong one.

Never move a tag after its packages are published. The new run builds the new commit, gets a 409 for every package, skips them all and passes. nuget.org keeps the old packages, while the tag and any new GitHub Release point at the new commit.

### GitHub Packages builds aren't releases

Each push to `main` also publishes `1.1.0-preview.<run number>` to GitHub Packages. Those versions don't match the nuget.org releases, and they sort below the `1.2.0` previews. Don't use them in place of a missing nuget.org package, and don't point users at them.

## Tell users which versions are affected

Do these once the fixed version is on nuget.org.

1. **An issue.** Open one, or use the bug's own issue. Name the affected packages and versions, the symptom, the fixed version, and what to do: move all four packages to the fixed version. Pin the issue while it matters, with `gh issue pin <number>`.
2. **nuget.org.** [Deprecate](#deprecate) every affected version of all four packages, with the fix as the alternate. This is how users who pinned a version find out. Visual Studio shows a warning on its Installed tab, and `dotnet list package --deprecated` lists the version[^deprecate].
3. **GitHub Releases.** Add a first line to each affected release's notes, for example "Affected by #123: wrong totals for grouped rows. Use v1.2.0-preview.5." Use the web page or `gh release edit`. In the fixed release's notes, say which versions it replaces. The workflow generates the notes, so edit them after the run.
4. **The READMEs.** The quick starts in `README.md` and `RecDiffy.NET/README.md` pin a version ("It was checked against …"). If they name an affected version, point them at the fix in a PR into `dev`. `RecDiffy.NET/README.md` is also the package README, so the affected version's package page keeps the old text. Its deprecation notice is what corrects it.
5. **A security flaw:** publish the repository security advisory, as in [Security flaws](#security-flaws).

## Sources

Checked on 2026-11-10.

[^deleting]: nuget.org, [Deleting packages](https://learn.microsoft.com/en-us/nuget/nuget-org/policies/deleting-packages): no permanent deletion, what unlisting hides, and the unlisting steps.
[^publish-api]: NuGet API, [Push and delete](https://learn.microsoft.com/en-us/nuget/api/package-publish-resource): 409 for an existing version, delete as unlist on nuget.org, and the relist request.
[^push]: .NET CLI, [dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push): `--skip-duplicate`.
[^rerun]: GitHub Actions, [Re-running workflows and jobs](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs): the 30-day limit, and the same commit and ref as the original run.
[^flat-container]: NuGet API, [Package content](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource): the version list includes unlisted versions.
[^publish]: nuget.org, [Publishing packages](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package): validation and indexing, validation failures, and API key scopes.
[^trusted]: nuget.org, [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing): policies, temporary activation, the one-hour key, and the `user` value.
[^deprecate]: nuget.org, [Deprecating packages](https://learn.microsoft.com/en-us/nuget/nuget-org/deprecate-packages): reasons, the alternate package, the message, and where deprecation shows.
[^nuget-delete]: .NET CLI, [dotnet nuget delete](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-delete): unlists on nuget.org.
[^audit]: NuGet, [Auditing package dependencies](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages): nuget.org's vulnerability data comes from the GitHub Advisory Database.
[^gh-create]: GitHub, [Creating a repository security advisory](https://docs.github.com/en/code-security/security-advisories/working-with-repository-security-advisories/creating-a-repository-security-advisory).
[^gh-publish]: GitHub, [Publishing a repository security advisory](https://docs.github.com/en/code-security/security-advisories/working-with-repository-security-advisories/publishing-a-repository-security-advisory): the private fork is deleted on publish, and the CVE request.
[^gh-about]: GitHub, [About repository security advisories](https://docs.github.com/en/code-security/concepts/vulnerability-reporting-and-management/repository-security-advisories): drafts, review, the Advisory Database, and the patched version.
[^gh-write]: GitHub, [Best practices for writing repository security advisories](https://docs.github.com/en/code-security/tutorials/fix-reported-vulnerabilities/write-security-advisories): version range syntax.
