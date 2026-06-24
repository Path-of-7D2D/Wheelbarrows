# Wheelbarrow

Prototype 7 Days To Die V3.0 wheelbarrow mod.

This first pass proves the custom vehicle asset pipeline:

- Blender generates the wheelbarrow source model and FBX.
- Unity 2022.3.62f2 imports the FBX and builds `wheelbarrow.unity3d`.
- XML appends a storage-focused vehicle, placeable item, recipe, and localization.

The MVP intentionally extends the vanilla bicycle vehicle behavior. It should be
treated as a slow storage cart first, not the final "walk behind and push it"
implementation.

## Paths

- Deployable mod: `1A-Wheelbarrow`
- Blender generator: `tools/generate_wheelbarrow_model.py`
- Unity project: `UnityProject`
- Asset bundle output: `1A-Wheelbarrow/Resources/wheelbarrow.unity3d`

## Build

Generate the model:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python tools\generate_wheelbarrow_model.py
```

Build the Unity asset bundle:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe' -batchmode -quit -projectPath UnityProject -executeMethod BuildWheelbarrowBundle.BuildAll -logFile UnityProject\build-wheelbarrow.log
```

Validate the built bundle:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe' -batchmode -quit -projectPath UnityProject -executeMethod BuildWheelbarrowBundle.ValidateBuiltBundle -logFile UnityProject\validate-wheelbarrow.log
```

Install by copying `1A-Wheelbarrow` into the game `Mods` folder.

For a quick in-game check, use creative search for `Wheelbarrow` or console
`giveself vehicleWheelbarrowPlaceable`.

After building the DLL, the faster test command is:

```text
wb
```

Optional distance:

```text
wb 5
```

Cleanup spawned test wheelbarrows:

```text
wb cleanup
```

Log active wheelbarrow renderer state:

```text
wb debug
```

Aliases: `wheelbarrow`, `spawnwheelbarrow`, `wbspawn`.
