import math
import os

import bpy
from mathutils import Vector


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
MODEL_DIR = os.path.join(ROOT, "UnityProject", "Assets", "Wheelbarrow", "Models")
BLEND_PATH = os.path.join(MODEL_DIR, "Wheelbarrow.blend")
FBX_PATH = os.path.join(MODEL_DIR, "Wheelbarrow.fbx")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()


def make_material(name, color, metallic=0.0, roughness=0.55):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def set_parent_local(obj, parent, location=(0, 0, 0), rotation=(0, 0, 0)):
    if parent is not None:
        obj.parent = parent
    obj.location = location
    obj.rotation_euler = rotation
    return obj


def empty(name, parent=None, location=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = 0.14
    return set_parent_local(obj, parent, location)


def cube(name, location, scale, material, parent=None, rotation=(0, 0, 0), bevel_width=0.025):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0), rotation=(0, 0, 0))
    obj = bpy.context.object
    obj.name = name
    set_parent_local(obj, parent, location, rotation)
    obj.dimensions = scale
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        obj.data.materials.append(material)
    if bevel_width > 0:
        bevel = obj.modifiers.new("SoftBevel", "BEVEL")
        bevel.width = bevel_width
        bevel.segments = 2
    obj.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")
    return obj


def cylinder_between(name, start, end, radius, material, parent=None, vertices=24):
    start_v = Vector(start)
    end_v = Vector(end)
    mid = (start_v + end_v) * 0.5
    direction = end_v - start_v
    length = direction.length
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=length, location=(0, 0, 0))
    obj = bpy.context.object
    obj.name = name
    set_parent_local(obj, parent, mid, direction.to_track_quat("Z", "Y").to_euler())
    if material:
        obj.data.materials.append(material)
    obj.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")
    return obj


def wheel(name, parent, location, radius=0.32, width=0.18, tire_mat=None, hub_mat=None):
    wheel_root = empty(name, parent, location)

    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=radius, depth=width, location=(0, 0, 0))
    tire = bpy.context.object
    tire.name = f"{name}_Tire"
    set_parent_local(tire, wheel_root, (0, 0, 0), (0, math.radians(90), 0))
    if tire_mat:
        tire.data.materials.append(tire_mat)
    tire.modifiers.new("TireBevel", "BEVEL").width = 0.018
    tire.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")

    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=radius * 0.42, depth=width * 1.08, location=(0, 0, 0))
    hub = bpy.context.object
    hub.name = f"{name}_Hub"
    set_parent_local(hub, wheel_root, (0, 0, 0), (0, math.radians(90), 0))
    if hub_mat:
        hub.data.materials.append(hub_mat)
    hub.modifiers.new("WeightedNormals", "WEIGHTED_NORMAL")

    for angle in range(0, 180, 30):
        rad = math.radians(angle)
        z = math.cos(rad) * radius * 0.78
        y = math.sin(rad) * radius * 0.78
        cylinder_between(
            f"{name}_Spoke_{angle}",
            (0, -y, -z),
            (0, y, z),
            0.009,
            hub_mat,
            wheel_root,
            vertices=8,
        )

    return wheel_root


def build_model():
    clear_scene()
    os.makedirs(MODEL_DIR, exist_ok=True)

    red = make_material("weathered_red_metal", (0.52, 0.08, 0.045, 1.0), metallic=0.45, roughness=0.68)
    red_dark = make_material("dark_red_worn_edges", (0.30, 0.035, 0.025, 1.0), metallic=0.55, roughness=0.72)
    dark_metal = make_material("dark_worn_metal", (0.055, 0.055, 0.05, 1.0), metallic=0.8, roughness=0.5)
    rubber = make_material("matte_black_rubber", (0.012, 0.011, 0.010, 1.0), metallic=0.0, roughness=0.82)
    wood = make_material("worn_handle_wood", (0.42, 0.25, 0.12, 1.0), metallic=0.0, roughness=0.74)

    root = empty("WheelbarrowPrefab")
    m = empty("M", root)
    forks = empty("Forks", m, (0, 0.36, 0.82))
    crank = empty("Crank", m, (0, 0.34, -0.15))
    empty("PedalL", crank, (-0.10, 0, 0))
    empty("PedalR", crank, (0.10, 0, 0))
    empty("Storage", m, (0, 0.80, -0.05))

    # Tray shell. Coordinates are local to M: X width, Y up, Z forward.
    cube("Tray_Bottom", (0, 0.48, -0.05), (0.78, 0.07, 1.15), red, m, (math.radians(-8), 0, 0), 0.035)
    cube("Tray_Left", (-0.42, 0.65, -0.05), (0.07, 0.42, 1.18), red, m, (math.radians(-8), 0, math.radians(-9)), 0.032)
    cube("Tray_Right", (0.42, 0.65, -0.05), (0.07, 0.42, 1.18), red, m, (math.radians(-8), 0, math.radians(9)), 0.032)
    cube("Tray_Front", (0, 0.63, 0.55), (0.82, 0.36, 0.07), red, m, (math.radians(10), 0, 0), 0.032)
    cube("Tray_Rear_Lip", (0, 0.72, -0.63), (0.88, 0.18, 0.06), red, m, (math.radians(-18), 0, 0), 0.03)
    cube("Tray_Wear_Patch", (0, 0.525, -0.05), (0.52, 0.012, 0.56), red_dark, m, (math.radians(-8), 0, 0), 0.01)

    # Rolled rim, welded frame, and handles.
    cylinder_between("Rim_Left", (-0.47, 0.86, -0.62), (-0.43, 0.80, 0.58), 0.026, red_dark, m, vertices=20)
    cylinder_between("Rim_Right", (0.47, 0.86, -0.62), (0.43, 0.80, 0.58), 0.026, red_dark, m, vertices=20)
    cylinder_between("Rim_Front", (-0.40, 0.80, 0.60), (0.40, 0.80, 0.60), 0.026, red_dark, m, vertices=20)
    cylinder_between("Rim_Rear", (-0.46, 0.86, -0.64), (0.46, 0.86, -0.64), 0.026, red_dark, m, vertices=20)

    cylinder_between("Frame_Left", (-0.28, 0.32, 0.78), (-0.48, 0.58, -1.22), 0.033, dark_metal, m)
    cylinder_between("Frame_Right", (0.28, 0.32, 0.78), (0.48, 0.58, -1.22), 0.033, dark_metal, m)
    cylinder_between("Crossbar_Front", (-0.38, 0.42, 0.54), (0.38, 0.42, 0.54), 0.028, dark_metal, m)
    cylinder_between("Crossbar_Rear", (-0.46, 0.54, -0.78), (0.46, 0.54, -0.78), 0.028, dark_metal, m)
    cylinder_between("Handle_Left", (-0.48, 0.58, -1.10), (-0.60, 0.70, -1.48), 0.036, wood, m)
    cylinder_between("Handle_Right", (0.48, 0.58, -1.10), (0.60, 0.70, -1.48), 0.036, wood, m)
    cylinder_between("Handle_CrossGrip", (-0.64, 0.70, -1.52), (0.64, 0.70, -1.52), 0.034, wood, m, vertices=20)
    cube("Grip_Left_End", (-0.66, 0.70, -1.52), (0.10, 0.08, 0.16), wood, m, (0, math.radians(8), 0), 0.035)
    cube("Grip_Right_End", (0.66, 0.70, -1.52), (0.10, 0.08, 0.16), wood, m, (0, math.radians(-8), 0), 0.035)

    # The game rotates M/Forks for steering and M/Forks/Wheel0 for tire spin.
    cylinder_between("Fork_Left", (-0.22, 0.05, -0.03), (-0.10, 0.00, 0.20), 0.024, dark_metal, forks)
    cylinder_between("Fork_Right", (0.22, 0.05, -0.03), (0.10, 0.00, 0.20), 0.024, dark_metal, forks)
    cylinder_between("Axle_Front", (-0.25, 0, 0), (0.25, 0, 0), 0.022, dark_metal, forks)
    wheel("Wheel0", forks, (0, 0, 0), radius=0.32, width=0.16, tire_mat=rubber, hub_mat=dark_metal)

    # Rear contact is a small hidden-assist roller so the bicycle class has two stable wheels.
    wheel("Wheel1", root, (0, 0.18, -0.78), radius=0.13, width=0.42, tire_mat=rubber, hub_mat=dark_metal)
    cylinder_between("Stand_Left", (-0.28, 0.46, -0.58), (-0.32, 0.12, -0.84), 0.025, dark_metal, m)
    cylinder_between("Stand_Right", (0.28, 0.46, -0.58), (0.32, 0.12, -0.84), 0.025, dark_metal, m)
    cylinder_between("Stand_Foot_Left", (-0.42, 0.12, -0.86), (-0.22, 0.12, -0.86), 0.024, dark_metal, m)
    cylinder_between("Stand_Foot_Right", (0.22, 0.12, -0.86), (0.42, 0.12, -0.86), 0.024, dark_metal, m)

    # Geometry above is authored in Unity's frame (X width, Y up, Z forward),
    # but Blender's world is Z-up. Stand the whole rig up so the model is a
    # standard Z-up Blender asset before the FBX export: author +Y (up) -> +Z,
    # author +Z (forward) -> -Y. This rotation lives on the root empty so every
    # named child (M/Forks/Wheel0/...) keeps its local axes, leaving the game's
    # local steer/spin rotations untouched.
    root.rotation_euler = (math.radians(90), 0, 0)

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    bpy.context.view_layer.objects.active = root

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=False,
        object_types={"EMPTY", "MESH"},
        apply_unit_scale=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        use_mesh_modifiers=True,
    )


if __name__ == "__main__":
    build_model()
