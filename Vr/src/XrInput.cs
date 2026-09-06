using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.XR;

namespace ShittyMaze.Vr
{
    /// <summary>
    /// Thin wrapper over the KNI <see cref="TouchController"/> (OpenXR
    /// oculus/touch_controller interaction profile) providing the small piece
    /// of controller state the VR port needs:
    ///  - right thumbstick vector with a radial deadzone (locomotion),
    ///  - right analog trigger with our own press/release hysteresis (shooting),
    ///  - a vibration pulse helper (haptics).
    ///
    /// The controller is wired up automatically by the KNI XR backend once the
    /// session reaches the Enabled state; before that (or when the runtime is
    /// on a different interaction profile) <see cref="TouchController.GetState"/>
    /// returns an empty/default state, which this class reports as
    /// "not connected" with a zero stick and released trigger.
    /// </summary>
    public class XrInput
    {
        // Radial deadzone applied to the right thumbstick.
        private const float StickDeadzone = 0.2f;

        // Analog trigger hysteresis thresholds. KNI's built-in virtual trigger
        // button uses odd thresholds, so edge detection is done manually:
        // the trigger "presses" above PressThreshold and stays pressed until
        // it falls below ReleaseThreshold.
        private const float TriggerPressThreshold = 0.85f;
        private const float TriggerReleaseThreshold = 0.30f;

        private bool triggerHeld;
        private bool connectedLogged;

        /// <summary>Right thumbstick vector with radial deadzone applied. +Y = push forward.</summary>
        public Vector2 RightStick { get; private set; }

        /// <summary>True only on the frame the trigger crosses the press threshold (edge).</summary>
        public bool RightTriggerPressed { get; private set; }

        /// <summary>Raw analog value of the right trigger (0..1), for debugging.</summary>
        public float RightTriggerValue { get; private set; }

        /// <summary>Whether the right controller currently reports a usable state.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Polls the right touch controller once per frame. Must be called
        /// before reading <see cref="RightStick"/> / <see cref="RightTriggerPressed"/>.
        /// </summary>
        public void Update()
        {
            GamePadState state = TouchController.GetState(TouchControllerType.RTouch);

            bool connected = state.IsConnected;
            if (connected != IsConnected)
            {
                IsConnected = connected;
                Console.WriteLine($"[XrInput] Right controller connected: {connected}");
            }

            // One-shot capability + first data log for on-device verification
            // (see plan risk 7.1: oculus/touch_controller profile on PICO 4).
            if (!connectedLogged && connected)
            {
                connectedLogged = true;
                try
                {
                    GamePadCapabilities caps = TouchController.GetCapabilities(TouchControllerType.RTouch);
                    Console.WriteLine($"[XrInput] RTouch caps: type={caps.GamePadType} '{caps.DisplayName}' connected={caps.IsConnected}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[XrInput] GetCapabilities failed: {ex.Message}");
                }
            }

            // --- Thumbstick with radial deadzone ---
            Vector2 stick = state.ThumbSticks.Right;
            float length = stick.Length();
            if (length < StickDeadzone)
            {
                RightStick = Vector2.Zero;
            }
            else
            {
                // Rescale so the deadzone edge maps to 0 and the rim to 1.
                float scaled = (length - StickDeadzone) / (1f - StickDeadzone);
                RightStick = (stick / length) * scaled;
            }

            // --- Trigger with hysteresis edge detection ---
            float trigger = state.Triggers.Right;
            RightTriggerValue = trigger;
            bool pressed = triggerHeld
                ? trigger >= TriggerReleaseThreshold
                : trigger > TriggerPressThreshold;
            RightTriggerPressed = pressed && !triggerHeld;
            triggerHeld = pressed;
        }

        /// <summary>
        /// Fires a short haptic pulse on the right controller.
        /// The KNI backend applies a fixed 3 kHz / 0.5 s pulse; the shot
        /// cooldown (>= 1 s) prevents overlapping pulses.
        /// </summary>
        /// <param name="amplitude">Pulse amplitude 0..1 (shot uses ~0.6).</param>
        public void Vibrate(float amplitude)
        {
            try
            {
                TouchController.SetVibration(TouchControllerType.RTouch, amplitude);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[XrInput] SetVibration failed: {ex.Message}");
            }
        }
    }
}
