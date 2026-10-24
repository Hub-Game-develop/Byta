"""
Byta 宣传动画 — 场景构建脚本（Blender 5.1 + Python）

用法：
    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build_promo.py

产物：
    byta-promo.blend   可直接在 Blender 里打开的源文件
    同时把渲染参数、帧率、输出路径都配置好，打开就能 Render Animation。

视觉语言刻意和项目本身对齐：
    深色底 #0B0D13、7×7 立方体阵列（spacing 3.05 / 边长 2.3，与 WebGL demo 同参数）、
    青蓝发光（#2B6CFF → #22D3EE）。
"""

import math
import os
import sys

import bpy
from mathutils import Vector

OUT_DIR = "/Users/cangcang/WorkBuddy/2026-10-23-07-10-54/byta/promo"
FPS = 24
FRAME_START = 1
FRAME_END = 240

FONT_BOLD = "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
FONT_REGULAR = "/System/Library/Fonts/Supplemental/Arial.ttf"
FONT_CJK = "/System/Library/Fonts/Hiragino Sans GB.ttc"

# ---------------------------------------------------------------- 工具


def log(message):
    print(f"[byta-promo] {message}")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.lights, bpy.data.cameras):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def make_emission_material(name, color, strength, roughness=0.3, metallic=0.0):
    """Principled + 自发光。用 Principled 而不是纯 Emission，这样还能吃到灯光和反射。"""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
    bsdf.inputs["Emission Strength"].default_value = strength
    return mat


def load_font(path):
    try:
        font = bpy.data.fonts.load(path)
        log(f"字体加载成功：{os.path.basename(path)}")
        return font
    except Exception as exc:                                    # noqa: BLE001
        log(f"字体加载失败 {path}: {exc}（退回默认字体）")
        return None


# ---------------------------------------------------------------- 场景


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.fps = FPS
    scene.frame_start = FRAME_START
    scene.frame_end = FRAME_END

    # EEVEE：开运动模糊，采样适度（这台机器 swap 紧张，别拉满）
    eevee = scene.eevee
    for attr, value in (
        ("taa_render_samples", 48),
        ("use_gtao", True),
        ("use_bloom", True),          # 4.x 之后可能已移除，下面做兼容
        ("use_raytracing", True),
        ("use_shadows", True),
    ):
        if hasattr(eevee, attr):
            try:
                setattr(eevee, attr, value)
            except Exception:                                  # noqa: BLE001
                pass

    scene.render.use_motion_blur = True
    if hasattr(scene.render, "motion_blur_shutter"):
        scene.render.motion_blur_shutter = 0.35

    # 色彩管理：AgX 偏灰，宣传片用 Standard 更透亮
    try:
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Punchy"
    except Exception:                                          # noqa: BLE001
        pass

    # 输出：H.264 MP4
    # 注意 Blender 5.1 的新约束：必须先把 media_type 切成 VIDEO，
    # file_format 的枚举里才会出现 FFMPEG（否则报 "enum FFMPEG not found"）。
    scene.render.image_settings.media_type = "VIDEO"
    scene.render.image_settings.file_format = "FFMPEG"
    scene.render.ffmpeg.format = "MPEG4"
    scene.render.ffmpeg.codec = "H264"
    scene.render.ffmpeg.constant_rate_factor = "HIGH"
    scene.render.ffmpeg.ffmpeg_preset = "GOOD"
    scene.render.filepath = os.path.join(OUT_DIR, "byta-promo.mp4")
    log(f"渲染配置：{scene.render.resolution_x}x{scene.render.resolution_y} @ {FPS}fps, {FRAME_START}-{FRAME_END}")


def setup_compositor():
    """
    EEVEE 4.2 之后没有内置 bloom 了，自发光想有光晕必须在合成器里加 Glare。

    Blender 5.x 的合成器又换了一套 API：不再是 scene.node_tree，
    而是 scene.compositing_node_group（一个 NodeTree），且输出要用 NodeGroupOutput，
    得先在 group.interface 里声明一个输出 socket。
    """
    scene = bpy.context.scene
    group = bpy.data.node_groups.new("BytaCompositing", "CompositorNodeTree")
    group.interface.new_socket(name="Image", in_out="OUTPUT", socket_type="NodeSocketColor")
    scene.compositing_node_group = group
    scene.use_nodes = True

    nodes, links = group.nodes, group.links
    layers = nodes.new("CompositorNodeRLayers")
    glare = nodes.new("CompositorNodeGlare")
    output = nodes.new("NodeGroupOutput")

    try:
        glare.inputs["Threshold"].default_value = 0.9
    except Exception:                                          # noqa: BLE001
        pass
    try:
        glare.inputs["Type"].default_value = "Fog Glow"
    except Exception:                                          # noqa: BLE001
        pass

    links.new(layers.outputs["Image"], glare.inputs["Image"])
    links.new(glare.outputs["Image"], output.inputs[0])
    log("合成器：Glare 光晕已接入")


def setup_world():
    world = bpy.data.worlds.new("BytaWorld")
    world.use_nodes = True
    bpy.context.scene.world = world

    nodes = world.node_tree.nodes
    links = world.node_tree.links
    nodes.clear()

    bg = nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = (0.006, 0.008, 0.014, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    output = nodes.new("ShaderNodeOutputWorld")
    links.new(bg.outputs["Background"], output.inputs["Surface"])


def build_floor():
    """一块很暗、略带反射的地面 —— 让立方体有"落在地上"的感觉。"""
    bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, -1.2))
    floor = bpy.context.object
    floor.name = "Floor"

    mat = bpy.data.materials.new("FloorMat")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.016, 0.022, 0.034, 1)
    bsdf.inputs["Roughness"].default_value = 0.18
    bsdf.inputs["Metallic"].default_value = 0.85
    floor.data.materials.append(mat)
    return floor


def build_cube_grid():
    """
    7×7 立方体阵列，参数与 WebGL demo 完全一致：
        spacing = 3.05，边长 = 2.3。
    每个立方体的升起延迟按"离中心的距离"递减，形成从中心向外扩散的波浪。
    """
    count = 9
    spacing = 3.05
    size = 2.3
    cubes = []

    cool = make_emission_material("CubeCool", (0.035, 0.085, 0.22), 0.35, roughness=0.22, metallic=0.6)
    hot = make_emission_material("CubeHot", (0.05, 0.42, 0.85), 1.1, roughness=0.16, metallic=0.35)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    template = bpy.context.object
    template.name = "CubeTemplate"
    template.data.materials.append(cool)
    template.data.materials.append(hot)

    center = (count - 1) / 2.0
    for ix in range(count):
        for iy in range(count):
            x = (ix - center) * spacing
            y = (iy - center) * spacing
            distance = math.hypot(ix - center, iy - center)

            cube = template.copy()
            cube.data = template.data.copy()
            cube.name = f"Cube_{ix}_{iy}"
            bpy.context.collection.objects.link(cube)

            cube.scale = (size, size, size)
            cube.rotation_euler = (0, 0, 0)

            # 中心区域用亮材质（slot 1）
            if distance < 1.6:
                cube.data.materials.clear()
                cube.data.materials.append(hot)
                cube.data.materials.append(cool)
                cube.data.materials[0] = hot
                cube.active_material_index = 0
                for poly in cube.data.polygons:
                    poly.material_index = 0
            else:
                for poly in cube.data.polygons:
                    poly.material_index = 0

            # 升起动画：中心先起，边缘后起
            delay = 1 + distance * 3.2
            start_z = -40.0
            # 中心高、外围低，形成轻微的地形起伏（不是一块平板）
            height = max(0.0, 2.6 - distance * 0.55)
            end_z = size / 2.0 + height

            cube.location = (x, y, start_z)
            cube.keyframe_insert("location", frame=FRAME_START)
            cube.location = (x, y, end_z)
            cube.keyframe_insert("location", frame=int(FRAME_START + 26 + delay))

            # 轻微的旋转，让静止画面也有生气
            cube.rotation_euler = (0, 0, 0)
            cube.keyframe_insert("rotation_euler", frame=FRAME_START)

            cubes.append(cube)

    # 模板本身不要显示
    template.hide_render = True
    template.hide_viewport = True
    log(f"立方体阵列：{len(cubes)} 个")
    return cubes


def build_text(name, body, font, size, location, extrude=0.06, color=(0.92, 0.96, 1.0), strength=2.2):
    # Text 默认躺在 XY 平面上、法线朝 +Z。绕 X 转 90° 才是"立着面向相机"。
    bpy.ops.object.text_add(location=location, rotation=(math.radians(90), 0, 0))
    text = bpy.context.object
    text.name = name
    text.data.body = body
    text.data.size = size
    text.data.extrude = extrude
    text.data.bevel_depth = extrude * 0.12
    text.data.bevel_resolution = 3
    text.data.align_x = "CENTER"
    text.data.align_y = "CENTER"
    if font is not None:
        text.data.font = font

    mat = make_emission_material(f"{name}Mat", color, strength, roughness=0.28, metallic=0.15)
    text.data.materials.append(mat)
    return text


def build_text_block():
    bold = load_font(FONT_BOLD)
    regular = load_font(FONT_REGULAR)
    cjk = load_font(FONT_CJK)

    # 主标题
    title = build_text("Title", "Byta", bold, 3.4, (0, 0, 12.0), extrude=0.14, color=(0.94, 0.97, 1.0), strength=4.2)
    # 副标题
    subtitle = build_text("Subtitle", "MODERN  .NET  OPENGL", regular, 0.86, (0, 0, 9.95),
                          extrude=0.03, color=(0.35, 0.62, 1.0), strength=3.6)
    # 三行要点
    lines = [
        ("Line1", "GL 4.6 core  ·  2651 命令  ·  零托管分配", 8.7),
        ("Line2", "内置 Mesa  ·  WebGL2 后端  ·  可插拔实现", 7.9),
        ("Line3", "同一个 API，本机与浏览器都能渲染", 7.1),
    ]
    features = []
    for name, body, z in lines:
        font = cjk if any("\u4e00" <= ch <= "\u9fff" for ch in body) else regular
        features.append(build_text(name, body, font, 0.62, (0, 0, z),
                                   extrude=0.02, color=(0.55, 0.74, 1.0), strength=2.6))

    # 文字时序：立方体先升完（约第 45 帧），文字再从第 58 帧起逐层浮出
    for obj, delay in ((title, 0), (subtitle, 10), *[(f, 22 + i * 11) for i, f in enumerate(features)]):
        base = obj.location.copy()
        appear = 58 + delay

        # 出场前彻底不可见：scale 压到 0.001 并保持到出场帧。
        # 只做位移不做缩放的话，前 2 秒画面上会一直挂着完整文字，
        # 立方体升起的悬念就没了。
        obj.scale = (0.001, 0.001, 0.001)
        obj.keyframe_insert("scale", frame=FRAME_START)
        obj.keyframe_insert("scale", frame=appear)

        obj.location = (base.x, base.y, base.z - 1.8)
        obj.keyframe_insert("location", frame=appear)
        obj.location = base
        obj.keyframe_insert("location", frame=appear + 42)

        obj.scale = (0.88, 0.88, 0.88)
        obj.keyframe_insert("scale", frame=appear + 4)
        obj.scale = (1.0, 1.0, 1.0)
        obj.keyframe_insert("scale", frame=appear + 50)

    log("文字层：1 标题 + 1 副标题 + 3 行要点")
    return title, subtitle, features


def build_lights():
    """两盏面光打轮廓 + 一盏顶光。全部绑定在固定位置，靠相机运动制造变化。"""
    specs = [
        ("KeyLight", (-16, -20, 22), 2600, (0.55, 0.72, 1.0), 22),
        ("RimLight", (20, 16, 14), 3400, (0.25, 0.55, 1.0), 16),
        ("TopLight", (0, 0, 34), 1800, (0.9, 0.95, 1.0), 26),
    ]
    for name, location, energy, color, size in specs:
        light_data = bpy.data.lights.new(name, type="AREA")
        light_data.energy = energy
        light_data.color = color
        light_data.size = size
        light = bpy.data.objects.new(name, light_data)
        light.location = location
        bpy.context.collection.objects.link(light)

    # 让灯都朝向原点
    target = bpy.data.objects.new("LightTarget", None)
    target.location = (0, 0, 2)
    bpy.context.collection.objects.link(target)
    for obj in bpy.data.objects:
        if obj.type == "LIGHT":
            constraint = obj.constraints.new("TRACK_TO")
            constraint.target = target
            constraint.track_axis = "TRACK_NEGATIVE_Z"
            constraint.up_axis = "UP_Y"


def build_camera():
    camera_data = bpy.data.cameras.new("Camera")
    camera_data.lens = 52
    camera = bpy.data.objects.new("Camera", camera_data)
    bpy.context.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    target = bpy.data.objects.new("CameraTarget", None)
    bpy.context.collection.objects.link(target)
    target.location = (0, 0, 5.2)
    target.keyframe_insert("location", frame=FRAME_START)
    target.location = (0, 0, 7.4)
    target.keyframe_insert("location", frame=110)
    target.location = (0, 0, 6.4)
    target.keyframe_insert("location", frame=FRAME_END)

    constraint = camera.constraints.new("TRACK_TO")
    constraint.target = target
    constraint.track_axis = "TRACK_NEGATIVE_Z"
    constraint.up_axis = "UP_Y"

    # 五段相机运动：低角度推进 → 环绕 → 拉高收尾
    keys = [
        (FRAME_START, (34.0, -50.0, 12.0)),
        (34, (19.0, -33.0, 11.5)),
        (110, (-7.0, -30.0, 14.5)),
        (160, (-16.0, -38.0, 19.0)),
        (205, (3.0, -48.0, 24.0)),
        (FRAME_END, (10.0, -42.0, 21.0)),
    ]
    for frame, location in keys:
        camera.location = location
        camera.keyframe_insert("location", frame=frame)

    # 缓入缓出，别是匀速直线。
    # Blender 5.x 把 Action 改成了 slotted action，action.fcurves 不存在了，
    # 要沿 layers → strips → channelbags 取；4.x 还是老的 action.fcurves。
    for fcurve in iter_fcurves(camera.animation_data.action):
        for keyframe in fcurve.keyframe_points:
            keyframe.interpolation = "BEZIER"
            keyframe.easing = "EASE_IN_OUT"
    log("相机：6 个位置关键帧 + Track To 约束")
    return camera


def iter_fcurves(action):
    """兼容 4.x / 5.x 的 Action 结构。"""
    if action is None:
        return
    if hasattr(action, "fcurves"):
        yield from action.fcurves
        return

    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                yield from bag.fcurves


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    clear_scene()
    setup_render()
    setup_compositor()
    setup_world()
    build_floor()
    build_cube_grid()
    build_text_block()
    build_lights()
    build_camera()

    # 回到第一帧，打开时看到的是开头
    bpy.context.scene.frame_set(FRAME_START)

    blend_path = os.path.join(OUT_DIR, "byta-promo.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    log(f"已保存 {blend_path}")
    log(f"文件大小 {os.path.getsize(blend_path) / 1024:.0f} KB")


if __name__ == "__main__":
    main()
