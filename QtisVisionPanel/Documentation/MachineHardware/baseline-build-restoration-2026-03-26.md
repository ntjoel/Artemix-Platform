# Baseline Build Restoration 2026-03-26

## Goal

Restore a trustworthy local build baseline for `QtisVisionPanel` before completing the native Inspector fusion.

## Actual issue found

The refreshed repository was not blocked by broken XAML or by the Inspector fusion itself.

The real blockers were build-toolchain related:

1. `dotnet build` was not the right baseline validator for this legacy WPF project shape.
2. the project still expected an old WebView2 `.targets` import that is not compatible with the currently installed build chain
3. the post-build step used `mklink /D`, which failed without elevated privileges

## Conservative corrections applied

### 1. Build toolchain used

The project now validates correctly with Visual Studio MSBuild:

- `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe`

Validation command:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' `
  'C:\Users\chouikha\QtisVisionPanel\QtisVisionPanel.csproj' `
  /t:Build /p:Configuration=Debug /p:Platform=x64 /nologo /m
```

### 2. WebView2 package targets import

The legacy import of:

- `packages\Microsoft.Web.WebView2.1.0.3595.46\build\Microsoft.Web.WebView2.targets`

was removed from:

- [QtisVisionPanel.csproj](C:/Users/chouikha/QtisVisionPanel/QtisVisionPanel.csproj)

Reason:

- the package targets file was introducing metadata that broke the local MSBuild evaluation
- the project already references the required WebView2 assemblies directly
- the old `DataInspector` WebView2 wrapper is no longer the active implementation path

This was a conservative build fix, not a product-level feature change.

### 3. Language version

The project had explicit `LangVersion` values that were not helping local restoration.

The x64 configurations now use:

- `LangVersion = default`

This lets the Visual Studio Roslyn compiler handle the codebase consistently.

### 4. Post-build dependency link

The project previously ran:

```text
mklink /D VisionProDependencies "$(VPRO_ROOT)\bin"
```

This required permissions that were not available in the local environment.

It now runs:

```text
mklink /J VisionProDependencies "$(VPRO_ROOT)\bin"
```

This preserves the expected `VisionProDependencies` probing folder while allowing the build to complete without elevation.

## Result

The main project now builds successfully in:

- `Debug | x64`

Current state:

- `0` build errors
- warnings remain, but they are non-blocking and pre-existing quality items

## Runtime smoke check

After the build restoration, the generated executable:

- [QtisVisionPanel.exe](C:/Users/chouikha/QtisVisionPanel/bin/x64/Debug/QtisVisionPanel.exe)

was launched successfully and stayed alive during a short smoke run.

This is not a full functional validation, but it confirms:

- the baseline now starts
- the restored build is not producing an immediately crashing executable

## Why this matters for fusion

This restoration changes the status of the native Inspector work:

- before: prepared integration on a shaky baseline
- now: compiled integration on a restored local baseline

That makes the next step meaningful:

1. open the main HMI
2. navigate to `DataInspector`
3. validate the native view with the local seeded `quatisv` database and `C:\QtisVision\Pieces\`

## Remaining quality notes

The project still has compile warnings worth cleaning later, but they do not block the Inspector fusion:

- hidden inherited members in `RecipeManagerViewModel`
- obsolete Advantech API calls in `AdvantechDeviceManager`
- several unused fields/events in older runtime areas

Those should be treated as technical debt, not as fusion blockers.
