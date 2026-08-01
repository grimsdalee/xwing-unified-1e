# Repository Maintenance Commands

## Verify obsolete models

```powershell
dotnet run -- verify-obsolete-models `
    "C:\Users\Evan\Documents\GitHub\xwing-unified-1e"
```

This is report-only. It writes reports under:

```text
_unifiedtoolkit_reports/model-cleanup
```

## Quarantine verified-unused models

```powershell
dotnet run -- quarantine-obsolete-models `
    "C:\Users\Evan\Documents\GitHub\xwing-unified-1e"
```

Only entries with an existing replacement and no active repository references are moved. The original folder structure is preserved under:

```text
_unifiedtoolkit_quarantine/obsolete-models
```

## Restore quarantined models

```powershell
dotnet run -- restore-quarantined-models `
    "C:\Users\Evan\Documents\GitHub\xwing-unified-1e"
```

## Permanently purge quarantined models

```powershell
dotnet run -- purge-quarantined-models `
    "C:\Users\Evan\Documents\GitHub\xwing-unified-1e" `
    --confirm-purge
```

Purge is the only permanently destructive command and requires the explicit confirmation option.
