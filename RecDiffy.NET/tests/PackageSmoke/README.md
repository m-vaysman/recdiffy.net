# Package smoke tests

These checks use the packed NuGet packages the way a consumer gets them. They aren't in `RecDiffy.slnx`, so `dotnet test` doesn't run them. CI runs them in [packages.yml](../../../.github/workflows/packages.yml).

## What is here

- **`Consumer/`** compiles every file in [`src/RecDiffy.Samples`](../../src/RecDiffy.Samples) against the packages, not the projects. It references `RecDiffy.RulePacks.Trading`, `RecDiffy.IO` and `RecDiffy.Excel`. `RecDiffy` comes in as their dependency. There is no `ProjectReference`.
- **`Aot/`** compiles the samples that use only `RecDiffy` and `RecDiffy.RulePacks.Trading`. Those are the two packages that claim `IsAotCompatible` and `IsTrimmable`.
- **`verify-sourcelink.sh`** reads the `.snupkg` files a pack produced and proves each PDB maps its source to the built commit, with no absolute paths ([#49](https://github.com/m-vaysman/recdiffy.net/issues/49)). It needs the `sourcelink` tool, not a package feed.
- **`nuget.config`** takes `RecDiffy` and `RecDiffy.*` only from `feed/`. Everything else comes from nuget.org.
- **`Directory.Build.props` and `Directory.Build.targets`** stop the build from importing the ones in `RecDiffy.NET`, so no analyzer or packaging setting leaks in. They also restore into `packages/` here, not into your global NuGet cache.

## Run them

From the repository root, pack into `feed/`, then run either script with the same version:

```
dotnet pack RecDiffy.NET/RecDiffy.slnx -c Release -o RecDiffy.NET/tests/PackageSmoke/feed -p:Version=0.0.0-local.1
bash RecDiffy.NET/tests/PackageSmoke/consume.sh 0.0.0-local.1
bash RecDiffy.NET/tests/PackageSmoke/publish-aot.sh 0.0.0-local.1
```

Each script deletes `packages/` first, so a new pack with the same version is picked up.

`verify-sourcelink.sh` reads a folder of packed files and takes the version and, optionally, the commit to expect:

```
dotnet tool install --global sourcelink
dotnet pack RecDiffy.NET/RecDiffy.slnx -c Release -o artifacts/packages -p:Version=0.0.0-local.1
bash RecDiffy.NET/tests/PackageSmoke/verify-sourcelink.sh artifacts/packages 0.0.0-local.1
```

`publish-aot.sh` needs the [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites). On Linux that is clang. On Windows it is the Visual Studio C++ tools, with `vswhere.exe` on `PATH`.

## What the scripts check

`consume.sh`:

- Each of the four packages restores from `feed/`, not from nuget.org or a cache.
- Each package holds `README.md` and its icon, and declares both in its nuspec.
- Each package has `lib/net8.0` and `lib/net10.0`.
- Trading, IO and Excel each depend on `RecDiffy` at the same version, for both frameworks.
- Every sample gets its expected result on `net8.0` and on `net10.0`. A dependency missing from a nuspec fails here, because the sample that needs it can't load the assembly. On `net8.0` that includes `System.Text.Json`.
- `EngineVersion` equals the version that was packed, on both frameworks ([#295](https://github.com/m-vaysman/recdiffy.net/issues/295)). The `EngineVersionReport` sample prints it, so this also proves the version survives the package.

`publish-aot.sh`:

- Publishes `Aot/` with Native AOT, and trimmed without AOT, for `net8.0` and `net10.0`.
- Treats every warning as an error, including trimming and AOT warnings.
- Runs each published app. Every sample in it must get its expected result.

`verify-sourcelink.sh`:

- Each of the four `.snupkg` files holds a PDB for `net8.0` and one for `net10.0`.
- Each PDB carries Source Link JSON, and every URL in it starts with the `raw.githubusercontent.com` URL for the commit being built, so a debugger lands on the matching source revision.
- Each PDB lists its source documents, and none is an absolute path, so the PDB is deterministic.
