# Contributors

This document is for anyone modifying or rebuilding the Wheelbarrow mod. The consumer-facing install and usage instructions live in `README.md`.

## Repository Layout

- `1A-Wheelbarrow/` - deployable mod folder.
- `1A-Wheelbarrow/Config/` - XML and localization appends.
- `1A-Wheelbarrow/Resources/wheelbarrow.unity3d` - built Unity asset bundle.
- `1A-Wheelbarrow/Wheelbarrow.dll` - built gameplay/command DLL.
- `src/Wheelbarrow/` - C# source for Harmony patches, commands, visual repair, and push behavior.
- `tools/import_artist_wheelbarrow.py` - imports the artist wheelbarrow FBX, orients/scales/rigs it, and exports the project FBX.
- `UnityProject/` - Unity project used to build the wheelbarrow prefab and asset bundle.

## Tooling

Current local tooling targets:

- 7 Days To Die V3.0.
- Unity `2022.3.62f2`.
- Blender `5.1` for regenerating the model.
- .NET SDK capable of building `net48`.
- Easy Anti-Cheat disabled for in-game testing.

Unity executable used during development:

```powershell
C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe
```

Open the `UnityProject` folder directly in Unity Hub. Do not try to import the repository root as a Unity project.

## Build Workflow

Run commands from the repository root.

Import/rig the artist model into the project FBX (set `SOURCE_FBX` in the script to the artist's `wheelbarrow_LOD0.fbx`):

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python tools\import_artist_wheelbarrow.py
```

Build the Unity asset bundle:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe' -batchmode -quit -projectPath UnityProject -executeMethod BuildWheelbarrowBundle.BuildAll -logFile UnityProject\build-wheelbarrow.log
```

Validate the built bundle:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe' -batchmode -quit -projectPath UnityProject -executeMethod BuildWheelbarrowBundle.ValidateBuiltBundle -logFile UnityProject\validate-wheelbarrow.log
```

Build the C# DLL:

```powershell
dotnet build src\Wheelbarrow\Wheelbarrow.csproj -v:minimal
```

The C# project deploys `Wheelbarrow.dll` into `1A-Wheelbarrow`. If a local 7D2D `Mods` folder exists at the default Steam path, it also installs the DLL there.

When XML, localization, icons, or the asset bundle change, copy the full `1A-Wheelbarrow` folder to your game `Mods` folder or copy the changed files explicitly.

## In-Game Test Commands

```text
wb
wb 5
wb push
wb push 1.25 0.2 15
wb drop
wb cleanup
wb debug
```

Useful notes:

- `wb` spawns a test wheelbarrow near the player.
- `wb cleanup` removes active and unloaded wheelbarrow records.
- `wb debug` logs renderer counts, missing materials, bounds, and transform paths.
- Restart 7D2D after DLL or localization changes.

## Implementation Notes

- The wheelbarrow entity is `vehicleWheelbarrow`.
- The placeable item is `vehicleWheelbarrowPlaceable`.
- The runtime uses Harmony patches from `WheelbarrowModApi`.
- `WheelbarrowPushController.cs` owns the walk-behind push state.
- `WheelbarrowInputLockPatches.cs` suppresses jump/attack while pushing.
- Toolbelt slot changes intentionally release the cart before vanilla inventory switching continues.
- `WheelbarrowInteractPatch.cs` replaces the normal ride/drive activation with push behavior and custom prompt text.
- `WheelbarrowVisuals.cs` repairs/logs renderer state for spawned wheelbarrows.

## Asset Bundle Notes

The Unity prefab structure matters because 7D2D vehicle code resolves transforms by name:

```text
WheelbarrowPrefab
  GameObject
    Mesh
      M
        Forks
          Wheel0
        Storage
      Wheel1
  Physics
    Wheel0
    Wheel1
```

`BuildWheelbarrowBundle.ValidateBuiltBundle` checks these paths, physics placement, renderer count, and visual bounds. Run it after any model, prefab, or Unity importer changes.

## Git Hygiene

Do not commit local generated state:

- `UnityProject/Library/`
- `UnityProject/Logs/`
- `UnityProject/UserSettings/`
- `src/**/bin/`
- `src/**/obj/`
- Blender backup files such as `*.blend1`

Do commit intentional deployable outputs when they change:

- `1A-Wheelbarrow/Wheelbarrow.dll`
- `1A-Wheelbarrow/Resources/wheelbarrow.unity3d`
- icon assets under `1A-Wheelbarrow/ItemIcons/` and `1A-Wheelbarrow/UIAtlases/`

Before publishing, run at least:

```powershell
dotnet build src\Wheelbarrow\Wheelbarrow.csproj -v:minimal
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe' -batchmode -quit -projectPath UnityProject -executeMethod BuildWheelbarrowBundle.ValidateBuiltBundle -logFile UnityProject\validate-wheelbarrow.log
```
