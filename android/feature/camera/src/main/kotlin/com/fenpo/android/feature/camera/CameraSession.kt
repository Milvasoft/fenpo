package com.fenpo.android.feature.camera

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Rect
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.hardware.camera2.CameraMetadata
import android.hardware.camera2.CaptureRequest
import android.hardware.camera2.CaptureResult
import android.hardware.camera2.TotalCaptureResult
import android.hardware.camera2.params.OutputConfiguration
import android.hardware.camera2.params.SessionConfiguration
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import android.util.Range
import android.view.Surface
import com.fenpo.protocol.v1.CameraFacing
import com.fenpo.protocol.v1.CameraSettings
import java.util.concurrent.Executor

/**
 * Camera2 for the webcam (docs/ANDROID.md §10): one capture session feeds the encoder and, while
 * the camera screen is open, its preview. CameraX's `Preview` gives frames to a single surface,
 * which is why this uses Camera2. Zoom uses `CONTROL_ZOOM_RATIO`; "Center" crops around the face
 * the camera detects.
 *
 * Frames reach the encoder in the sensor's orientation: upright when the phone lies in landscape,
 * as on the stand the design recommends.
 */
class CameraSession(
    context: Context,
    private val encoderSurface: Surface,
    private val fps: Int,
    private val onFailure: (String) -> Unit,
) {
    private val manager = context.getSystemService(CameraManager::class.java)
    private val thread = HandlerThread("FenpoCamera").apply { start() }
    private val handler = Handler(thread.looper)
    private val executor = Executor { handler.post(it) }

    private var device: CameraDevice? = null
    private var session: CameraCaptureSession? = null
    private var previewSurface: Surface? = null
    private var settings = CameraSettings(facing = CameraFacing.CAMERA_FACING_BACK, zoom = 1f)
    private var activeArray = Rect()
    private var zoomRange = Range(1f, 1f)
    private var faceCenter: Pair<Float, Float>? = null
    private var lastCropUpdate = 0L

    /** Zoom ratios worth offering for the current camera (design: 0,5 / 1× / 2). */
    var zoomPresets: List<Float> = listOf(1f)
        private set

    /** Whether the phone has a front camera to flip to. */
    val frontAvailable: Boolean
        get() = cameraId(CameraFacing.CAMERA_FACING_FRONT) != null

    /** Opens the camera for [initial]; reports problems through `onFailure`. */
    fun start(initial: CameraSettings) {
        handler.post { open(initial) }
    }

    /** Applies new settings: another facing reopens the camera, the rest changes the request. */
    fun apply(next: CameraSettings) {
        handler.post {
            val facingChanged = next.facing != CameraFacing.CAMERA_FACING_UNSPECIFIED && next.facing != settings.facing
            if (facingChanged) {
                closeCamera()
                open(next)
            } else {
                settings = next.copy(facing = settings.facing, zoom = if (next.zoom > 0f) next.zoom else settings.zoom)
                repeat()
            }
        }
    }

    /** The camera screen's preview surface, or `null` when the screen is gone. */
    fun setPreview(surface: Surface?) {
        handler.post {
            if (surface == previewSurface) return@post
            previewSurface = surface
            if (device != null) configure()
        }
    }

    /** The settings as applied. */
    fun current(): CameraSettings = settings

    fun stop() {
        handler.post {
            closeCamera()
            thread.quitSafely()
        }
    }

    @SuppressLint("MissingPermission") // The camera screen asks for CAMERA before this service starts.
    private fun open(wanted: CameraSettings) {
        val facing =
            if (wanted.facing ==
                CameraFacing.CAMERA_FACING_UNSPECIFIED
            ) {
                CameraFacing.CAMERA_FACING_BACK
            } else {
                wanted.facing
            }
        val id = cameraId(facing) ?: cameraId(CameraFacing.CAMERA_FACING_BACK)
        if (id == null) {
            onFailure("No camera")
            return
        }
        val characteristics = manager.getCameraCharacteristics(id)
        activeArray = characteristics.get(CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE) ?: Rect()
        zoomRange = characteristics.get(CameraCharacteristics.CONTROL_ZOOM_RATIO_RANGE) ?: Range(1f, 1f)
        zoomPresets = presets(zoomRange)
        settings =
            wanted.copy(
                facing = facing,
                zoom =
                    wanted.zoom.takeIf { it > 0f }?.coerceIn(zoomRange.lower, zoomRange.upper) ?: 1f,
            )
        manager.openCamera(
            id,
            executor,
            object : CameraDevice.StateCallback() {
                override fun onOpened(camera: CameraDevice) {
                    device = camera
                    configure()
                }

                override fun onDisconnected(camera: CameraDevice) {
                    camera.close()
                    device = null
                    onFailure("Another app took the camera")
                }

                override fun onError(
                    camera: CameraDevice,
                    error: Int,
                ) {
                    camera.close()
                    device = null
                    onFailure(
                        if (error ==
                            ERROR_CAMERA_IN_USE
                        ) {
                            "The camera is in use by another app"
                        } else {
                            "Camera error $error"
                        },
                    )
                }
            },
        )
    }

    private fun configure() {
        val camera = device ?: return
        session?.close()
        session = null
        val outputs = listOfNotNull(encoderSurface, previewSurface).map { OutputConfiguration(it) }
        val config =
            SessionConfiguration(
                SessionConfiguration.SESSION_REGULAR,
                outputs,
                executor,
                object : CameraCaptureSession.StateCallback() {
                    override fun onConfigured(configured: CameraCaptureSession) {
                        session = configured
                        repeat()
                    }

                    override fun onConfigureFailed(failed: CameraCaptureSession) {
                        onFailure("The camera could not be configured")
                    }
                },
            )
        runCatching {
            camera.createCaptureSession(
                config,
            )
        }.onFailure { onFailure(it.message ?: "Camera session failed") }
    }

    private fun repeat() {
        val camera = device ?: return
        val current = session ?: return
        val request =
            camera.createCaptureRequest(CameraDevice.TEMPLATE_RECORD).apply {
                addTarget(encoderSurface)
                previewSurface?.let { addTarget(it) }
                set(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, Range(fps, fps))
                set(CaptureRequest.CONTROL_VIDEO_STABILIZATION_MODE, CameraMetadata.CONTROL_VIDEO_STABILIZATION_MODE_ON)
                if (settings.center_stage) {
                    set(CaptureRequest.STATISTICS_FACE_DETECT_MODE, CameraMetadata.STATISTICS_FACE_DETECT_MODE_SIMPLE)
                    set(CaptureRequest.CONTROL_ZOOM_RATIO, 1f)
                    set(CaptureRequest.SCALER_CROP_REGION, centerCrop())
                } else {
                    set(CaptureRequest.CONTROL_ZOOM_RATIO, settings.zoom.coerceIn(zoomRange.lower, zoomRange.upper))
                }
            }
        runCatching { current.setRepeatingRequest(request.build(), FaceTracker(), handler) }
            .onFailure { Log.w(TAG, "Repeating request failed: ${it.message}") }
    }

    /** Follows the largest face; the crop moves at most five times a second to stay calm. */
    private inner class FaceTracker : CameraCaptureSession.CaptureCallback() {
        override fun onCaptureCompleted(
            session: CameraCaptureSession,
            request: CaptureRequest,
            result: TotalCaptureResult,
        ) {
            if (!settings.center_stage) return
            val face =
                result.get(CaptureResult.STATISTICS_FACES)?.maxByOrNull { it.bounds.width() * it.bounds.height() }
                    ?: return
            val target = face.bounds.exactCenterX() to face.bounds.exactCenterY()
            val previous = faceCenter ?: target
            faceCenter =
                (previous.first + (target.first - previous.first) * SMOOTHING) to
                (previous.second + (target.second - previous.second) * SMOOTHING)
            val now = System.currentTimeMillis()
            if (now - lastCropUpdate >= CROP_INTERVAL_MS) {
                lastCropUpdate = now
                repeat()
            }
        }
    }

    /** A crop of the active array around the face, as tight as the zoom setting (at least 1.4×). */
    private fun centerCrop(): Rect {
        val zoom = maxOf(settings.zoom, CENTER_MIN_ZOOM)
        val width = (activeArray.width() / zoom).toInt()
        val height = (activeArray.height() / zoom).toInt()
        val (cx, cy) = faceCenter ?: (activeArray.exactCenterX() to activeArray.exactCenterY())
        val left = (cx - width / 2).toInt().coerceIn(activeArray.left, activeArray.right - width)
        val top = (cy - height / 2).toInt().coerceIn(activeArray.top, activeArray.bottom - height)
        return Rect(left, top, left + width, top + height)
    }

    private fun closeCamera() {
        session?.close()
        session = null
        device?.close()
        device = null
        faceCenter = null
    }

    private fun cameraId(facing: CameraFacing): String? {
        val lens =
            if (facing ==
                CameraFacing.CAMERA_FACING_FRONT
            ) {
                CameraMetadata.LENS_FACING_FRONT
            } else {
                CameraMetadata.LENS_FACING_BACK
            }
        return manager.cameraIdList.firstOrNull {
            manager.getCameraCharacteristics(it).get(CameraCharacteristics.LENS_FACING) ==
                lens
        }
    }

    companion object {
        private const val TAG = "FenpoCamera"
        private const val SMOOTHING = 0.3f
        private const val CROP_INTERVAL_MS = 200L
        private const val CENTER_MIN_ZOOM = 1.4f

        /** 0.5 when the camera goes that wide, 1, and 2 when it zooms that far. */
        fun presets(range: Range<Float>): List<Float> =
            buildList {
                if (range.lower <= WIDE + 0.05f) add(WIDE)
                add(1f)
                if (range.upper >= 2f) add(2f)
            }

        private const val WIDE = 0.5f
    }
}
