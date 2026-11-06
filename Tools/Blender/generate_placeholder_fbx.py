"""Blender 4.1 --background --python this.py -- [--project-root path]

Rebuild the original placeholder geometry as FBX, preserving Unity hierarchy,
local transforms, material slot names and the belt's named controllable parts.
The checked-in JSON snapshot is the editable source for these placeholder assets.
"""
import argparse
import json
import sys
from pathlib import Path
import bpy
from mathutils import Matrix, Vector, Quaternion

args = argparse.ArgumentParser()
args.add_argument('--project-root', type=Path, default=Path(__file__).resolve().parents[2])
options = args.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
project = options.project_root.resolve()
snapshot = json.loads((project / 'Tools/Blender/Fixtures/PlaceholderModels.json').read_text(encoding='utf-8'))
swap = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))

for model in snapshot['models']:
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    anchor = bpy.data.objects.new('Factory Export Root', None)
    bpy.context.collection.objects.link(anchor)
    objects = []
    for node in model['nodes']:
        mesh = None
        if node.get('vertices'):
            mesh = bpy.data.meshes.new(node['name'])
            vertices = [(v['x'], v['z'], v['y']) for v in node['vertices']]
            faces, slots = [], []
            for slot, face_set in enumerate(node['faces']):
                indices = face_set['triangles']
                faces.extend(tuple(reversed(indices[i:i + 3])) for i in range(0, len(indices), 3))
                slots.extend([slot] * (len(indices) // 3))
                name = Path(face_set['material']).stem
                material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
                mesh.materials.append(material)
            mesh.from_pydata(vertices, [], faces)
            mesh.update()
            for polygon, slot in zip(mesh.polygons, slots):
                polygon.material_index = slot
                polygon.use_smooth = node['name'].startswith('Test')
            uv = node.get('uv') or []
            if len(uv) == len(vertices):
                layer = mesh.uv_layers.new(name='UVMap')
                for polygon in mesh.polygons:
                    for loop in polygon.loop_indices:
                        value = uv[mesh.loops[loop].vertex_index]
                        layer.data[loop].uv = (value['x'], value['y'])
        obj = bpy.data.objects.new(node['name'], mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = objects[node['parent']] if node['parent'] >= 0 else anchor
        p, q, s = node['position'], node['rotation'], node['scale']
        local = Matrix.LocRotScale(Vector((p['x'], p['y'], p['z'])),
            Quaternion((q['w'], q['x'], q['y'], q['z'])), Vector((s['x'], s['y'], s['z'])))
        obj.matrix_basis = swap @ local @ swap
        objects.append(obj)
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active = anchor
    bpy.context.scene.unit_settings.scale_length = 1
    output = project / model['output']
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(output), use_selection=True,
        object_types={'MESH', 'EMPTY'}, global_scale=1, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        use_space_transform=True, bake_space_transform=False, bake_anim=False,
        use_mesh_modifiers=True, path_mode='AUTO', mesh_smooth_type='FACE', use_custom_props=False)
    print('FACTORY_PLACEHOLDER_FBX', model['key'], output)
print('FACTORY_PLACEHOLDER_COMPLETE', len(snapshot['models']))
