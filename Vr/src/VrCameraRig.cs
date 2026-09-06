using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.XR;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// Head-rotation-only stereo camera for the VR maze.
    ///
    /// Requirement: the player looks around by rotating the head, but physical
    /// head TRANSLATION (walking / leaning / crouching in the real room) must
    /// NOT move the player capsule or the camera. Therefore:
    ///  - the head rotation is taken from <see cref="HeadsetState.HeadPose"/>.Orientation,
    ///  - the head translation is DISCARDED, and replaced by a virtual eye
    ///    anchor at (player.X, EyeHeight, player.Z),
    ///  - the per-eye IPD offset is preserved by re-applying the eye-to-head
    ///    translation (rotated into the virtual head frame), so stereo
    ///    separation is correct while real-world walking has no effect.
    /// </summary>
    public class VrCameraRig
    {
        /// <summary>
        /// Constant virtual eye height. Maze walls are 2.0 m; 1.5 m feels
        /// natural in VR (the web camera used 0.7 m which is far too low).
        /// </summary>
        public const float EyeHeight = 1.5f;

        private HeadsetState headsetState;

        /// <summary>Current head rotation (real-world; translations discarded).</summary>
        public Quaternion HeadRotation { get; private set; } = Quaternion.Identity;

        /// <summary>Virtual head/eye anchor world position (player XZ at fixed eye height).</summary>
        public Vector3 EyeAnchor { get; private set; } = Vector3.Zero;

        /// <summary>
        /// World matrix of the virtual head: head rotation at the eye anchor.
        /// Used to rebase the right-hand grip pose onto the virtual head.
        /// </summary>
        public Matrix VirtualHeadMatrix { get; private set; } = Matrix.Identity;

        /// <summary>Horizontal (XZ, normalized) forward of the head - movement basis.</summary>
        public Vector3 HeadForwardXZ { get; private set; } = new Vector3(0f, 0f, -1f);

        /// <summary>Horizontal (XZ, normalized) right of the head - movement basis.</summary>
        public Vector3 HeadRightXZ { get; private set; } = new Vector3(1f, 0f, 0f);

        /// <summary>Full 3D forward of the head (including pitch) - used by the level banner.</summary>
        public Vector3 HeadForward3D { get; private set; } = new Vector3(0f, 0f, -1f);

        /// <summary>
        /// Recomputes the virtual camera from the current headset state and the
        /// player position. Call once per frame after <c>XRDevice.BeginFrame()</c>.
        /// </summary>
        public void Update(HeadsetState state, Vector3 playerPosition)
        {
            headsetState = state;

            // Take ONLY the rotation from the real head pose.
            HeadRotation = state.HeadPose.Orientation;

            // Discard the head translation: the eyes live on the player capsule.
            EyeAnchor = new Vector3(playerPosition.X, EyeHeight, playerPosition.Z);

            VirtualHeadMatrix =
                Matrix.CreateFromQuaternion(HeadRotation) * Matrix.CreateTranslation(EyeAnchor);

            // Full 3D forward (-Z in head space) for the billboard banner.
            HeadForward3D = Vector3.Transform(-Vector3.UnitZ, HeadRotation);

            // Horizontal-only basis for locomotion (looking up/down never moves the player).
            Vector3 forward = HeadForward3D;
            forward.Y = 0f;
            if (forward.LengthSquared() < 1e-6f)
                forward = new Vector3(0f, 0f, -1f); // Looking straight up/down: keep last sensible default axis.
            forward.Normalize();
            HeadForwardXZ = forward;

            // Right = forward x up (XNA left-handed cross ordering used by the web Camera).
            HeadRightXZ = Vector3.Cross(forward, Vector3.Up);
            if (HeadRightXZ.LengthSquared() > 1e-6f)
                HeadRightXZ.Normalize();
            else
                HeadRightXZ = new Vector3(1f, 0f, 0f);
        }

        /// <summary>
        /// World position of one virtual eye: the anchor plus the real eye
        /// offset from the head (IPD separation), rotated by the head rotation.
        /// </summary>
        public Vector3 GetEyePosition(XREye eye)
        {
            Pose3 eyePose = eye == XREye.Left ? headsetState.LEyePose : headsetState.REyePose;
            return EyeAnchor + GetEyeLocalOffset(eyePose);
        }

        /// <summary>
        /// View matrix for one eye: inverse of (head rotation at the virtual
        /// eye position). Real head translation is excluded; only the
        /// eye-relative-to-head offset (IPD) is kept.
        /// </summary>
        public Matrix GetEyeView(XREye eye)
        {
            Pose3 eyePose = eye == XREye.Left ? headsetState.LEyePose : headsetState.REyePose;
            Vector3 eyeWorld = EyeAnchor + GetEyeLocalOffset(eyePose);

            Matrix eyeWorldMatrix =
                Matrix.CreateFromQuaternion(HeadRotation) * Matrix.CreateTranslation(eyeWorld);
            return Matrix.Invert(eyeWorldMatrix);
        }

        /// <summary>
        /// Eye-to-head translation re-expressed relative to the virtual head:
        /// (eye.Translation - head.Translation) un-rotated into head space so it
        /// can be re-applied after the head rotation (≈ ±0.032 m along head-right).
        /// </summary>
        private Vector3 GetEyeLocalOffset(Pose3 eyePose)
        {
            Vector3 realOffset = eyePose.Translation - headsetState.HeadPose.Translation;
            Vector3 localOffset = Vector3.Transform(realOffset, Quaternion.Inverse(HeadRotation));
            return Vector3.Transform(localOffset, HeadRotation);
        }
    }
}
