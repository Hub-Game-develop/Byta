#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""
Byta 宣传片（电影感版）—— 纯黑、像素字、碎裂魔方。

分镜：
  f1–f47    纯黑 + 一行 Unifont 像素字，逐字符打出
  f48       突然亮起：碎片从远处爆现
  f48–f74   碎片极速收拢成"微微分离"的魔方
  f72–f240  魔方在屏幕中心极速旋转（三轴倾斜、碎块各自自转）
  f84–f180  字幕：No Chromium. No Sluggish UI. Just native speed.
  f216–f238 终端风：dotnet add package Byta.GL
  f240–f264 黑屏

用法：
    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build_cinematic.py
"""

from __future__ import annotations

import math
import random

import bpy
from mathutils import Euler, Vector

# ---------------------------------------------------------------- 配置
HERE = "/Users/cangcang/WorkBuddy/2026-10-23-07-10-54/byta/promo/cinematic"
BLEND_PATH = f"{HERE}/byta-cinematic.blend"
AUDIO_PATH = f"{HERE}/OverWorld-Infinity.wav"

FPS = 24
FRAME_START = 1
FRAME_END = 264          # 11 秒
IMPACT = 48              # 2.0 秒：亮起
SETTLE = 74              # 碎片收拢完成
BLACKOUT = 246           # 10.25 秒：黑屏

FONT_PIXEL = "/Users/cangcang/Library/Fonts/unifont-18.0.01.otf"
FONT_MONO = "/System/Library/Fonts/SFNSMono.ttf"

RES_X, RES_Y = 1920, 1080
SAMPLES = 48

SHARDS_PER_AXIS = 4      # 4×4×4 = 64 块
CUBE_SIZE = 2.2
GAP = 0.014              # 拼合时碎片间的细缝

CYAN = (0.32, 0.72, 1.0)
MAGENTA = (1.0, 0.22, 0.55)
WHITE = (0.95, 0.97, 1.0)

# 全局引用的动画曲线（最后统一设插值模式）
_curves: list[tuple[object, str]] = []


def log(message: str):
    print(f"[byta-cine] {message}")


# ---------------------------------------------------------------- 基础工具
def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.lights,
                  bpy.data.cameras, bpy.data.curves, bpy.data.node_groups):
        for item in list(block):
            try:
                block.remove(item)
            except Exception:                                # noqa: BLE001
                pass


def iter_fcurves(action):
    """兼容 4.x（action.fcurves）与 5.x（slotted action）。"""
    if action is None:
        return
    if hasattr(action, "fcurves"):
        yield from action.fcurves
        return
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                yield from bag.fcurves


def set_interpolation(obj, mode: str, match: str | None = None):
    """把对象的动画曲线插值改成指定模式（可只改某条路径）。"""
    if obj.animation_data is None or obj.animation_data.action is None:
        return
    for fcurve in iter_fcurves(obj.animation_data.action):
        if match and match not in fcurve.data_path:
            continue
        for key in fcurve.keyframe_points:
            key.interpolation = mode


def load_font(path: str):
    try:
        return bpy.data.fonts.load(path, check_existing=True)
    except Exception as error:                               # noqa: BLE001
        log(f"字体加载失败 {path}: {error}")
        return None


# ---------------------------------------------------------------- 材质
def make_shard_material():
    """
    深色金属 + 菲涅耳边缘发光。

    边缘发光是"碎裂感"的关键：64 个碎块旋转时，每块棱边的亮线扫过，
    比整体给一个发光强度要立体得多。
    """
    mat = bpy.data.materials.new("Shard")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links

    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.022, 0.034, 0.058, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.62
    bsdf.inputs["Roughness"].default_value = 0.125
    bsdf.inputs["Emission Color"].default_value = (*CYAN, 1.0)

    # 菲涅耳：视线越掠过表面，发光越强
    layer = nodes.new("ShaderNodeLayerWeight")
    layer.inputs["Blend"].default_value = 0.30
    layer.location = (-620, -120)

    power = nodes.new("ShaderNodeMath")
    power.operation = "POWER"
    power.inputs[1].default_value = 2.1
    power.location = (-440, -120)

    strength = nodes.new("ShaderNodeMath")
    strength.operation = "MULTIPLY"
    strength.inputs[1].default_value = 2.45
    strength.location = (-260, -120)

    links.new(layer.outputs["Facing"], power.inputs[0])
    links.new(power.outputs[0], strength.inputs[0])
    links.new(strength.outputs[0], bsdf.inputs["Emission Strength"])

    # 内部一点冷光，让碎块在背光面也不至于全黑
    bsdf.inputs["Emission Color"].default_value = (0.10, 0.35, 0.85, 1.0)
    return mat


def make_emission_material(name: str, color, strength: float):
    """
    纯 Emission 节点，不用 Principled。

    Principled 即使把 Base Color 刷成纯黑，默认的镜面反射仍会把灯光"接"到物体上，
    于是 emission=0 的文字依然会隐隐浮出来。Emission 节点没有反射项，
    强度 0 就等于完全不可见 —— 在纯黑背景上这正是我们需要的开关。
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    for node in list(nodes):
        nodes.remove(node)

    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (*color, 1.0)
    emission.inputs["Strength"].default_value = strength
    emission.name = "Emission"

    links.new(emission.outputs[0], output.inputs["Surface"])
    return mat


def make_volume_material(density: float = 0.014, anisotropy: float = 0.55):
    """体积雾：只有强光附近才看得见，给"超强光影"提供光柱与空气感。"""
    mat = bpy.data.materials.new("Fog")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    for node in list(nodes):
        nodes.remove(node)

    output = nodes.new("ShaderNodeOutputMaterial")
    scatter = nodes.new("ShaderNodeVolumeScatter")
    scatter.inputs["Color"].default_value = (0.55, 0.70, 0.95, 1.0)
    scatter.inputs["Density"].default_value = density
    scatter.inputs["Anisotropy"].default_value = anisotropy

    links.new(scatter.outputs[0], output.inputs["Volume"])
    return mat


# ---------------------------------------------------------------- 文字
def build_text(name: str, body: str, font, size: float, location,
               color, strength: float, align: str = "CENTER", extrude: float = 0.0):
    curve = bpy.data.curves.new(name, type="FONT")
    curve.body = body
    if font is not None:
        curve.font = font
    curve.size = size
    curve.align_x = align
    curve.extrude = extrude
    curve.resolution_u = 2          # 像素字不需要细分

    obj = bpy.data.objects.new(name, curve)
    obj.location = location
    obj.rotation_euler = (math.radians(90), 0, 0)   # 立在 XZ 平面、正面朝 -Y
    obj.data.materials.append(make_emission_material(f"{name}Mat", color, strength))
    bpy.context.collection.objects.link(obj)
    return obj


def set_visible_window(obj, start: int, end: int):
    """
    在 [start, end] 之外把对象缩到 0.001。

    只把 emission 归零是不够的：几何体仍然不透明，会在魔方前面留下一块
    黑色字形（实测就是这样 —— 远看像脏点，放大才看出是字）。缩到 0.001
    比 hide_render 更简单，也不需要额外处理依赖图。
    """
    tiny = (0.001, 0.001, 0.001)
    obj.scale = tiny
    obj.keyframe_insert("scale", frame=FRAME_START)
    obj.keyframe_insert("scale", frame=max(FRAME_START, start - 1))
    obj.scale = (1.0, 1.0, 1.0)
    obj.keyframe_insert("scale", frame=start)
    obj.keyframe_insert("scale", frame=end)
    obj.scale = tiny
    obj.keyframe_insert("scale", frame=min(FRAME_END, end + 1))
    set_interpolation(obj, "CONSTANT", match="scale")


def animate_emission(obj, keys: list[tuple[int, float]], color_strength_index: int = 0):
    """
    直接对材质的 Emission Strength 打关键帧 —— 纯黑背景上，
    强度 0 就等于不可见，不需要处理 alpha 混合模式，最稳。
    """
    material = obj.data.materials[color_strength_index]
    socket = material.node_tree.nodes["Emission"].inputs["Strength"]

    # 两件事必须做，否则"还没到时间"的对象会提前浮出来：
    #   1) 第一个关键帧之前，Blender 会沿用材质创建时的默认亮度 → 补起始 0 键；
    #   2) 默认 BEZIER 插值在 (1, 0) 和 (238, 3.0) 这种长间隔上会拱起来
    #      —— 实测第 140 帧已经爬到 1.89，屏幕上就浮出了暗字。
    #      所以要在第一个非零键的**前一帧**再插一个 0，把这段压平。
    keys = sorted(keys)
    if not keys or keys[0][0] > FRAME_START:
        first = keys[0][0] if keys else FRAME_START
        keys = [(FRAME_START, 0.0), (max(FRAME_START, first - 1), 0.0), *keys]

    for frame, value in keys:
        socket.default_value = value
        socket.keyframe_insert("default_value", frame=frame)


def measure_char_width(font, size: float, sample: str = "M") -> float:
    """等宽字体：量一个字符的 advance，用来逐字排版。"""
    probe = build_text("__probe", sample, font, size, (0, 0, 0), (0, 0, 0), 0.0,
                       align="LEFT")
    bpy.context.view_layer.update()
    width = probe.dimensions.x
    bpy.data.objects.remove(probe, do_unlink=True)
    return width


def build_typewriter_line(prefix: str, text: str, font, size: float, origin,
                          color, strength: float, start_frame: int, step: int = 1):
    """
    逐字符点亮。

    Blender 5.1 里 Text 的 body 属性不可打关键帧（"property body not animatable"），
    所以不能用"改 body"做打字机。改成每个字符一个对象、各自控制发光强度 ——
    效果更好：可以做出终端那种"刚写下的字偏亮、随后回落"的呼吸感。
    """
    width = measure_char_width(font, size)
    total = len(text) * width
    x0 = origin[0] - total / 2.0

    characters = []
    for index, char in enumerate(text):
        if char == " ":
            continue
        obj = build_text(f"{prefix}{index:02d}", char, font, size,
                         (x0 + (index + 0.5) * width, origin[1], origin[2]),
                         color, strength, align="CENTER")
        animate_emission(obj, [
            (start_frame + index * step - 1, 0.0),
            (start_frame + index * step, strength * 1.9),   # 刚出现时过曝一下
            (start_frame + index * step + 6, strength),     # 再回落到常态
        ])
        characters.append(obj)

    return characters, width, total


# ---------------------------------------------------------------- 镜头
def build_cameras():
    # 第一段：正交，像素字完全正对，没有透视变形
    intro_data = bpy.data.cameras.new("CamIntro")
    intro_data.type = "ORTHO"
    intro_data.ortho_scale = 9.0
    intro = bpy.data.objects.new("CamIntro", intro_data)
    intro.location = (0.0, -12.0, 0.0)
    intro.rotation_euler = (math.radians(90), 0.0, 0.0)
    bpy.context.collection.objects.link(intro)

    # 主镜头：52mm + 景深
    main_data = bpy.data.cameras.new("CamMain")
    main_data.lens = 52.0
    main_data.dof.use_dof = True
    main_data.dof.aperture_fstop = 4.0
    main = bpy.data.objects.new("CamMain", main_data)
    bpy.context.collection.objects.link(main)

    focus = bpy.data.objects.new("Focus", None)
    focus.location = (0.0, 0.0, 0.0)
    bpy.context.collection.objects.link(focus)
    main_data.dof.focus_object = focus

    # 用 Track To 对准原点，而不是手算 euler —— 手算容易差几度，
    # 表现就是魔方偏在画面一角（第一版就是这样）。
    track = main.constraints.new("TRACK_TO")
    track.target = focus
    track.track_axis = "TRACK_NEGATIVE_Z"
    track.up_axis = "UP_Y"

    scene = bpy.context.scene
    scene.timeline_markers.new("intro", frame=FRAME_START).camera = intro
    scene.timeline_markers.new("impact", frame=IMPACT).camera = main
    scene.camera = main

    # 16:9 下限制构图的是**垂直**视野：52mm / 36mm 传感器 → 垂直半角约 10.8°，
    # 所以"距离 7.4"的实际可视高度只有 2.82，正好被 2.8 的立方体贴满。
    # 想在旋转到对角线（4.85）时还留边，得在 12 左右。
    keys = [
        (IMPACT, (0.0, -12.2, 2.50)),
        (150, (-0.9, -11.5, 2.30)),
        (BLACKOUT, (0.7, -10.4, 2.05)),
    ]
    for frame, location in keys:
        main.location = location
        main.keyframe_insert("location", frame=frame)

    return intro, main


# ---------------------------------------------------------------- 碎裂魔方
def make_cube_mesh(size: float):
    mesh = bpy.data.meshes.new("ShardMesh")
    half = size / 2.0
    vertices = [
        (-half, -half, -half), (half, -half, -half), (half, half, -half), (-half, half, -half),
        (-half, -half, half), (half, -half, half), (half, half, half), (-half, half, half),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    return mesh


def build_shattered_cube():
    """
    64 块碎片拼成一个立方体。

    不用真正的不规则切割：视觉上的"碎裂"来自
      1) 碎片各自非均匀缩放 + 独立自转，缝隙宽窄不一
      2) 菲涅耳边缘发光在棱线上扫过
      3) 体积雾 + Glare 让缝隙透出的光溢出
    真正决定观感的不是几何复杂度，而是这三点。
    """
    material = make_shard_material()
    mesh = make_cube_mesh(CUBE_SIZE / SHARDS_PER_AXIS - GAP)

    root = bpy.data.objects.new("CubeRoot", None)
    bpy.context.collection.objects.link(root)

    rng = random.Random(20261024)
    step = CUBE_SIZE / SHARDS_PER_AXIS
    offset = (SHARDS_PER_AXIS - 1) / 2.0

    shards = []
    for ix in range(SHARDS_PER_AXIS):
        for iy in range(SHARDS_PER_AXIS):
            for iz in range(SHARDS_PER_AXIS):
                position = Vector((
                    (ix - offset) * step,
                    (iy - offset) * step,
                    (iz - offset) * step,
                ))

                obj = bpy.data.objects.new(f"Shard_{ix}{iy}{iz}", mesh)
                obj.data.materials.append(material)
                bpy.context.collection.objects.link(obj)
                obj.parent = root

                # 非均匀缩放：缝隙不均匀，看起来才是"碎"的而不是"整齐的方块"
                obj.scale = (
                    rng.uniform(0.86, 1.0),
                    rng.uniform(0.86, 1.0),
                    rng.uniform(0.86, 1.0),
                )

                # 爆散方向：从中心向外；中心附近的块给随机方向，避免速度为 0
                direction = position.normalized() if position.length > 1e-4 else Vector((
                    rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized()
                distance = rng.uniform(2.6, 6.2)
                scatter = position + direction * distance + Vector((
                    rng.uniform(-1.2, 1.2), rng.uniform(-1.2, 1.2), rng.uniform(-1.2, 1.2)))

                # 收拢后停在"微微分离 + 高低错落"的位置。
                # 如果 64 块都精确贴合，表面是一个完美平面，光打上去均匀一片，
                # 看起来就是个蓝盒子；加一点径向/切向偏移，面才有碎的光影。
                settled = (position * 1.055
                           + direction * rng.uniform(-0.02, 0.055)
                           + Vector((rng.uniform(-0.03, 0.03),
                                     rng.uniform(-0.03, 0.03),
                                     rng.uniform(-0.03, 0.03))))

                # 收拢后只允许极小漂移：真正的大角度旋转由父级承担。
                # 每块自己持续大角度乱转的话，立方体轮廓会彻底散掉。
                drift = Vector((
                    math.radians(rng.uniform(-2.5, 2.5)),
                    math.radians(rng.uniform(-2.5, 2.5)),
                    math.radians(rng.uniform(-2.5, 2.5))))
                start_rotation = Euler((
                    rng.uniform(-math.pi, math.pi),
                    rng.uniform(-math.pi, math.pi),
                    rng.uniform(-math.pi, math.pi)))

                shards.append((obj, scatter, settled, start_rotation, drift))

    # ---- 时序：先全部不可见，第 IMPACT 帧炸现，然后收拢
    for obj, scatter, settled, start_rotation, drift in shards:
        obj.scale = (0.001, 0.001, 0.001)
        obj.keyframe_insert("scale", frame=FRAME_START)
        obj.keyframe_insert("scale", frame=IMPACT - 1)
        obj.scale = (1.0, 1.0, 1.0)
        obj.keyframe_insert("scale", frame=IMPACT)

        obj.location = scatter
        obj.keyframe_insert("location", frame=IMPACT)
        obj.location = settled
        obj.keyframe_insert("location", frame=SETTLE)

        # 爆散时随机朝向 → 收拢时必须精确归零，让 64 块拼回立方体
        obj.rotation_euler = start_rotation
        obj.keyframe_insert("rotation_euler", frame=IMPACT)
        obj.rotation_euler = (0.0, 0.0, 0.0)
        obj.keyframe_insert("rotation_euler", frame=SETTLE)
        # 收拢后只做极缓慢的微漂移（±2.5°），既不呆板也不破坏轮廓
        obj.rotation_euler = Euler(drift)
        obj.keyframe_insert("rotation_euler", frame=BLACKOUT)

        # 收拢用 LINEAR（急速、无缓动，才有"被吸回去"的力量感）；自转也 LINEAR
        set_interpolation(obj, "LINEAR", match="location")
        set_interpolation(obj, "LINEAR", match="rotation_euler")

    # ---- 整体极速旋转
    root.rotation_euler = (0.0, 0.0, 0.0)
    root.keyframe_insert("rotation_euler", frame=IMPACT)
    # 三轴都给速度，合成一个倾斜的旋转轴 —— 比单纯绕 Z 转更像"失控"
    root.rotation_euler = (
        math.radians(38.0),
        math.radians(-24.0),
        math.radians(1240.0),      # 约 3.4 圈：极速旋转
    )
    root.keyframe_insert("rotation_euler", frame=BLACKOUT)
    set_interpolation(root, "LINEAR", match="rotation_euler")

    # ---- 退场：结尾要"打出一行代码"，魔方不能继续抢戏。
    # 让它收缩进中心消失（像被吸走），代码才落在纯黑背景上。
    root.scale = (1.0, 1.0, 1.0)
    root.keyframe_insert("scale", frame=204)
    root.scale = (0.001, 0.001, 0.001)
    root.keyframe_insert("scale", frame=216)
    set_interpolation(root, "LINEAR", match="scale")
    set_interpolation(root, "LINEAR", match="scale")

    log(f"碎片：{len(shards)} 块，半径 {CUBE_SIZE / 2:.2f}")
    return root, material


# ---------------------------------------------------------------- 灯光 / 环境
def build_lights():
    def area(name, location, rotation, color, energy, size):
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = energy
        data.color = color
        data.size = size
        obj = bpy.data.objects.new(name, data)
        obj.location = location
        obj.rotation_euler = rotation
        bpy.context.collection.objects.link(obj)
        return obj

    # 主光：青蓝，从右上斜打，负责金属高光
    area("Key", (5.4, -5.2, 6.4), (math.radians(48), 0, math.radians(46)),
         CYAN, 22000.0, 1.0)
    # 副光：品红，从左下补，制造冷暖对撞
    area("Fill", (-5.2, -5.0, 1.8), (math.radians(84), 0, math.radians(-52)),
         MAGENTA, 5200.0, 2.2)
    # 轮廓光：白色，从背后打，勾出碎块边缘
    area("Rim", (0.4, 5.8, 3.6), (math.radians(125), 0, math.radians(4)),
         WHITE, 12000.0, 1.0)

    # 核心光：在魔方内部，缝隙里透出来的就是它
    core_data = bpy.data.lights.new("Core", type="POINT")
    core_data.energy = 520.0
    core_data.color = (0.55, 0.86, 1.0)
    core_data.shadow_soft_size = 0.6
    core = bpy.data.objects.new("Core", core_data)
    bpy.context.collection.objects.link(core)

    # 亮起瞬间：核心光爆闪一下再回落
    core_data.energy = 40.0
    core_data.keyframe_insert("energy", frame=IMPACT - 1)
    core_data.energy = 2400.0
    core_data.keyframe_insert("energy", frame=IMPACT + 2)
    core_data.energy = 620.0
    core_data.keyframe_insert("energy", frame=IMPACT + 28)

    # 体积雾已移除：试过 26 单位 / 密度 0.014，整个背景被散射成蓝紫渐变，
    # 与"纯黑背景"直接冲突。光影层次改由硬光源 + Glare + 缝隙透光承担。

    return core


def setup_world():
    world = bpy.data.worlds.new("Black")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.0, 0.0, 0.0, 1.0)
    background.inputs["Strength"].default_value = 0.0
    bpy.context.scene.world = world


def setup_compositor():
    """Blender 5.x：合成器是 scene.compositing_node_group + NodeGroupOutput。"""
    scene = bpy.context.scene
    group = bpy.data.node_groups.new("BytaCine", "CompositorNodeTree")
    group.interface.new_socket(name="Image", in_out="OUTPUT", socket_type="NodeSocketColor")
    scene.compositing_node_group = group
    scene.use_nodes = True

    nodes, links = group.nodes, group.links
    layers = nodes.new("CompositorNodeRLayers")
    glare = nodes.new("CompositorNodeGlare")
    output = nodes.new("NodeGroupOutput")

    # Fog Glow 比 Bloom 更柔，适合"强光影"；阈值压低让金属高光也参与
    for name, value in (("Type", "Fog Glow"), ("Threshold", 0.62), ("Size", 0.92)):
        if name in glare.inputs:
            try:
                glare.inputs[name].default_value = value
            except Exception:                                # noqa: BLE001
                pass

    links.new(layers.outputs["Image"], glare.inputs["Image"])
    links.new(glare.outputs["Image"], output.inputs[0])

    # 第一段（纯黑 + 像素字）不该有光晕：细笔画一旦被 Glare 糊开就成"描边"了。
    # 用阈值做动画，亮起那一帧才真正开启。
    threshold = glare.inputs.get("Threshold")
    if threshold is not None:
        threshold.default_value = 8.0
        threshold.keyframe_insert("default_value", frame=FRAME_START)
        threshold.keyframe_insert("default_value", frame=IMPACT - 1)
        threshold.default_value = 0.55
        threshold.keyframe_insert("default_value", frame=IMPACT)

    log("合成器：Glare Fog Glow（阈值随亮起开启）")


def setup_render():
    scene = bpy.context.scene
    # 引擎标识在 4.2 叫 BLENDER_EEVEE_NEXT，5.1 又改回 BLENDER_EEVEE —— 不写死
    engines = {item.identifier for item in
               bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        if candidate in engines:
            scene.render.engine = candidate
            break
    else:
        raise RuntimeError(f"没有 EEVEE 引擎可用：{sorted(engines)}")
    scene.render.resolution_x = RES_X
    scene.render.resolution_y = RES_Y
    scene.render.resolution_percentage = 100
    scene.render.fps = FPS
    scene.frame_start = FRAME_START
    scene.frame_end = FRAME_END

    eevee = scene.eevee
    for name, value in (("taa_render_samples", SAMPLES), ("use_gtao", True),
                        # raytracing 关掉：屏幕空间反射会把字幕"印"到金属面上，
                        # 而且高光会被模糊。纯黑环境 + 硬光源才是要的"超强光影"。
                        ("use_raytracing", False), ("use_shadows", True)):
        if hasattr(eevee, name):
            try:
                setattr(eevee, name, value)
            except Exception:                                # noqa: BLE001
                pass

    # 运动模糊：极速旋转必须有它，否则碎块看起来是"跳帧"
    scene.render.use_motion_blur = True
    if hasattr(scene.render, "motion_blur_shutter"):
        scene.render.motion_blur_shutter = 0.55

    scene.view_settings.view_transform = "AgX"
    for look in ("AgX - Punchy", "AgX - Medium High Contrast", "None"):
        try:
            scene.view_settings.look = look
            break
        except Exception:                                    # noqa: BLE001
            continue

    # 输出：PNG 序列（音频由 make_cinematic.sh 里的 ffmpeg 混入）
    settings = scene.render.image_settings
    settings.media_type = "IMAGE"
    settings.file_format = "PNG"
    settings.color_mode = "RGB"
    settings.compression = 15
    scene.render.filepath = f"{HERE}/frames/frame_"


# ---------------------------------------------------------------- 文字层
def build_text_layers(camera):
    pixel = load_font(FONT_PIXEL)
    mono = load_font(FONT_MONO)
    if pixel is None:
        log("警告：Unifont 未加载，像素感会丢失")

    headline_text = "Zero-Overhead. OpenGL 4.6. .NET 10."

    # ---- 1. 开头：一行像素字，逐字符打出（正交相机，中心在原点）
    characters, char_width, total_width = build_typewriter_line(
        "Head", headline_text, pixel, 0.46, (0.0, 0.0, 0.0),
        (0.88, 0.95, 1.0), 1.9, start_frame=3, step=1)
    log(f"标题：{len(characters)} 个字符，字宽 {char_width:.3f}，总宽 {total_width:.2f}")

    # 停留到亮起前，然后整行熄灭（逐字熄灭，像断电）
    # 所有字符必须在 IMPACT(48) 之前归零。之前写成 index//3，最后几个字符
    # 拖到 54 帧才灭，结果亮起时屏幕上还挂着半行字。
    for obj in characters:
        animate_emission(obj, [(45, 1.9), (47, 0.0)])
        set_visible_window(obj, FRAME_START, IMPACT - 1)

    # ---- 2. 字幕：整行淡入淡出
    subtitle = build_text("Subtitle",
                          "No Chromium. No Sluggish UI. Just native speed.",
                          # y=-3.0：放在魔方（半径 1.4）前方，旋转到任何角度都不会被挡
                          pixel, 0.165, (0.0, -3.0, -0.62), (0.80, 0.90, 1.0), 2.0)
    bpy.context.view_layer.update()
    log(f"字幕宽度 {subtitle.dimensions.x:.2f}")
    animate_emission(subtitle, [(84, 0.0), (100, 2.0), (164, 2.0), (180, 0.0)])
    set_visible_window(subtitle, 80, 186)

    # ---- 3. 结尾：终端命令，逐字符打出
    command_text = "dotnet add package Byta.GL"
    command_chars, command_width, _ = build_typewriter_line(
        "Cmd", command_text, mono, 0.30, (0.22, -3.0, -0.44),
        (0.90, 0.96, 1.0), 2.6, start_frame=212, step=1)
    for obj in command_chars:
        set_visible_window(obj, 208, BLACKOUT - 1)
    log(f"命令：{len(command_chars)} 个字符")

    # 提示符：命令左侧，稍亮一点
    prompt_width = measure_char_width(mono, 0.30)
    prompt = build_text("Prompt", "$", mono, 0.30,
                        (0.22 - (len(command_text) * command_width) / 2.0 - prompt_width * 0.95,
                         -3.0, -0.52), CYAN, 3.2, align="CENTER")
    animate_emission(prompt, [(210, 0.0), (212, 3.2)])
    set_visible_window(prompt, 208, BLACKOUT - 1)

    # 光标：命令末尾，固定闪烁
    cursor = build_text("Cursor", "_", mono, 0.30,
                        (0.22 - (len(command_text) * command_width) / 2.0
                         + len(command_text) * command_width * 0.98,
                         -3.0, -0.52), CYAN, 3.0, align="CENTER")
    keys = []
    for frame in range(232, BLACKOUT, 10):
        keys += [(frame, 3.0), (frame + 5, 0.0)]
    if keys:
        animate_emission(cursor, keys)
    set_visible_window(cursor, 208, BLACKOUT - 1)

    log("文字层就绪")


def main():
    clear_scene()
    setup_render()
    setup_world()
    setup_compositor()
    build_lights()
    _, main_camera = build_cameras()
    build_shattered_cube()
    build_text_layers(main_camera)

    bpy.context.scene.frame_set(1)

    # 音频条：放进 VSE 方便在 Blender 里预览/手动合成（不影响这里的渲染）
    try:
        scene = bpy.context.scene
        editor = scene.sequence_editor_create()
        # 5.x 把 sequences 改名成 strips，两个名字都试一下
        collection = getattr(editor, "strips", None) or editor.sequences
        collection.new_sound("OverWorld-Infinity", AUDIO_PATH, channel=1, frame_start=1)
        log("音轨已放进 VSE（渲染仍走 3D，音频由 ffmpeg 合成）")
    except Exception as error:                               # noqa: BLE001
        log(f"音轨加入失败（不影响画面）：{error}")

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    log(f"已保存 {BLEND_PATH}")


if __name__ == "__main__":
    main()
