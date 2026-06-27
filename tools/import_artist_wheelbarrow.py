"""Import the artist's wheelbarrow (wheelbarrow_LOD0.fbx), orient/scale it to the
mod's convention, rig it for the 7D2D vehicle/push system, and export the project
Wheelbarrow.fbx that BuildWheelbarrowBundle consumes.

Source FBX is authored Z-up, length along X (wheel at -X), ~5.4 units long, with
three meshes (hull, chasis_frame, wheel) UV-mapped to vanilla minibike /
banditWallMetal textures.

Run: blender -b --python tools/import_artist_wheelbarrow.py
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MODEL_DIR = os.path.join(ROOT, "UnityProject", "Assets", "Wheelbarrow", "Models")
BLEND_PATH = os.path.join(MODEL_DIR, "Wheelbarrow.blend")
FBX_PATH = os.path.join(MODEL_DIR, "Wheelbarrow.fbx")
SOURCE_FBX = r"C:\Users\rusta\Downloads\wheelbarrow_LOD0.fbx"

# Rotate +90 about Z so length runs along Y with the wheel toward -Y (FBX export then
# maps -Y -> Unity +Z forward); scale so the rig is ~2.7 Blender units long like before.
ORIENT_Z_DEG = 90.0
SCALE = 0.5


def empty(name, parent=None, location=(0, 0, 0)):
    """Create an empty at a WORLD position (set via matrix_world after parenting, with
    a depsgraph update, so parent matrices are fresh and nothing jumps)."""
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.12
    if parent is not None:
        obj.parent = parent
    bpy.context.view_layer.update()
    obj.matrix_world = Matrix.Translation(Vector(location))
    bpy.context.view_layer.update()
    return obj


def reparent(child, parent):
    """Parent keeping world transform (explicit matrix_world to avoid stale matrices)."""
    bpy.context.view_layer.update()
    world = child.matrix_world.copy()
    child.parent = parent
    bpy.context.view_layer.update()
    child.matrix_world = world


def world_bounds(objs):
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                mn[i] = min(mn[i], w[i])
                mx[i] = max(mx[i], w[i])
    return mn, mx


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    bpy.ops.import_scene.fbx(filepath=SOURCE_FBX)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]

    # Orient straight into Unity's frame (X width, Y up, Z forward with the wheel at
    # +Z) and scale. We export with bake_space_transform=True, which maps Blender axes
    # directly to Unity, so the model must already be Y-up here. (The artist FBX is
    # Z-up/length-X; +90 about Z puts the wheel at -Y, then -90 about X stands it up
    # with the wheel toward +Z.) bake=True also avoids the non-uniform stretch the
    # normal up-axis export conversion bakes into this imported geometry.
    for o in meshes:
        o.rotation_euler = (0.0, 0.0, math.radians(ORIENT_Z_DEG))
        o.scale = (SCALE, SCALE, SCALE)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for o in meshes:
        o.rotation_euler = (math.radians(-90), 0.0, 0.0)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Sit on the ground (min Y = 0, up is Y now) and centre on X.
    mn, mx = world_bounds(meshes)
    shift = Vector((-(mn.x + mx.x) * 0.5, -mn.y, 0.0))
    for o in meshes:
        o.location += shift
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    by_name = {o.name: o for o in meshes}
    hull = next((o for o in meshes if "hull" in o.name.lower()), None)
    frame = next((o for o in meshes if "chasis" in o.name.lower() or "frame" in o.name.lower()), None)
    wheel = next((o for o in meshes if "wheel" in o.name.lower()), None)

    # Wheel centre (axle) from the wheel mesh.
    wmn, wmx = world_bounds([wheel])
    wheel_center = (wmn + wmx) * 0.5

    # Tub centre for the storage attach point.
    hmn, hmx = world_bounds([hull])
    tub_center = (hmn + hmx) * 0.5

    # Handle grips: take the rearmost frame verts (min Z; handles point to -Z), split
    # left/right by X.
    fmn, fmx = world_bounds([frame])
    rear_cut = fmn.z + 0.18
    left_pts, right_pts = [], []
    for v in frame.data.vertices:
        wv = frame.matrix_world @ v.co
        if wv.z <= rear_cut:
            (left_pts if wv.x < 0 else right_pts).append(wv)

    def centroid(pts, fallback):
        if not pts:
            return fallback
        s = Vector((0, 0, 0))
        for p in pts:
            s += p
        return s / len(pts)

    grip_l = centroid(left_pts, Vector((-0.4, 0.75, fmn.z + 0.05)))
    grip_r = centroid(right_pts, Vector((0.4, 0.75, fmn.z + 0.05)))
    print(f"GRIP_L={tuple(round(v,3) for v in grip_l)}  GRIP_R={tuple(round(v,3) for v in grip_r)}")
    print(f"WHEEL_CENTER={tuple(round(v,3) for v in wheel_center)}  TUB={tuple(round(v,3) for v in tub_center)}")

    # Keep the Blender hierarchy shallow: just root/M/{hull, frame, wheel}. The deeper
    # vehicle rig (Forks/Wheel0/Crank/Storage/Grips/Wheel1) is built in Unity instead,
    # because exporting nested empties with bake_space_transform compounds the unit
    # scale per level and collapses anything more than one empty deep to zero size.
    root = empty("WheelbarrowPrefab")
    m = empty("M", root)
    reparent(hull, m)
    reparent(frame, m)
    reparent(wheel, m)

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    bpy.context.view_layer.objects.active = root

    # The artist FBX import leaves the scene on a non-1 unit scale, which makes the
    # FBX exporter bake a non-uniform stretch along the up-axis conversion. Reset it.
    print(f"scene scale_length before reset = {bpy.context.scene.unit_settings.scale_length}")
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0

    os.makedirs(MODEL_DIR, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=False,
        object_types={"EMPTY", "MESH"},
        apply_unit_scale=True,
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        use_mesh_modifiers=True,
        path_mode="STRIP",
    )
    print("exported", FBX_PATH)


if __name__ == "__main__":
    main()
