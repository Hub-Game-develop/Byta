import bpy
scene = bpy.context.scene
scene.render.resolution_percentage = 50
scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
for f in (10, 40, 62, 90):
    scene.frame_set(f)
    scene.render.filepath = f"/tmp/byta-anim/preview/c{f:03d}.png"
    bpy.ops.render.render(write_still=True)
