import bpy, time
scene = bpy.context.scene
scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
scene.frame_set(170)
scene.render.filepath = "/tmp/byta-anim/preview/full1080.png"
t = time.time()
bpy.ops.render.render(write_still=True)
print(f"[timing] 1080p 单帧 {time.time()-t:.2f}s, samples={scene.eevee.taa_render_samples}")
