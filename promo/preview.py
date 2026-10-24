import bpy, os, sys

OUT = "/tmp/byta-anim/preview"
os.makedirs(OUT, exist_ok=True)

scene = bpy.context.scene
# 预览用 PNG、半分辨率
scene.render.resolution_percentage = 50
scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
scene.render.use_motion_blur = False

frames = [1, 30, 70, 110, 160, 210, 240]
for f in frames:
    scene.frame_set(f)
    scene.render.filepath = os.path.join(OUT, f"f{f:03d}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[preview] frame {f} -> {scene.render.filepath}")
