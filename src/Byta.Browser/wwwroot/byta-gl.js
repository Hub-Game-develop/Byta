// SPDX-License-Identifier: MIT
//
// Byta 的 WebGL2 后端。
//
// 设计要点：
//   1. WebGL 的对象（Buffer/Shader/Program/Texture/Framebuffer/UniformLocation）是 JS 对象，
//      而 Byta 的 API 用 uint 句柄 —— 所以这里维护一张句柄表，1 起编号，0 表示 0 号对象。
//   2. .NET 传进来的是 MemoryView（`{ data: Uint8Array, length }`），按目标类型重解释成
//      Uint32Array / Int32Array / Float32Array，零拷贝。
//   3. 这一层不含任何 GL 语义判断，只做翻译；句柄失效由调用方负责。

let gl = null;

/** handle -> WebGL 对象（1 起；0 号位置留空表示"无对象"） */
const objects = [null];

function reg(object) {
    if (!object) return 0;
    const handle = objects.length;
    objects.push(object);
    return handle;
}

function obj(handle) {
    return handle > 0 && handle < objects.length ? objects[handle] : null;
}

function drop(handle) {
    if (handle > 0 && handle < objects.length) objects[handle] = null;
}

// ------------------------------------------------------------------ MemoryView
function byteLength(view) {
    // .NET 的 MemoryView 同时给出 data(Uint8Array) 和 length(元素数)
    return view.length !== undefined ? view.length : view.data.length;
}

function asU32(view) {
    return new Uint32Array(view.data.buffer, view.data.byteOffset, byteLength(view) >> 2);
}

function asI32(view) {
    return new Int32Array(view.data.buffer, view.data.byteOffset, byteLength(view) >> 2);
}

function asF32(view) {
    return new Float32Array(view.data.buffer, view.data.byteOffset, byteLength(view) >> 2);
}

// ------------------------------------------------------------------ 上下文
export function attach(canvasId) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return 0;

    gl = canvas.getContext('webgl2', {
        alpha: false,
        antialias: true,
        depth: true,
        stencil: false,
        preserveDrawingBuffer: true,   // 为了能 readPixels / 截图
        powerPreference: 'high-performance',
    });
    return gl ? 1 : 0;
}

export function info() {
    if (!gl) return '未初始化';
    const debug = gl.getExtension('WEBGL_debug_renderer_info');
    const renderer = debug
        ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL)
        : gl.getParameter(gl.RENDERER);
    const vendor = debug
        ? gl.getParameter(debug.UNMASKED_VENDOR_WEBGL)
        : gl.getParameter(gl.VENDOR);
    return `${renderer} / ${vendor}`;
}

export function setupViewport(width, height, pixelRatio) {
    const canvas = gl.canvas;
    const ratio = pixelRatio > 0 ? pixelRatio : (globalThis.devicePixelRatio || 1);
    const cssWidth = width > 0 ? width : (canvas.clientWidth || 640);
    const cssHeight = height > 0 ? height : (canvas.clientHeight || 360);

    canvas.width = Math.max(1, Math.round(cssWidth * ratio));
    canvas.height = Math.max(1, Math.round(cssHeight * ratio));
    gl.viewport(0, 0, canvas.width, canvas.height);
}

// ------------------------------------------------------------------ 状态
export function enable(capability) { gl.enable(capability); }
export function disable(capability) { gl.disable(capability); }
export function isEnabled(capability) { return gl.isEnabled(capability) ? 1 : 0; }
export function depthFunc(func) { gl.depthFunc(func); }
export function depthMask(flag) { gl.depthMask(!!flag); }
export function cullFace(mode) { gl.cullFace(mode); }
export function frontFace(mode) { gl.frontFace(mode); }
export function blendFunc(source, destination) { gl.blendFunc(source, destination); }

export function blendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha) {
    gl.blendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
}

export function colorMask(r, g, b, a) { gl.colorMask(!!r, !!g, !!b, !!a); }
export function viewport(x, y, width, height) { gl.viewport(x, y, width, height); }
export function scissor(x, y, width, height) { gl.scissor(x, y, width, height); }
export function clear(mask) { gl.clear(mask); }
export function clearColor(r, g, b, a) { gl.clearColor(r, g, b, a); }
export function clearDepth(depth) { gl.clearDepth(depth); }
export function clearStencil(value) { gl.clearStencil(value); }
export function pixelStorei(pname, value) { gl.pixelStorei(pname, value); }
export function lineWidth(width) { gl.lineWidth(width); }
export function polygonOffset(factor, units) { gl.polygonOffset(factor, units); }

// ------------------------------------------------------------------ 缓冲
export function genBuffers(ids) {
    const dst = asU32(ids);
    for (let i = 0; i < dst.length; i++) dst[i] = reg(gl.createBuffer());
}

export function deleteBuffers(ids) {
    const src = asU32(ids);
    for (let i = 0; i < src.length; i++) {
        gl.deleteBuffer(obj(src[i]));
        drop(src[i]);
    }
}

export function bindBuffer(target, buffer) { gl.bindBuffer(target, obj(buffer)); }

export function bufferData(target, size, data, usage) {
    if (byteLength(data) === 0) gl.bufferData(target, size, usage);
    else gl.bufferData(target, data.data, usage);
}

export function bufferSubData(target, offset, size, data) {
    gl.bufferSubData(target, offset, data.data);
}

// ------------------------------------------------------------------ VAO
export function genVertexArrays(ids) {
    const dst = asU32(ids);
    for (let i = 0; i < dst.length; i++) dst[i] = reg(gl.createVertexArray());
}

export function deleteVertexArrays(ids) {
    const src = asU32(ids);
    for (let i = 0; i < src.length; i++) {
        gl.deleteVertexArray(obj(src[i]));
        drop(src[i]);
    }
}

export function bindVertexArray(array) { gl.bindVertexArray(obj(array)); }
export function enableVertexAttribArray(index) { gl.enableVertexAttribArray(index); }
export function disableVertexAttribArray(index) { gl.disableVertexAttribArray(index); }

export function vertexAttribPointer(index, size, type, normalized, stride, offset) {
    gl.vertexAttribPointer(index, size, type, !!normalized, stride, offset);
}

export function vertexAttribIPointer(index, size, type, stride, offset) {
    gl.vertexAttribIPointer(index, size, type, stride, offset);
}

export function vertexAttribDivisor(index, divisor) { gl.vertexAttribDivisor(index, divisor); }

// ------------------------------------------------------------------ 着色器
export function createShader(type) { return reg(gl.createShader(type)); }
export function shaderSource(shader, source) { gl.shaderSource(obj(shader), source); }
export function compileShader(shader) { gl.compileShader(obj(shader)); }

export function getShaderiv(shader, pname, output) {
    asI32(output)[0] = gl.getShaderParameter(obj(shader), pname) | 0;
}

export function getShaderInfoLog(shader) {
    return gl.getShaderInfoLog(obj(shader)) || '';
}

export function deleteShader(shader) {
    gl.deleteShader(obj(shader));
    drop(shader);
}

// ------------------------------------------------------------------ 程序
export function createProgram() { return reg(gl.createProgram()); }
export function attachShader(program, shader) { gl.attachShader(obj(program), obj(shader)); }
export function detachShader(program, shader) { gl.detachShader(obj(program), obj(shader)); }
export function linkProgram(program) { gl.linkProgram(obj(program)); }
export function useProgram(program) { gl.useProgram(obj(program)); }

export function deleteProgram(program) {
    gl.deleteProgram(obj(program));
    drop(program);
}

export function getProgramiv(program, pname, output) {
    asI32(output)[0] = gl.getProgramParameter(obj(program), pname) | 0;
}

export function getProgramInfoLog(program) {
    return gl.getProgramInfoLog(obj(program)) || '';
}

export function getAttribLocation(program, name) {
    return gl.getAttribLocation(obj(program), name);
}

export function getUniformLocation(program, name) {
    const location = gl.getUniformLocation(obj(program), name);
    // 不存在的 uniform 返回 null，Byta 侧约定为 -1
    return location === null ? -1 : reg(location);
}

export function uniformBlockBinding(program, blockIndex, binding) {
    gl.uniformBlockBinding(obj(program), blockIndex, binding);
}

// ------------------------------------------------------------------ uniform
export function uniform1f(location, v0) { gl.uniform1f(obj(location), v0); }
export function uniform2f(location, v0, v1) { gl.uniform2f(obj(location), v0, v1); }
export function uniform3f(location, v0, v1, v2) { gl.uniform3f(obj(location), v0, v1, v2); }
export function uniform4f(location, v0, v1, v2, v3) { gl.uniform4f(obj(location), v0, v1, v2, v3); }
export function uniform1i(location, v0) { gl.uniform1i(obj(location), v0); }
export function uniform1ui(location, v0) { gl.uniform1ui(obj(location), v0); }
export function uniform1fv(location, values) { gl.uniform1fv(obj(location), asF32(values)); }
export function uniform2fv(location, values) { gl.uniform2fv(obj(location), asF32(values)); }
export function uniform3fv(location, values) { gl.uniform3fv(obj(location), asF32(values)); }
export function uniform4fv(location, values) { gl.uniform4fv(obj(location), asF32(values)); }

export function uniformMatrix4fv(location, transpose, values) {
    gl.uniformMatrix4fv(obj(location), !!transpose, asF32(values));
}

export function uniformMatrix3x2fv(location, transpose, values) {
    gl.uniformMatrix3x2fv(obj(location), !!transpose, asF32(values));
}

// ------------------------------------------------------------------ 纹理
export function genTextures(ids) {
    const dst = asU32(ids);
    for (let i = 0; i < dst.length; i++) dst[i] = reg(gl.createTexture());
}

export function deleteTextures(ids) {
    const src = asU32(ids);
    for (let i = 0; i < src.length; i++) {
        gl.deleteTexture(obj(src[i]));
        drop(src[i]);
    }
}

export function bindTexture(target, texture) { gl.bindTexture(target, obj(texture)); }
export function activeTexture(unit) { gl.activeTexture(unit); }
export function texParameteri(target, pname, value) { gl.texParameteri(target, pname, value); }
export function generateMipmap(target) { gl.generateMipmap(target); }

export function texImage2D(target, level, internalFormat, width, height, border, format, type, pixels) {
    const data = byteLength(pixels) === 0 ? null : pixels.data;
    gl.texImage2D(target, level, internalFormat, width, height, border, format, type, data);
}

export function texSubImage2D(target, level, xOffset, yOffset, width, height, format, type, pixels) {
    const data = byteLength(pixels) === 0 ? null : pixels.data;
    gl.texSubImage2D(target, level, xOffset, yOffset, width, height, format, type, data);
}

// ------------------------------------------------------------------ FBO / RBO
export function genFramebuffers(ids) {
    const dst = asU32(ids);
    for (let i = 0; i < dst.length; i++) dst[i] = reg(gl.createFramebuffer());
}

export function deleteFramebuffers(ids) {
    const src = asU32(ids);
    for (let i = 0; i < src.length; i++) {
        gl.deleteFramebuffer(obj(src[i]));
        drop(src[i]);
    }
}

export function bindFramebuffer(target, framebuffer) { gl.bindFramebuffer(target, obj(framebuffer)); }

export function framebufferTexture2D(target, attachment, textureTarget, texture, level) {
    gl.framebufferTexture2D(target, attachment, textureTarget, obj(texture), level);
}

export function framebufferRenderbuffer(target, attachment, renderbufferTarget, renderbuffer) {
    gl.framebufferRenderbuffer(target, attachment, renderbufferTarget, obj(renderbuffer));
}

export function checkFramebufferStatus(target) {
    return gl.checkFramebufferStatus(target);
}

export function drawBuffers(buffers) { gl.drawBuffers(Array.from(asU32(buffers))); }
export function readBuffer(source) { gl.readBuffer(source); }

export function genRenderbuffers(ids) {
    const dst = asU32(ids);
    for (let i = 0; i < dst.length; i++) dst[i] = reg(gl.createRenderbuffer());
}

export function deleteRenderbuffers(ids) {
    const src = asU32(ids);
    for (let i = 0; i < src.length; i++) {
        gl.deleteRenderbuffer(obj(src[i]));
        drop(src[i]);
    }
}

export function bindRenderbuffer(target, renderbuffer) { gl.bindRenderbuffer(target, obj(renderbuffer)); }
export function renderbufferStorage(target, internalFormat, width, height) {
    gl.renderbufferStorage(target, internalFormat, width, height);
}

export function readPixels(x, y, width, height, format, type, pixels) {
    gl.readPixels(x, y, width, height, format, type, pixels.data);
}

// ------------------------------------------------------------------ 绘制
export function drawArrays(mode, first, count) { gl.drawArrays(mode, first, count); }
export function drawElements(mode, count, type, offset) { gl.drawElements(mode, count, type, offset); }
export function drawArraysInstanced(mode, first, count, instanceCount) {
    gl.drawArraysInstanced(mode, first, count, instanceCount);
}

export function drawElementsInstanced(mode, count, type, offset, instanceCount) {
    gl.drawElementsInstanced(mode, count, type, offset, instanceCount);
}

// ------------------------------------------------------------------ 杂项
export function finish() { gl.finish(); }
export function flush() { gl.flush(); }
export function getError() { return gl.getError(); }

export function getString(name) {
    const value = gl.getParameter(name);
    return typeof value === 'string' ? value : '';
}

export function getStringi(name, index) {
    // WebGL 没有 glGetStringi：扩展列表走 getSupportedExtensions()
    if (name === 0x821D) {                     // GL_EXTENSIONS
        const list = gl.getSupportedExtensions();
        return index >= 0 && index < list.length ? list[index] : '';
    }
    return '';
}

export function getParameter(pname, output) {
    writeParameter(gl.getParameter(pname), asI32(output));
}

export function getParameterFloat(pname, output) {
    writeParameter(gl.getParameter(pname), asF32(output));
}

function writeParameter(value, destination) {
    if (Array.isArray(value) || ArrayBuffer.isView(value)) {
        for (let i = 0; i < destination.length && i < value.length; i++) {
            destination[i] = value[i];
        }
        return;
    }
    if (typeof value === 'boolean') {
        destination[0] = value ? 1 : 0;
        return;
    }
    if (typeof value === 'number') {
        destination[0] = value;
    }
}
