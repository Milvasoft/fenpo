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
 * Frames come in the sensor's orientation; [GlFrameRotator] turns them upright for the PC using
 * [sensorOrientation] and [isFront].
 *
 * Zoom is relative to the facing's main lens. Below what the main lens reaches (0,5, or the phone
 * held upright, where the rotator crops by about 1.8) the session moves to the back's ultra-wide
 * lens when the phone shows it to apps, like a webcam held upright keeps its field of view.
 */
class CameraSession(
    context: Context,
    private val encoderSurface: Surface,
    private val fps: Int,
    private val onOpened: () -> Unit = {},
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
    private var lenses = Lenses(main = Lens(id = "", reach = 1f), wide = null)
    private var lens = lenses.main
    private var mainMinZoom = 1f
    private var opening: String? = null
    private var faceCenter: Pair<Float, Float>? = null
    private var lastCropUpdate = 0L

    /** Zoom ratios worth offering for the current camera (design: 0,5 / 1× / 2). */
    var zoomPresets: List<Float> = listOf(1f)
        private set

    /** How the open camera's sensor is mounted, in degrees. */
    @Volatile var sensorOrientation: Int = 0
        private set

    /** Whether the open camera faces the user. */
    @Volatile var isFront: Boolean = false
        private set

    /** Whether the phone has a front camera to flip to. */
    val frontAvailable: Boolean
        get() = lensesFor(CameraFacing.CAMERA_FACING_FRONT) != null

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
                follow()
            }
        }
    }

    @Volatile private var cropCompensation = 1f

    /**
     * How much [GlFrameRotator] crops (1 in landscape, about 1.8 when the phone is upright): the
     * camera zooms out by as much, on the ultra-wide lens when the main one cannot, so the PC sees
     * the field of view the zoom setting means instead of a close-up (device test, 16 October:
     * the S10+'s main lens stops at 1×, its ultra-wide sees about twice as wide).
     */
    fun setCropCompensation(factor: Float) {
        handler.post {
            if (factor == cropCompensation) return@post
            cropCompensation = factor
            follow()
        }
    }

    /** The zoom ratio on the open lens that gives the field of view [settings] mean. */
    private fun effectiveZoom(): Float =
        (settings.zoom * lens.reach / cropCompensation).coerceIn(zoomRange.lower, zoomRange.upper)

    /** The lens the current zoom and crop need: the ultra-wide only where the main one is too narrow. */
    private fun wantedLens(): Lens {
        val wide = lenses.wide ?: return lenses.main
        return if (settings.zoom / cropCompensation < mainMinZoom) wide else lenses.main
    }

    /** Moves to the lens the settings need, or changes the request on the open one. */
    private fun follow() {
        val next = wantedLens()
        if (next.id != lens.id) {
            closeCamera()
            openLens(next)
        } else {
            repeat()
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

    private fun open(wanted: CameraSettings) {
        val asked =
            if (wanted.facing ==
                CameraFacing.CAMERA_FACING_UNSPECIFIED
            ) {
                CameraFacing.CAMERA_FACING_BACK
            } else {
                wanted.facing
            }
        val facing = if (lensesFor(asked) != null) asked else CameraFacing.CAMERA_FACING_BACK
        val found = lensesFor(facing)
        if (found == null) {
            onFailure("No camera")
            return
        }
        lenses = found
        val mainRange = zoomRangeOf(found.main.id)
        mainMinZoom = mainRange.lower
        // Zoom settings are on the main lens's scale; the ultra-wide reaches below its 1×.
        val limits = Range(found.wide?.let { zoomRangeOf(it.id).lower / it.reach } ?: mainRange.lower, mainRange.upper)
        zoomPresets = presets(limits)
        settings =
            wanted.copy(
                facing = facing,
                zoom = wanted.zoom.takeIf { it > 0f }?.coerceIn(limits.lower, limits.upper) ?: 1f,
            )
        openLens(wantedLens())
    }

    @SuppressLint("MissingPermission") // The camera screen asks for CAMERA before this service starts.
    private fun openLens(next: Lens) {
        lens = next
        val characteristics = manager.getCameraCharacteristics(next.id)
        sensorOrientation = characteristics.get(CameraCharacteristics.SENSOR_ORIENTATION) ?: 0
        isFront = characteristics.get(CameraCharacteristics.LENS_FACING) == CameraMetadata.LENS_FACING_FRONT
        activeArray = characteristics.get(CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE) ?: Rect()
        zoomRange = zoomRangeOf(next.id)
        // One camera opens at a time; a lens wanted while another opens follows when that one is in.
        if (opening != null) return
        opening = next.id
        Log.i(TAG, "Opening camera ${next.id} (reach ${"%.2f".format(next.reach)}, zoom ${effectiveZoom()})")
        manager.openCamera(
            next.id,
            executor,
            object : CameraDevice.StateCallback() {
                override fun onOpened(camera: CameraDevice) {
                    opening = null
                    if (camera.id != lens.id || device != null) {
                        camera.close()
                        if (device == null) openLens(lens)
                        return
                    }
                    device = camera
                    onOpened()
                    configure()
                }

                override fun onDisconnected(camera: CameraDevice) {
                    camera.close()
                    if (opening == camera.id) opening = null
                    if (camera.id != lens.id) {
                        if (device == null && opening == null) openLens(lens)
                        return
                    }
                    device = null
                    onFailure("Another app took the camera")
                }

                override fun onError(
                    camera: CameraDevice,
                    error: Int,
                ) {
                    camera.close()
                    if (opening == camera.id) opening = null
                    if (camera.id != lens.id) {
                        if (device == null && opening == null) openLens(lens)
                        return
                    }
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
                    set(CaptureRequest.CONTROL_ZOOM_RATIO, effectiveZoom())
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
        val zoom = maxOf(settings.zoom, CENTER_MIN_ZOOM) * lens.reach / cropCompensation
        val width = (activeArray.width() / zoom.coerceAtLeast(1f)).toInt()
        val height = (activeArray.height() / zoom.coerceAtLeast(1f)).toInt()
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

    private fun lensesFor(facing: CameraFacing): Lenses? {
        val wanted =
            if (facing ==
                CameraFacing.CAMERA_FACING_FRONT
            ) {
                CameraMetadata.LENS_FACING_FRONT
            } else {
                CameraMetadata.LENS_FACING_BACK
            }
        val ids =
            manager.cameraIdList.filter {
                manager.getCameraCharacteristics(it).get(CameraCharacteristics.LENS_FACING) ==
                    wanted
            }
        val main = ids.firstOrNull() ?: return null
        val mainView = viewOf(main)
        val lenses = ids.map { Lens(id = it, reach = viewOf(it) / mainView) }
        val wide = lenses.filter { it.reach >= WIDER_LENS }.maxByOrNull { it.reach }
        return Lenses(
            main = Lens(id = main, reach = 1f),
            wide =
                wide.takeIf {
                    facing !=
                        CameraFacing.CAMERA_FACING_FRONT
                },
        )
    }

    /** Half the horizontal field of view as a tangent: sensor width over twice the focal length. */
    private fun viewOf(id: String): Float {
        val characteristics = manager.getCameraCharacteristics(id)
        val width = characteristics.get(CameraCharacteristics.SENSOR_INFO_PHYSICAL_SIZE)?.width ?: return 1f
        val focal =
            characteristics.get(CameraCharacteristics.LENS_INFO_AVAILABLE_FOCAL_LENGTHS)?.minOrNull() ?: return 1f
        return width / (2 * focal)
    }

    private fun zoomRangeOf(id: String): Range<Float> =
        manager.getCameraCharacteristics(id).get(CameraCharacteristics.CONTROL_ZOOM_RATIO_RANGE) ?: Range(1f, 1f)

    /** One camera and how much wider than the facing's main lens it sees. */
    private class Lens(
        val id: String,
        val reach: Float,
    )

    private class Lenses(
        val main: Lens,
        val wide: Lens?,
    )

    companion object {
        private const val TAG = "FenpoCamera"
        private const val SMOOTHING = 0.3f
        private const val CROP_INTERVAL_MS = 200L
        private const val CENTER_MIN_ZOOM = 1.4f

        /** A lens counts as the ultra-wide when it sees at least this much wider than the main one. */
        private const val WIDER_LENS = 1.3f

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
