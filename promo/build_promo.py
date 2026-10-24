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
FRAME_END = 336

FONT_BOLD = "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
FONT_REGULAR = "/System/Library/Fonts/Supplemental/Arial.ttf"
FONT_CJK = "/System/Library/Fonts/Hiragino Sans GB.ttc"
FONT_MONO = "/System/Library/Fonts/SFNSMono.ttf"

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
        ("taa_render_samples", 40),
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


def build_text(name, body, font, size, location, extrude=0.06, color=(0.92, 0.96, 1.0), strength=2.2, align="CENTER"):
    # 绕 X 转 90°：文字立在 XZ 平面、正面朝 -Y。
    # 相机全程待在 -Y 半空间，所以不需要 Track To —— 那个约束在近水平视角下会
    # 因为 up 轴与视线平行而退化成万向锁，把文字压成一条线。
    bpy.ops.object.text_add(location=location, rotation=(math.radians(90), 0, 0))
    text = bpy.context.object
    text.name = name
    text.data.body = body
    text.data.size = size
    text.data.extrude = extrude
    text.data.bevel_depth = extrude * 0.12
    text.data.bevel_resolution = 3
    text.data.align_x = align
    text.data.align_y = "CENTER"
    if font is not None:
        text.data.font = font

    mat = make_emission_material(f"{name}Mat", color, strength, roughness=0.28, metallic=0.15)
    text.data.materials.append(mat)
    return text


def build_text_block(camera):
    """
    文字分四章，每章只在自己的时间窗内可见（靠 scale 0.001 实现"从无到有"）。

    所有文字都用 Track To 约束朝向相机 —— 否则相机环绕到侧面时会看到文字的侧边。
    """
    bold = load_font(FONT_BOLD)
    regular = load_font(FONT_REGULAR)
    cjk = load_font(FONT_CJK)
    mono = load_font(FONT_MONO) or regular

    def mono_text(name, body, size, z, color, strength, align="CENTER", x=0.0):
        return build_text(name, body, mono, size, (x, 0, z), extrude=0.02,
                          color=color, strength=strength, align=align)

    cyan = (0.35, 0.72, 1.0)
    soft = (0.62, 0.78, 1.0)
    white = (0.95, 0.97, 1.0)
    dim = (0.55, 0.66, 0.85)

    sequence = []   # (对象, 出现帧, 消失帧)

    # ---- 第一章：标题
    title = build_text("Title", "Byta", bold, 3.4, (0, 0, 12.0), extrude=0.14, color=white, strength=4.2)
    subtitle = build_text("Subtitle", "MODERN  .NET  OPENGL", regular, 0.86, (0, 0, 9.95),
                          extrude=0.03, color=cyan, strength=3.6)
    tagline = build_text("Tagline", "一套 API，本机与浏览器都能渲染", cjk, 0.62, (0, 0, 8.7),
                         extrude=0.02, color=dim, strength=2.6)
    sequence += [(title, 46, 132), (subtitle, 56, 132), (tagline, 86, 132)]

    # ---- 第二章：API 卡（两屏，每屏两个）
    def api_pair(prefix, api, api_desc, z_api, z_desc, appear, disappear):
        name = mono_text(f"{prefix}Api", api, 1.05, z_api, white, 3.4)
        desc = build_text(f"{prefix}Desc", api_desc, cjk, 0.5, (0, 0, z_desc),
                          extrude=0.015, color=soft, strength=2.2)
        sequence.append((name, appear, disappear))
        sequence.append((desc, appear + 6, disappear))

    api_pair("A1", "GlBuffer",
             "值句柄缓冲 · Span 直传 · 热路径零托管分配",
             10.2, 9.1, 140, 200)
    api_pair("A2", "GlProgram",
             "编译失败直接抛异常，消息里带完整驱动日志",
             7.5, 6.4, 152, 200)
    api_pair("B1", "GlState",
             "线程局部状态缓存 · 冗余绑定到不了驱动",
             10.2, 9.1, 204, 262)
    api_pair("B2", "GlApi",
             "后端门面 · 原生与 WebGL2 走同一套调用",
             7.5, 6.4, 216, 262)

    # ---- 第三章：代码片段（左对齐，逐行出现）
    code_lines = [
        "var session = BytaRuntime.Initialize();",
        "using var vbo = GlBuffer.Create();",
        "vbo.Allocate(vertices);        // Span<T>, no allocation",
        "GlDraw.ElementsInstanced(Triangles, 36, UnsignedInt, 0, 49);",
    ]
    for index, line in enumerate(code_lines):
        obj = mono_text(f"Code{index}", line, 0.62, 9.6 - index * 1.5, cyan, 2.6,
                        align="LEFT", x=-8.2)
        sequence.append((obj, 268 + index * 9, 306))

    # ---- 第四章：结尾 + 文档链接
    closing = build_text("Closing", "Byta", bold, 2.2, (0, 0, 9.9), extrude=0.08, color=white, strength=4.0)
    url = mono_text("Url", "hub-game-develop.github.io/Byta/api/", 0.62, 7.5, cyan, 3.2)
    hint = build_text("UrlHint", "API 参考", cjk, 0.46, (0, 0, 6.5), extrude=0.02,
                      color=dim, strength=2.0)
    sequence += [(closing, 310, 336), (url, 316, 336), (hint, 320, 336)]

    # ---- 统一打关键帧
    for obj, appear, disappear in sequence:
        animate_window(obj, appear, disappear, fade_out=disappear < FRAME_END)

    # 不做 billboard：文字固定朝 -Y，相机全程在 -Y 半空间（见 build_camera 的 key），
    # 最多 20° 左右的斜视，反而更有纵深。

    log(f"文字层：{len(sequence)} 个对象，{len({a for _, a, _ in sequence})} 个出场时刻")
    return title


def animate_window(obj, appear, disappear, fade=9, fade_out=True):
    """
    在 [appear, disappear] 内可见，窗口外缩到 0.001（等于不可见）。

    `fade_out=False` 用于结尾落版：不能再插一个 tiny 关键帧，否则它和
    "保持到最后一帧"的关键帧撞在同一个 frame 上，Bezier 会在结束前十来帧
    就把字缩没（表现为结尾文字忽然变成一条细线）。
    """
    tiny = (0.001, 0.001, 0.001)
    full = (1.0, 1.0, 1.0)

    obj.scale = tiny
    obj.keyframe_insert("scale", frame=FRAME_START)
    obj.keyframe_insert("scale", frame=max(FRAME_START, appear - fade))

    obj.scale = full
    obj.keyframe_insert("scale", frame=appear)
    obj.keyframe_insert("scale", frame=disappear)

    if fade_out:
        obj.scale = tiny
        obj.keyframe_insert("scale", frame=min(FRAME_END, disappear + fade))


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
            constraint.track_axis = "TRACK_Z"      # 文字正面（+Z）朝相机
            constraint.up_axis = "UP_Y"


def build_camera():
    camera_data = bpy.data.cameras.new("Camera")
    camera_data.lens = 52
    camera = bpy.data.objects.new("Camera", camera_data)
    bpy.context.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    target = bpy.data.objects.new("CameraTarget", None)
    bpy.context.collection.objects.link(target)
    target.location = (0, 0, 6.0)
    target.keyframe_insert("location", frame=FRAME_START)
    target.location = (0, 0, 8.4)
    target.keyframe_insert("location", frame=110)
    target.location = (0, 0, 8.2)
    target.keyframe_insert("location", frame=FRAME_END)

    constraint = camera.constraints.new("TRACK_TO")
    constraint.target = target
    constraint.track_axis = "TRACK_NEGATIVE_Z"
    constraint.up_axis = "UP_Y"

    # 五段相机运动：低角度推进 → 环绕 → 拉高收尾
    # 距离是重点：文字是世界空间的，离得远就必然小。
    # 每一章都让相机停在"该章内容刚好占满画面"的位置。
    keys = [
        (FRAME_START, (34.0, -50.0, 12.0)),
        (34, (19.0, -33.0, 11.5)),     # 标题章，距离 ~40
        (110, (-7.0, -30.0, 14.5)),    # 距离 ~35
        (160, (-16.0, -38.0, 19.0)),   # API 章，距离 ~45
        (205, (3.0, -44.0, 22.0)),     # 全景
        (262, (0.0, -32.0, 16.0)),     # 第二屏 API，距离 ~37
        (306, (0.0, -26.0, 15.0)),     # 代码章，距离 ~28
        (FRAME_END, (0.0, -15.0, 13.5)), # 落版，距离 ~17，保留 ~17° 俯角
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
    build_lights()
    camera = build_camera()          # 文字要朝相机，所以先建相机
    build_text_block(camera)

    # 回到第一帧，打开时看到的是开头
    bpy.context.scene.frame_set(FRAME_START)

    blend_path = os.path.join(OUT_DIR, "byta-promo.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    log(f"已保存 {blend_path}")
    log(f"文件大小 {os.path.getsize(blend_path) / 1024:.0f} KB")


if __name__ == "__main__":
    main()
