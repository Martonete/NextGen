# Client Build Guide

## Architecture: EXE + PCK + Data

| Component | Size | Changes? | Description |
|-----------|------|----------|-------------|
| `TierrasSagradasAO.exe` | ~82MB | Rarely | Godot engine + .NET runtime |
| `TierrasSagradasAO.pck` | ~180KB | Every code change | Compiled scenes + scripts |
| `data_TierrasSagradasAO_*/` | ~150MB | Rarely | .NET runtime DLLs |
| `Data/` | ~217MB | Only when assets change | Game assets (sprites, sounds, maps) |

`Data/` is loaded from the filesystem at runtime (not packed). All loaders use `System.IO.File`.

## Export Steps (Windows)

Prerequisites: Godot 4.4 .NET + .NET 8.0 SDK + Export Templates (`Editor` → `Manage Export Templates` → `Download and Install`).

Before exporting, set server IP in `client/Scripts/Main.cs`:
```csharp
private const string ServerHost = "123.45.67.89"; // Your server's public IP
```

```powershell
cd client
dotnet build
mkdir build -Force
& "C:\path\to\Godot_v4.4-stable_mono_win64.exe" --headless --export-release "Windows Desktop" build\TierrasSagradasAO.exe
Copy-Item -Recurse Data build\
Compress-Archive -Path build\* -DestinationPath ..\TierrasSagradasAO.zip
```

Or with Make: `make client-dist`

## Export Settings

In Godot → Project → Export → Windows Desktop:
- **Resources tab**: Export Mode = `Export selected scenes`, select only `Main.tscn`, exclude `Data/*, test/*`
- **Options tab**: `Embed PCK` = OFF

## Updating (after code changes)

| What changed | What to send | Size |
|---|---|---|
| C# code, scenes, UI | `.pck` only | ~180KB |
| Game assets | Specific `Data/` files | Variable |
| Godot version upgrade | Full re-export | ~230MB |
| First install | Everything (zip) | ~450MB |

## Patch PCK

Drop `patch.pck` or `patch_001.pck` next to the `.exe` — loaded automatically on startup.
