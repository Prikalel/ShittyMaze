using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.XR;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// Debug pose sampler for investigating the bug where the pistol rotation
    /// swings unnaturally when the player turns the torso while holding the
    /// controller rigidly in front of the head.
    ///
    /// Protocol (right controller only):
    ///  - A (short press): sample of the NATURAL holding pose - controller in
    ///    front, wrist oriented as one would really hold a pistol (in-game the
    ///    pistol may look wrong - that is exactly what is being measured).
    ///  - B (short press): sample of the CORRECTED pose - the controller is
    ///    rotated (kept roughly at the same position relative to the head)
    ///    until the in-game pistol points straight away from the camera.
    ///  - The A/B pair is repeated after every 90-degree torso turn to the
    ///    left (body yaws 0/90/180/270).
    ///
    /// Analysis idea: while the controller is held rigidly relative to the
    /// head, the head-relative grip pose ("rel") must NOT change between
    /// consecutive same-button samples, and the pistol aimed straight each
    /// time yields a constant head-relative gun pose ("relGun"). Any
    /// systematic delta (angle + axis) between samples exposes which rotation
    /// axis the displayed weapon swings around.
    ///
    /// Every sample is dumped to the log (Console.WriteLine -> logcat) with
    /// the [CALIB] prefix: raw quaternions, head-relative poses as
    /// angle-axis, and deltas against the previous sample of the same button.
    /// </summary>
    public class VrAimCalibrator
    {
        private struct Sample
        {
            public int Index;
            public Quaternion Head;    // camera (headset) rotation
            public Quaternion Grip;    // right grip pose orientation
            public Quaternion Rel;     // grip in head frame:  inv(head)*grip
            public Quaternion Gun;     // displayed weapon rotation (VrWeapon.DisplayRotation)
            public Quaternion RelGun;  // gun in head frame:   inv(head)*gun
        }

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private int sampleCounter;
        private Sample? lastA;
        private Sample? lastB;

        /// <summary>
        /// Records a sample when A or B was pressed this frame. Call once per
        /// frame from Draw with the SAME fresh HeadsetState/HandsState used to
        /// pose the weapon (after <see cref="VrWeapon.UpdatePose"/>, so
        /// <see cref="VrWeapon.DisplayRotation"/> is current).
        /// </summary>
        public void Update(bool aPressed, bool bPressed, HeadsetState headset, HandsState hands, VrWeapon weapon)
        {
            if (!aPressed && !bPressed)
                return;

            if (weapon == null || !weapon.Tracked)
            {
                Log(" right grip pose not tracked - sample SKIPPED");
                return;
            }

            Record(aPressed ? 'A' : 'B', headset, hands, weapon);
        }

        private void Record(char button, HeadsetState headset, HandsState hands, VrWeapon weapon)
        {
            Quaternion head = headset.HeadPose.Orientation;
            Quaternion grip = hands.RGripPose.Orientation;
            Quaternion invHead = Quaternion.Inverse(head);
            Quaternion gun = weapon.DisplayRotation;

            Sample s = new Sample
            {
                Index = ++sampleCounter,
                Head = head,
                Grip = grip,
                // XNA operator* is the Hamilton product, so
                // invHead * grip == Pose3 Inverse(head)*grip (grip in head frame).
                Rel = invHead * grip,
                Gun = gun,
                RelGun = invHead * gun,
            };

            Log($" === sample #{s.Index} button={button} t={clock.Elapsed.TotalSeconds:F2}s ===");
            Log($"  head  q={FmtQ(s.Head)} {FmtEuler(s.Head)}");
            Log($"  grip  q={FmtQ(s.Grip)} {FmtEuler(s.Grip)}");
            Log($"  rel   (grip in head frame)   q={FmtQ(s.Rel)} aa={FmtAA(s.Rel)}");
            Log($"  gun   (displayed weapon rot) q={FmtQ(s.Gun)} {FmtEuler(s.Gun)}");
            Log($"  gunFwdWorld=({FmtV(Forward(s.Gun))})  gunFwdInHead=({FmtV(Forward(s.RelGun))})");
            Log($"  relGun(gun in head frame)   q={FmtQ(s.RelGun)} aa={FmtAA(s.RelGun)}");

            Sample? prev = button == 'A' ? lastA : lastB;
            if (prev.HasValue)
            {
                Log($"  deltas vs sample #{prev.Value.Index} [{button}]  (cur * inv(prev), world axes):");
                Log($"    dHead   aa={FmtAA(Delta(prev.Value.Head, s.Head))}");
                Log($"    dRel    aa={FmtAA(Delta(prev.Value.Rel, s.Rel))}    (~0 expected: grip rigid vs head)");
                Log($"    dGun    aa={FmtAA(Delta(prev.Value.Gun, s.Gun))}");
                Log($"    dRelGun aa={FmtAA(Delta(prev.Value.RelGun, s.RelGun))} (~0 expected: pistol aimed straight each time)");
            }
            else
            {
                Log("  (first sample of this button - no delta)");
            }

            if (button == 'A')
                lastA = s;
            else
                lastB = s;
        }

        /// <summary>Rotation that takes prev to cur, expressed with world axes: inv(prev)*cur.</summary>
        private static Quaternion Delta(Quaternion prev, Quaternion cur)
        {
            return Quaternion.Inverse(prev) * cur;
        }

        /// <summary>Normalized forward (-Z) of a rotation.</summary>
        private static Vector3 Forward(Quaternion q)
        {
            Vector3 f = Vector3.Transform(-Vector3.UnitZ, q);
            if (f.LengthSquared() > 1e-10f)
                f.Normalize();
            return f;
        }

        private static string FmtQ(Quaternion q)
        {
            return $"({q.X,9:F5},{q.Y,9:F5},{q.Z,9:F5},{q.W,9:F5})";
        }

        private static string FmtV(Vector3 v)
        {
            return $"{v.X,7:F3},{v.Y,7:F3},{v.Z,7:F3}";
        }

        /// <summary>Canonical (shortest-arc, w>=0) angle-axis string in degrees.</summary>
        private static string FmtAA(Quaternion q)
        {
            q.Normalize();
            if (q.W < 0f)
                q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);

            float cosHalf = MathHelper.Clamp(q.W, -1f, 1f);
            float angleDeg = 2f * (float)Math.Acos(cosHalf) * (180f / (float)Math.PI);
            float sinHalf = (float)Math.Sqrt(MathHelper.Max(0f, 1f - q.W * q.W));

            Vector3 axis;
            if (sinHalf < 1e-4f)
                axis = Vector3.UnitX; // Angle ~ 0: the axis is irrelevant.
            else
                axis = new Vector3(q.X / sinHalf, q.Y / sinHalf, q.Z / sinHalf);

            return $"({angleDeg,6:F1} deg; x={axis.X,5:F2} y={axis.Y,5:F2} z={axis.Z,5:F2})";
        }

        /// <summary>
        /// Human-readable yaw/pitch/roll in degrees for a rotation quaternion
        /// (XNA LH conventions: -Z forward at identity; yaw about +Y).
        /// </summary>
        private static string FmtEuler(Quaternion q)
        {
            Vector3 fwd = Forward(q);
            Vector3 right = Vector3.Transform(Vector3.UnitX, q);
            Vector3 up = Vector3.Transform(Vector3.UnitY, q);
            if (fwd.LengthSquared() < 1e-10f)
                return "(unstable)";

            const float Rad2Deg = 180f / (float)Math.PI;
            float yaw = (float)Math.Atan2(fwd.X, -fwd.Z) * Rad2Deg;
            float pitch = (float)Math.Asin(MathHelper.Clamp(fwd.Y, -1f, 1f)) * Rad2Deg;
            float roll = (float)Math.Atan2(right.Y, up.Y) * Rad2Deg;
            return $"yaw={yaw,7:F1} pitch={pitch,6:F1} roll={roll,6:F1}";
        }

        private static void Log(string message)
        {
            Console.WriteLine($"[CALIB]{message}");
        }
    }
}
