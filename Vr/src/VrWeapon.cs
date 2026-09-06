using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.XR;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// The 3D TT-33 pistol mounted rigidly on the right controller.
    ///
    /// Mounting ("pose rebasing"): real arm motion must be reproduced 1:1
    /// relative to the head, while real-world head TRANSLATION (physical
    /// walking/leaning) must not displace the gun. Both are achieved with
    /// Pose3 algebra:
    /// <code>
    /// rel     = Inverse(realHeadPose) * RGripPose      // hand relative to real head
    /// virtual = Pose3(headRotation, eyeAnchor)         // rotation-only virtual head
    /// weaponPose = virtual * rel                       // re-based onto virtual head
    /// </code>
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
        /// Local offset applied after the grip pose: scales the unit-normalized
        /// model, rotates the model's +X long axis (muzzle direction of the
        /// baked TT-33) onto the grip pose's -Z forward, and shifts the grip
        /// into the palm (slightly below and forward of the pose origin).
        /// </summary>
        private static Matrix WeaponLocalOffset => Matrix.Identity
            * Matrix.CreateScale(WeaponLength)
            * Matrix.CreateRotationY(MathHelper.PiOver2)
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
            bool zeroPose = hands.RGripPose.Translation == Vector3.Zero
                && hands.RGripPose.Orientation == Quaternion.Identity;
            if (zeroPose != !tracked)
            {
                tracked = !zeroPose;
                Console.WriteLine($"[VrWeapon] Right grip pose tracking: {tracked}");
            }
            if (zeroPose)
                return;

            // Hand pose relative to the real head, re-based onto the virtual
            // (rotation-only) head: real arm motion 1:1, physical body
            // movement ignored.
            Pose3 rel = Pose3.Inverse(headset.HeadPose) * hands.RGripPose;
            Pose3 virtualHead = new Pose3(rig.HeadRotation, rig.EyeAnchor);
            Pose3 weaponPose = virtualHead * rel;

            world = Matrix.CreateFromPose(weaponPose) * WeaponLocalOffset;

            MuzzleOrigin = world.Translation;
            Vector3 forward = world.Forward;
            if (forward.LengthSquared() > 1e-6f)
                forward.Normalize();
            MuzzleDirection = forward;
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
