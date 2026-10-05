# Config XML Encoding Hardening 2026-03-30

## Problem

The main project could fail during startup with:

- `System.InvalidOperationException`
- inner `XmlException`: `Nessun indicatore per l'ordine dei byte Unicode. Impossibile passare a Unicode.`

Observed call path:

- [AsyncConfigManagerXml.cs](C:/Users/chouikha/QtisVisionPanel/Cls_Config/AsyncConfigManagerXml.cs)

## Root cause

The active runtime file:

- `C:\QtisVision\cfg\Config.xml`

contained an XML declaration with:

- `encoding="utf-16"`

but the file content on disk was effectively being handled as UTF-8-style text without a matching Unicode BOM.

This created a classic declaration/content mismatch:

- XML declaration said `utf-16`
- file bytes did not match a proper UTF-16 stream

As a result, `XmlSerializer.Deserialize(stream)` could fail before any real config parsing happened.

## Fix applied

### 1. Loader hardening

In [AsyncConfigManagerXml.cs](C:/Users/chouikha/QtisVisionPanel/Cls_Config/AsyncConfigManagerXml.cs):

- direct stream deserialization was replaced with a safer text-loading path
- the loader now:
  - reads raw bytes
  - tries decoding as UTF-8 first
  - falls back to UTF-16 LE / BE when needed
  - normalizes the XML declaration to `utf-8`
  - deserializes from normalized text

This makes startup more resilient against local legacy files coming from older deployments or manual edits.

### 2. Save-path hardening

Still in [AsyncConfigManagerXml.cs](C:/Users/chouikha/QtisVisionPanel/Cls_Config/AsyncConfigManagerXml.cs):

- config writes now use `StreamWriter(..., new UTF8Encoding(false))`
- serialized XML declarations are normalized to `utf-8`

This prevents the same mismatch from being regenerated during future saves.

### 3. Runtime file correction

The active local runtime file:

- `C:\QtisVision\cfg\Config.xml`

was corrected so the XML declaration now matches the actual file-writing strategy:

- `encoding="utf-8"`

## Verification

Verified with:

- `MSBuild QtisVisionPanel.csproj /p:Configuration=Debug /p:Platform=x64`

Result:

- build succeeded
- no new blocking issues introduced

## Operational note

If a colleague or another machine reintroduces a `Config.xml` with an inconsistent encoding declaration, the main project should now tolerate it better.

Still, the preferred contract for the local runtime file remains:

- XML text saved as UTF-8
- XML declaration set to `utf-8`
