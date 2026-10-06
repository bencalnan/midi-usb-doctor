# MIDI USB Doctor

## Local development

This repository uses a local .NET 10 SDK so administrator access is not required.

Install or restore the pinned SDK:

```bash
./scripts/setup-dotnet.sh
```

Run .NET commands through the repository wrapper:

```bash
./scripts/dotnet --info
./scripts/dotnet build
./scripts/dotnet test
```

The SDK is installed in `.dotnet/`, which is excluded from Git.
