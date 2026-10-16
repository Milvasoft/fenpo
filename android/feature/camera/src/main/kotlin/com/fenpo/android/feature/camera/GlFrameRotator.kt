package com.fenpo.android.feature.camera

import android.graphics.SurfaceTexture
import android.opengl.EGL14
import android.opengl.EGLConfig
import android.opengl.EGLContext
import android.opengl.EGLDisplay
import android.opengl.EGLExt
import android.opengl.EGLSurface
import android.opengl.GLES11Ext
import android.opengl.GLES20
import android.opengl.Matrix
import android.os.Handler
import android.os.HandlerThread
import android.view.Surface
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer

/**
 * Turns camera frames upright for the PC whatever way the phone is held: the camera draws into
 * [inputSurface], every frame is rotated by [rotationDegrees] and drawn into the encoder's
 * surface. A rotated portrait picture is cropped to the encoder's landscape frame, like a
 * webcam's. Runs on its own GL thread.
 */
class GlFrameRotator(
    private val output: Surface,
    private val width: Int,
    private val height: Int,
) {
    private val thread = HandlerThread("FenpoCameraGl").apply { start() }
    private val handler = Handler(thread.looper)
    private lateinit var display: EGLDisplay
    private lateinit var context: EGLContext
    private lateinit var surface: EGLSurface
    private lateinit var texture: SurfaceTexture
    private var program = 0
    private var textureId = 0
    private val transform = FloatArray(MATRIX)
    private val mvp = FloatArray(MATRIX)
    private val quad: FloatBuffer =
        ByteBuffer
            .allocateDirect(QUAD.size * FLOAT_BYTES)
            .order(ByteOrder.nativeOrder())
            .asFloatBuffer()
            .apply { put(QUAD).position(0) }

    /** Clockwise rotation applied to the camera picture, 0/90/180/270. */
    @Volatile var rotationDegrees: Int = 0

    /** Where the camera draws; valid after construction until [release]. */
    lateinit var inputSurface: Surface
        private set

    init {
        val ready = java.util.concurrent.CountDownLatch(1)
        handler.post {
            setUp()
            ready.countDown()
        }
        ready.await()
    }

    private fun setUp() {
        display = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY)
        val version = IntArray(2)
        EGL14.eglInitialize(display, version, 0, version, 1)
        val attributes =
            intArrayOf(
                EGL14.EGL_RED_SIZE,
                8,
                EGL14.EGL_GREEN_SIZE,
                8,
                EGL14.EGL_BLUE_SIZE,
                8,
                EGL14.EGL_RENDERABLE_TYPE,
                EGL14.EGL_OPENGL_ES2_BIT,
                EGL_RECORDABLE_ANDROID,
                1,
                EGL14.EGL_NONE,
            )
        val configs = arrayOfNulls<EGLConfig>(1)
        EGL14.eglChooseConfig(display, attributes, 0, configs, 0, 1, IntArray(1), 0)
        context =
            EGL14.eglCreateContext(
                display,
                configs[0],
                EGL14.EGL_NO_CONTEXT,
                intArrayOf(EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE),
                0,
            )
        surface = EGL14.eglCreateWindowSurface(display, configs[0], output, intArrayOf(EGL14.EGL_NONE), 0)
        EGL14.eglMakeCurrent(display, surface, surface, context)

        program = link(VERTEX_SHADER, FRAGMENT_SHADER)
        val ids = IntArray(1)
        GLES20.glGenTextures(1, ids, 0)
        textureId = ids[0]
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, textureId)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR)
        texture =
            SurfaceTexture(textureId).apply {
                setDefaultBufferSize(width, height)
                setOnFrameAvailableListener({ draw() }, handler)
            }
        inputSurface = Surface(texture)
    }

    private fun draw() {
        texture.updateTexImage()
        texture.getTransformMatrix(transform)
        GLES20.glViewport(0, 0, width, height)
        GLES20.glUseProgram(program)
        val rotation = rotationDegrees
        // Rotate in pixel space so the picture keeps its shape; a quarter turn makes it portrait,
        // which is scaled up to cover the landscape frame (cropped top and bottom, like a webcam).
        val cover = if (rotation % STRAIGHT != 0) width.toFloat() / height else 1f
        Matrix.setIdentityM(mvp, 0)
        Matrix.scaleM(mvp, 0, 2f / width, 2f / height, 1f)
        Matrix.scaleM(mvp, 0, cover, cover, 1f)
        Matrix.rotateM(mvp, 0, -rotation.toFloat(), 0f, 0f, 1f)
        Matrix.scaleM(mvp, 0, width / 2f, height / 2f, 1f)
        GLES20.glUniformMatrix4fv(GLES20.glGetUniformLocation(program, "uMvp"), 1, false, mvp, 0)
        GLES20.glUniformMatrix4fv(GLES20.glGetUniformLocation(program, "uTex"), 1, false, transform, 0)
        val position = GLES20.glGetAttribLocation(program, "aPos")
        val coordinate = GLES20.glGetAttribLocation(program, "aTex")
        quad.position(0)
        GLES20.glVertexAttribPointer(position, 2, GLES20.GL_FLOAT, false, STRIDE, quad)
        GLES20.glEnableVertexAttribArray(position)
        quad.position(2)
        GLES20.glVertexAttribPointer(coordinate, 2, GLES20.GL_FLOAT, false, STRIDE, quad)
        GLES20.glEnableVertexAttribArray(coordinate)
        GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, VERTICES)
        EGLExt.eglPresentationTimeANDROID(display, surface, texture.timestamp)
        EGL14.eglSwapBuffers(display, surface)
    }

    fun release() {
        handler.post {
            inputSurface.release()
            texture.release()
            GLES20.glDeleteProgram(program)
            EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT)
            EGL14.eglDestroySurface(display, surface)
            EGL14.eglDestroyContext(display, context)
            EGL14.eglTerminate(display)
            thread.quitSafely()
        }
    }

    private fun link(
        vertex: String,
        fragment: String,
    ): Int {
        fun compile(
            type: Int,
            source: String,
        ) = GLES20.glCreateShader(type).also {
            GLES20.glShaderSource(it, source)
            GLES20.glCompileShader(it)
        }
        return GLES20.glCreateProgram().also {
            GLES20.glAttachShader(it, compile(GLES20.GL_VERTEX_SHADER, vertex))
            GLES20.glAttachShader(it, compile(GLES20.GL_FRAGMENT_SHADER, fragment))
            GLES20.glLinkProgram(it)
        }
    }

    companion object {
        private const val EGL_RECORDABLE_ANDROID = 0x3142
        private const val MATRIX = 16
        private const val FLOAT_BYTES = 4
        private const val STRIDE = 4 * FLOAT_BYTES
        private const val VERTICES = 4
        private const val STRAIGHT = 180

        // x, y, u, v for a full-screen strip.
        private val QUAD = floatArrayOf(-1f, -1f, 0f, 0f, 1f, -1f, 1f, 0f, -1f, 1f, 0f, 1f, 1f, 1f, 1f, 1f)

        private const val VERTEX_SHADER =
            "uniform mat4 uMvp; uniform mat4 uTex; attribute vec4 aPos; attribute vec4 aTex; varying vec2 vTex;" +
                "void main() { gl_Position = uMvp * aPos; vTex = (uTex * aTex).xy; }"
        private const val FRAGMENT_SHADER =
            "#extension GL_OES_EGL_image_external : require\n" +
                "precision mediump float; uniform samplerExternalOES sTex; varying vec2 vTex;" +
                "void main() { gl_FragColor = texture2D(sTex, vTex); }"

        /**
         * Clockwise turn that makes the camera picture upright, as for a JPEG's orientation:
         * the sensor's mounting plus the way the phone is held (degrees from `OrientationEventListener`,
         * rounded to a quarter), the other way round for the front camera.
         */
        fun rotationFor(
            sensorOrientation: Int,
            deviceDegrees: Int,
            front: Boolean,
        ): Int {
            val device = ((deviceDegrees + QUARTER / 2) / QUARTER * QUARTER) % FULL
            val turn = if (front) sensorOrientation - device else sensorOrientation + device
            return ((turn % FULL) + FULL) % FULL
        }

        private const val QUARTER = 90
        private const val FULL = 360
    }
}
