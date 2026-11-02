"""Blender 4.1: --background --python this.py -- source.blend output_dir addon_repo

Exports the current evaluated static meshes and plugin JSON without saving the blend.
The fixed FBX preset is paired with FactoryBuildingModelPipeline.Configure.
"""
import sys
import json
from pathlib import Path
import bpy

source, output, addon = sys.argv[sys.argv.index('--') + 1:]
sys.path.insert(0, addon)
import factory_building
if not hasattr(bpy.types.Scene, 'factory_building'):
    factory_building.register()
bpy.ops.wm.open_mainfile(filepath=str(Path(source).resolve()))
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
output = Path(output).resolve()
output.mkdir(parents=True, exist_ok=True)
key = Path(source).stem
bpy.ops.factory.export_json(filepath=str(output / (key + '.factory.json')))
bpy.ops.object.select_all(action='DESELECT')
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH'
          and o.factory_collider.kind == 'NONE' and o.get('factory_role') != 'collider']
expected = []
graph = bpy.context.evaluated_depsgraph_get()
for obj in meshes:
    obj.hide_set(False)
    obj.hide_viewport = False
    obj.select_set(True)
    evaluated = obj.evaluated_get(graph)
    mesh = evaluated.to_mesh()
    coords = [evaluated.matrix_world @ v.co for v in mesh.vertices]
    if coords:
        lo = [min(v[i] for v in coords) for i in (0, 2, 1)]
        hi = [max(v[i] for v in coords) for i in (0, 2, 1)]
        expected.append(dict(name=obj.name, minimum=lo, maximum=hi))
    evaluated.to_mesh_clear()
if not meshes:
    raise RuntimeError('No model meshes selected')
bpy.context.view_layer.objects.active = meshes[0]
# Ignore Blender's unit display scale, as specified by factory.json v1.
bpy.context.scene.unit_settings.scale_length = 1.0
bpy.ops.export_scene.fbx(filepath=str(output / (key + '.fbx')), use_selection=True,
    object_types={'MESH'}, global_scale=1.0, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
    use_space_transform=True, bake_space_transform=True, bake_anim=False,
    use_mesh_modifiers=True, path_mode='COPY', embed_textures=False,
    mesh_smooth_type='FACE', use_custom_props=False)
(output / (key + '.geometry.json')).write_text(json.dumps(dict(meshes=expected), indent=2), encoding='utf-8')
print('FACTORY_EXPORT_COMPLETE', key, 'meshes=', len(meshes))
