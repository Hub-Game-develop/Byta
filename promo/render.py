"""渲染 PNG 序列（可中断续渲），再用 ffmpeg 合成 mp4。"""
import bpy

scene = bpy.context.scene
scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGB'
scene.render.image_settings.color_depth = '8'
scene.render.image_settings.compression = 15
scene.render.filepath = "/Users/cangcang/WorkBuddy/2026-10-23-07-10-54/byta/promo/frames/frame_"
scene.render.resolution_percentage = 100
scene.render.use_overwrite = False      # 已渲染的帧跳过，中断后能续
print(f"[render] {scene.frame_start}-{scene.frame_end} @ {scene.render.resolution_x}x{scene.render.resolution_y}")
