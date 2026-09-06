using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.XR;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// The 3D TT-33 pistol mounted rigidly on the right controller.
    ///
    /// Mounting: real arm rotation must be reproduced 1:1 relative to the
    /// head, while real-world head TRANSLATION (physical walking/leaning)
    /// must not displace the gun. Rotation: the world matrix is
    /// <c>WeaponLocalOffset * M(gripOrientation)</c> - the local model fix is
    /// applied first and the grip orientation last (rightmost). Because the
    /// PICO grip pose is rigid with the head (grip = R * head with a constant
    /// R, see <see cref="NaturalGripPitchDeg"/>), this keeps the
    /// gun-relative-to-head orientation constant while turning the torso,
    /// instead of swinging it around like the previous grip-first order did.
    /// Translation: the world-axes hand offset (grip.T - head.T) is
    /// re-anchored onto the virtual eye anchor.
    ///
    /// HP indication (requirement): the shared BasicEffect tint shows the
    /// remaining lives as weapon redness: 3 = white, 2 = redder, 1 = fully red
    /// (0 is transient - the game resets on death).
    ///
    /// All tuning constants (scale, orientation, grip offset) live here so the
    /// first on-device run can adjust them in one place (plan risk 7.2).
    /// </summary>
    public class VrWeapon
    {
        // --- Tuning constants (on-device iteration expected) ---

        /// <summary>Weapon length in meters along its longest (normalized) axis.</summary>
        private const float WeaponLength = 0.24f;

        /// <summary>
        /// Natural-hold grip pitch measured on-device with the A-button
        /// calibration ([CALIB] samples): while the controller is held in
        /// front like a pistol, rel = inv(head)*grip is constant at
        /// ~RotX(+73 deg) for all body yaws (80.0 / 67.5 / 74.0 / 72.5 deg,
        /// axis ~pure X). Undoing it here aligns the "aim frame" (the frame
        /// this local offset's translation lives in) with the head frame at
        /// the natural hold.
        /// </summary>
        private const float NaturalGripPitchDeg = 73f;

        /// <summary>
        /// Model-local offset applied BEFORE the grip orientation (leftmost =
        /// applied first in XNA row-vector math):
        ///  1. scale the unit-normalized model;
        ///  2. RotY(-90 deg): the baked TT-33's muzzle actually points along
        ///     the model's -X (verified with the B-button calibration: the
        ///     visible barrel followed the image of model -X and the user
        ///     aligned it to head-forward at all four body yaws) - this maps
        ///     model -X onto the gun frame's -Z forward, so world.Forward is
        ///     the visible muzzle direction;
        ///  3. RotX(-73 deg): undo the natural-hold grip pitch (above);
        ///  4. palm shift, slightly below and forward in the aim frame.
        /// </summary>
        private static Matrix WeaponLocalOffset => Matrix.Identity
            * Matrix.CreateScale(WeaponLength)
            * Matrix.CreateRotationY(-MathHelper.PiOver2)
            * Matrix.CreateRotationX(MathHelper.ToRadians(-NaturalGripPitchDeg))
            * Matrix.CreateTranslation(new Vector3(0f, -0.01f, -0.10f));

        /// <summary>How fast the HP tint lerps to its target value (1/dt).</summary>
        private const float TintLerpPerSecond = 1f / 0.3f;

        // HP tint table, index = lives - 1 (0..2 for lives 1..3).
        private static readonly Vector3[] HpTints =
        {
            new Vector3(1.0f, 0.10f, 0.10f), // 1 life  - completely red
            new Vector3(1.0f, 0.45f, 0.45f), // 2 lives - redder
            new Vector3(1.0f, 1.00f, 1.00f), // 3 lives - normal
        };

        private readonly GltfModel model;

        private Matrix world = Matrix.Identity;
        private Vector3 currentTint = Vector3.One;
        private bool tracked;


        /// <summary>World-space muzzle origin (grip-pose based) for the hitscan ray.</summary>
        public Vector3 MuzzleOrigin { get; private set; }

        /// <summary>Normalized world-space shooting direction along the weapon's forward.</summary>
        public Vector3 MuzzleDirection { get; private set; } = new Vector3(0f, 0f, -1f);

        /// <summary>
        /// Pure orientation the weapon model is currently DRAWN with (rotation
        /// part of the world matrix; scale/translation removed). Consumed by
        /// <see cref="VrAimCalibrator"/> to log the displayed pistol rotation.
        /// </summary>
        public Quaternion DisplayRotation { get; private set; } = Quaternion.Identity;

        /// <summary>False while the right controller pose is unavailable (weapon hidden, no shooting).</summary>
        public bool Tracked => tracked;

        /// <summary>Creates the weapon from the raw glTF AndroidAssets.</summary>
        public VrWeapon(GraphicsDevice graphicsDevice)
        {
            model = GltfModel.Load(graphicsDevice, "tt_33/scene.gltf");
        }

        /// <summary>
        /// Recomputes the weapon world matrix from the current headset and hand
        /// poses and the virtual head (call once per frame in Draw, after
        /// <see cref="VrCameraRig.Update"/>).
        /// </summary>
        public void UpdatePose(HeadsetState headset, HandsState hands, VrCameraRig rig)
        {
            // Heuristic: an all-zero grip pose means the controller is not
            // tracked (KNI reports identity poses then) - hide the weapon.
            // KNI's never-updated HandsState default has Orientation W=0
            // (default quaternion, not Identity); a real pose never has W=0,
            // so treat that as untracked too.
            bool zeroPose = hands.RGripPose.Translation == Vector3.Zero
                && hands.RGripPose.Orientation == Quaternion.Identity;
            bool trackedNow = !zeroPose && hands.RGripPose.Orientation.W != 0f;
            if (trackedNow != tracked)
            {
                tracked = trackedNow;
                Console.WriteLine($"[VrWeapon] Right grip pose tracking: {tracked}");
            }
            if (!trackedNow)
                return;

            // Weapon orientation: local model offset first, grip orientation
            // LAST (rightmost). XNA row-vector matrices apply the leftmost
            // factor first, so this order means: model -> gun frame -> aim
            // frame -> grip pose -> world.
            //
            // WHY the grip must be rightmost: the PICO grip orientation is
            // rigid with the head (G = R * H with constant R = inv(H)*G, the
            // measured ~RotX(73 deg) natural-hold pose). With the grip
            // applied first (the old M(grip) * fix * local order) the head
            // rotation baked inside M(grip) never cancelled out of
            // gun-relative-to-head = local * M(R) * M(H) * fix * local *
            // inv(M(H)), so the pistol visibly swung when turning the torso.
            // With the grip rightmost, gun-in-head = WeaponLocalOffset *
            // M(rel) - constant while the controller is held rigidly in
            // front of the head, i.e. true 1:1 controller tracking.
            //
            // Position: the virtual head uses the same orientation as the
            // real head, only its position is replaced by EyeAnchor, so the
            // world-axes hand offset (grip.T - head.T) is simply re-anchored:
            //   weapon position = EyeAnchor + (grip.T - head.T)  [world axes]
            // (physical head/body translation is ignored, arm offsets are 1:1).
            world = WeaponLocalOffset * Matrix.CreateFromQuaternion(hands.RGripPose.Orientation);

            Vector3 handOffset = hands.RGripPose.Translation - headset.HeadPose.Translation;
            world.Translation = rig.EyeAnchor + handOffset + world.Translation;

            MuzzleOrigin = world.Translation;
            Vector3 forward = world.Forward;
            if (forward.LengthSquared() > 1e-6f)
                forward.Normalize();
            MuzzleDirection = forward;

            DisplayRotation = ExtractRotation(world);
        }

        /// <summary>
        /// Extracts the pure rotation quaternion from a (uniformly scaled,
        /// translated) transform matrix: normalizes the Right/Up basis rows,
        /// re-orthogonalizes (Backward = Right x Up for the XNA row layout,
        /// where row 3 is Backward = -Forward - using Forward there would
        /// build a reflection and produce a non-unit garbage quaternion) and
        /// converts to a quaternion in the same XNA convention as the
        /// head/grip pose quaternions.
        /// </summary>
        private static Quaternion ExtractRotation(Matrix m)
        {
            Vector3 right = m.Right;
            Vector3 up = m.Up;
            if (right.LengthSquared() < 1e-12f || up.LengthSquared() < 1e-12f)
                return Quaternion.Identity;

            right.Normalize();
            up.Normalize();

            Vector3 backward = Vector3.Cross(right, up);
            if (backward.LengthSquared() > 1e-12f)
                backward.Normalize();

            Matrix rotation = new Matrix(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                backward.X, backward.Y, backward.Z, 0f,
                0f, 0f, 0f, 1f);
            Quaternion q = Quaternion.CreateFromRotationMatrix(rotation);
            q.Normalize();
            return q;
        }

        /// <summary>
        /// Smoothly moves the weapon tint toward the value for the given
        /// remaining lives (3 = white, 2 = redder, 1 = fully red).
        /// </summary>
        public void UpdateTint(int lives, float deltaTime)
        {
            int idx = MathHelper.Clamp(lives - 1, 0, HpTints.Length - 1);
            Vector3 target = HpTints[idx];
            currentTint = Vector3.Lerp(currentTint, target, MathHelper.Clamp(TintLerpPerSecond * deltaTime, 0f, 1f));
            model.Tint = currentTint;
        }

        /// <summary>
        /// Draws the weapon per eye among the scene geometry (real world
        /// object: depth test ON, no back-face culling - material is
        /// doubleSided, opaque).
        /// </summary>
        public void Draw(Matrix view, Matrix projection)
        {
            if (!tracked)
                return;

            GraphicsDevice gd = model.GraphicsDevice;
            gd.BlendState = BlendState.Opaque;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            model.Draw(world, view, projection);
        }

    }
}
