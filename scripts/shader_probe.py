#!/usr/bin/env python3
"""Compile the actual retained shaders and numerically read an isolated cue pass.

Uses an offscreen EGL context. No game window or image is captured.
SceneProbe exports the composed shader sources directly from the built runtime.
"""
import argparse
import ctypes as C
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fixtures", type=Path)
    args = parser.parse_args()
    egl = C.CDLL("libEGL.so.1")

    def api(lib, name, result, *arguments):
        f = getattr(lib, name)
        f.restype, f.argtypes = result, arguments
        return f

    get_proc = api(egl, "eglGetProcAddress", C.c_void_p, C.c_char_p)
    platform = get_proc(b"eglGetPlatformDisplayEXT")
    if not platform:
        raise RuntimeError("offscreen EGL platform unavailable")
    display = C.CFUNCTYPE(C.c_void_p, C.c_uint, C.c_void_p, C.POINTER(C.c_int))(platform)(0x31dd, None, None)
    major, minor = C.c_int(), C.c_int()
    if not api(egl, "eglInitialize", C.c_uint, C.c_void_p, C.POINTER(C.c_int), C.POINTER(C.c_int))(display, C.byref(major), C.byref(minor)):
        raise RuntimeError("offscreen EGL initialise failed")
    api(egl, "eglBindAPI", C.c_uint, C.c_uint)(0x30a2)
    attributes = (C.c_int * 5)(0x3033, 1, 0x3040, 8, 0x3038)
    config, count = C.c_void_p(), C.c_int()
    if not api(egl, "eglChooseConfig", C.c_uint, C.c_void_p, C.POINTER(C.c_int), C.POINTER(C.c_void_p), C.c_int, C.POINTER(C.c_int))(display, attributes, C.byref(config), 1, C.byref(count)) or count.value == 0:
        raise RuntimeError("offscreen GL config unavailable")
    attributes = (C.c_int * 7)(0x3098, 3, 0x30fb, 3, 0x30fd, 1, 0x3038)
    context = api(egl, "eglCreateContext", C.c_void_p, C.c_void_p, C.c_void_p, C.c_void_p, C.POINTER(C.c_int))(display, config, None, attributes)
    if not context or not api(egl, "eglMakeCurrent", C.c_uint, C.c_void_p, C.c_void_p, C.c_void_p, C.c_void_p)(display, None, None, context):
        raise RuntimeError("offscreen GL core context unavailable")

    def gl(name, result, *arguments):
        address = get_proc(name.encode())
        if not address:
            raise RuntimeError(f"{name} unavailable")
        return C.CFUNCTYPE(result, *arguments)(address)

    u, i, f, ptr = C.c_uint, C.c_int, C.c_float, C.c_void_p
    get_string = gl("glGetString", C.c_char_p, u)
    renderer = get_string(0x1f01).decode()
    shader_create = gl("glCreateShader", u, u)
    shader_source = gl("glShaderSource", None, u, i, C.POINTER(C.c_char_p), C.POINTER(i))
    shader_compile = gl("glCompileShader", None, u)
    shader_status = gl("glGetShaderiv", None, u, u, C.POINTER(i))
    shader_log = gl("glGetShaderInfoLog", None, u, i, C.POINTER(i), C.c_char_p)
    link_status = gl("glGetProgramiv", None, u, u, C.POINTER(i))
    link_log = gl("glGetProgramInfoLog", None, u, i, C.POINTER(i), C.c_char_p)

    def shader(kind, source):
        handle = shader_create(kind)
        string = C.c_char_p(source.encode())
        shader_source(handle, 1, C.byref(string), None)
        shader_compile(handle)
        status = i()
        shader_status(handle, 0x8b81, C.byref(status))
        if not status.value:
            buffer = C.create_string_buffer(32768)
            shader_log(handle, len(buffer), None, buffer)
            raise RuntimeError(buffer.value.decode())
        return handle

    def program(vertex, fragment):
        handle = gl("glCreateProgram", u)()
        attach = gl("glAttachShader", None, u, u)
        attach(handle, shader(0x8b31, vertex))
        attach(handle, shader(0x8b30, fragment))
        gl("glLinkProgram", None, u)(handle)
        status = i()
        link_status(handle, 0x8b82, C.byref(status))
        if not status.value:
            buffer = C.create_string_buffer(32768)
            link_log(handle, len(buffer), None, buffer)
            raise RuntimeError(buffer.value.decode())
        return handle

    def source(name):
        return (args.fixtures / (name + ".glsl")).read_text()

    program(source("WorldVs"), source("PrimFs"))
    program(source("WorldNormalVs"), source("NormalFs"))
    cases = json.loads((args.fixtures / "fog-cases.json").read_text())
    vertex = """#version 330 core
    void main() { vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2); gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0); }
    """
    fragment = "#version 330 core\nuniform vec3 sampleCue; out float result;\n" + source("LinearDepthCue") + "\nvoid main() { result = linearDepthCue(sampleCue.x, sampleCue.yz); }"
    handle = program(vertex, fragment)
    gl("glUseProgram", None, u)(handle)
    location = gl("glGetUniformLocation", i, u, C.c_char_p)(handle, b"sampleCue")
    uniform = gl("glUniform3f", None, i, f, f, f)
    vao, texture, framebuffer = u(), u(), u()
    gl("glGenVertexArrays", None, i, C.POINTER(u))(1, C.byref(vao))
    gl("glBindVertexArray", None, u)(vao)
    gl("glGenTextures", None, i, C.POINTER(u))(1, C.byref(texture))
    gl("glBindTexture", None, u, u)(0x0de1, texture)
    gl("glTexImage2D", None, u, i, i, i, i, i, u, u, ptr)(0x0de1, 0, 0x822e, 1, 1, 0, 0x1903, 0x1406, None)
    gl("glGenFramebuffers", None, i, C.POINTER(u))(1, C.byref(framebuffer))
    gl("glBindFramebuffer", None, u, u)(0x8d40, framebuffer)
    gl("glFramebufferTexture2D", None, u, u, u, u, i)(0x8d40, 0x8ce0, 0x0de1, texture, 0)
    if gl("glCheckFramebufferStatus", u, u)(0x8d40) != 0x8cd5:
        raise RuntimeError("probe framebuffer incomplete")
    gl("glViewport", None, i, i, i, i)(0, 0, 1, 1)
    draw = gl("glDrawArrays", None, u, i, i)
    read = gl("glReadPixels", None, i, i, i, i, u, u, ptr)
    bad, maximum = [], 0
    for depth, near, far, expected in cases:
        uniform(location, depth, near, far)
        draw(4, 0, 3)
        value = f()
        read(0, 0, 1, 1, 0x1903, 0x1406, C.byref(value))
        error = abs(value.value - expected)
        maximum = max(maximum, error)
        if error != 0:
            bad.append(dict(depth=depth, near=near, far=far, expected=expected, gpu=value.value))
    # Exercise the exact modelPosed function composed into both real vertex
    # programs, using the recompiled game's decoder as the numeric oracle.
    model_fragment = """#version 330 core
    uniform mat3 uR; uniform vec3 uCam, uT; uniform float uH;
    uniform vec2 uC, uFb; uniform int uWorldSnap, vertexIndex;
    out ivec4 result;
    """ + source("ModelGlsl") + "\nvoid main() { result = ivec4(modelPosed(vertexIndex), 0); }"
    model = program(vertex, model_fragment)
    gl("glUseProgram", None, u)(model)
    get_location = gl("glGetUniformLocation", i, u, C.c_char_p)
    set_integer = gl("glUniform1i", None, i, i)
    for name, value in [(b"uModelPoses", 0), (b"uModelPose", 0)]:
        set_integer(get_location(model, name), value)
    weight_location, index_location = get_location(model, b"uModelPoseW"), get_location(model, b"vertexIndex")
    gl("glBindTexture", None, u, u)(0x0de1, texture)
    gl("glTexImage2D", None, u, i, i, i, i, i, u, u, ptr)(0x0de1, 0, 0x8d82, 1, 1, 0, 0x8d99, 0x1404, None)
    buffer, pose_texture = u(), u()
    gl("glGenBuffers", None, i, C.POINTER(u))(1, C.byref(buffer))
    gl("glBindBuffer", None, u, u)(0x8c2a, buffer)
    gl("glGenTextures", None, i, C.POINTER(u))(1, C.byref(pose_texture))
    gl("glBindTexture", None, u, u)(0x8c2a, pose_texture)
    upload = gl("glBufferData", None, u, C.c_ssize_t, ptr, u)
    attach_buffer = gl("glTexBuffer", None, u, u, u)
    pose_cases = json.loads((args.fixtures / "pose-cases.json").read_text())
    pose_bad, pose_vertices = [], 0
    for case in pose_cases:
        texels = (C.c_short * len(case["texels"]))(*case["texels"])
        upload(0x8c2a, C.sizeof(texels), texels, 0x88e0)
        attach_buffer(0x8c2a, 0x8d88, buffer)
        set_integer(weight_location, case["weight"])
        for index in range(len(case["expected"]) // 4):
            set_integer(index_location, index)
            draw(4, 0, 3)
            value = (i * 4)()
            read(0, 0, 1, 1, 0x8d99, 0x1404, value)
            expected = case["expected"][index * 4:index * 4 + 3]
            pose_vertices += 1
            if list(value)[:3] != expected:
                pose_bad.append(dict(weight=case["weight"], vertex=index, expected=expected, gpu=list(value)[:3]))
    world = source("WorldVs")
    record_source = world[world.index("uniform isampler2D uRecords;"):world.index("float linearDepthCue(")]
    light_program = program(vertex, "#version 330 core\nuniform vec3 sampleNormal; uniform uint sampleLight, sampleRgbc; out ivec4 result;\n"
                            + record_source + "\nvoid main() { vec3 colour, cue; recordLit(sampleLight, sampleNormal, 0.0, 0.0, sampleRgbc, colour, cue); result = ivec4(ivec3(colour), 0); }")
    gl("glUseProgram", None, u)(light_program)
    set_integer(get_location(light_program, b"uRecords"), 0)
    record_texture = u()
    gl("glGenTextures", None, i, C.POINTER(u))(1, C.byref(record_texture))
    gl("glBindTexture", None, u, u)(0x0de1, record_texture)
    parameter = gl("glTexParameteri", None, u, u, i)
    parameter(0x0de1, 0x2801, 0x2600)
    parameter(0x0de1, 0x2800, 0x2600)
    light_cases = json.loads((args.fixtures / "light-cases.json").read_text())
    light_bad = []
    for case in light_cases:
        values = (i * (52 * 64))(*(case["record"] + [0] * (52 * 63)))
        gl("glTexImage2D", None, u, i, i, i, i, i, u, u, ptr)(0x0de1, 0, 0x8d82, 13, 64, 0, 0x8d99, 0x1404, values)
        uniform(get_location(light_program, b"sampleNormal"), *case["normal"])
        unsigned = gl("glUniform1ui", None, i, u)
        unsigned(get_location(light_program, b"sampleLight"), 0x80000000 | case["turn"] << 6)
        unsigned(get_location(light_program, b"sampleRgbc"), case["rgbc"])
        draw(4, 0, 3)
        value = (i * 4)()
        read(0, 0, 1, 1, 0x8d99, 0x1404, value)
        if list(value)[:3] != case["expected"]:
            light_bad.append(dict(normal=case["normal"], expected=case["expected"], gpu=list(value)[:3]))
    report = dict(renderer=renderer, composedPrograms=2, linearCases=len(cases), maximumError=maximum,
                  mismatches=bad, poseVertices=pose_vertices, poseMismatches=pose_bad,
                  lightCases=len(light_cases), lightMismatches=light_bad)
    (args.fixtures / "gpu-report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"{renderer}: two actual retained programs linked; {len(cases)} cue cases, max error {maximum}; "
          f"{pose_vertices} pose vertices, {len(pose_bad)} mismatches; {len(light_cases)} native light cases, {len(light_bad)} mismatches")
    if bad or pose_bad or light_bad:
        raise RuntimeError(f"{len(bad)} GPU cue, {len(pose_bad)} GPU pose and {len(light_bad)} GPU light mismatches")
    api(egl, "eglTerminate", C.c_uint, C.c_void_p)(display)


if __name__ == "__main__":
    main()
