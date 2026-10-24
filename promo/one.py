import bpy
scene = bpy.context.scene
scene.render.resolution_percentage = 50
scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
scene.frame_set(170)
scene.render.filepath = "/tmp/byta-anim/preview/check9x9.png"
bpy.ops.render.render(write_still=True)
