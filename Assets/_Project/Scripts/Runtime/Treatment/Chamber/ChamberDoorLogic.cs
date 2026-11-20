using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Treatment
{
    /// <summary>
    /// Pure rules of the chamber's hinged door (unit tested; <see cref="ChamberDoor"/> applies them): when the user counts as
    /// standing at the door, how the leaf's swing eases over time, and how far it may swing before it would hit a station
    /// or leave the hall. All positions are on the floor plane (world X, Z).
    /// </summary>
    public static class ChamberDoorLogic
    {
        /// <summary>The head must be within this horizontal distance of the closed door's front.</summary>
        public const float Range = 1.3f;
        /// <summary>Minimum cosine between "door front to head" and the door's outward normal (about 72 degrees off-axis).</summary>
        public const float MinFacing = 0.3f;
        /// <summary>How far the leaf swings outwards when nothing is in the way.</summary>
        public const float OpenAngle = 100f;
        /// <summary>Seconds for a full open or close.</summary>
        public const float SwingSeconds = 0.8f;
        /// <summary>Clear distance the swinging leaf keeps from station bases and the hall walls.</summary>
        public const float SwingMargin = 0.1f;
        /// <summary>Below this clear angle the door stays shut (it could not open usefully).</summary>
        public const float MinUsefulAngle = 20f;

        /// <summary>
        /// True when <paramref name="head"/> is within <paramref name="range"/> of the door front segment
        /// <paramref name="hinge"/>..<paramref name="edge"/> and roughly in front of it: the direction from the nearest point on
        /// the front to the head is at least <paramref name="minFacing"/> (cosine) along <paramref name="outward"/>.
        /// </summary>
        public static bool InRange(Vector2 head, Vector2 hinge, Vector2 edge, Vector2 outward, float range = Range, float minFacing = MinFacing)
        {
            var front = edge - hinge;
            var t = front.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(head - hinge, front) / front.sqrMagnitude) : 0f;
            var toHead = head - (hinge + front * t);
            var distance = toHead.magnitude;
            if (distance > range)
                return false;
            if (distance < 1e-4f)
                return true;
            var normal = outward.sqrMagnitude > 1e-8f ? outward.normalized : Vector2.zero;
            return Vector2.Dot(toHead / distance, normal) >= minFacing;
        }

        /// <summary>Smootherstep ease-in-out (zero speed and zero acceleration at both ends: a soft start and a damped stop).</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>Advances the linear swing progress (0 closed .. 1 open) towards the target over <paramref name="seconds"/> per full swing.</summary>
        public static float Step(float progress, bool open, float deltaTime, float seconds = SwingSeconds) =>
            Mathf.MoveTowards(Mathf.Clamp01(progress), open ? 1f : 0f, seconds > 1e-4f ? deltaTime / seconds : 1f);

        /// <summary>The leaf's angle for a linear progress (eased) and the allowed open angle.</summary>
        public static float Angle(float progress, float openAngle) => openAngle * Ease(progress);

        /// <summary>
        /// The largest swing (up to <paramref name="desired"/>, in whole <paramref name="step"/>s) for which the leaf — the
        /// segment from <paramref name="hinge"/> to its free edge, <paramref name="closedEdge"/> turned by the swing (positive
        /// = Unity yaw) — stays <paramref name="margin"/> clear of every obstacle polygon and, when given, inside
        /// <paramref name="area"/> (the hall floor, convex). The whole sweep up to the result is clear.
        /// </summary>
        public static float ClearAngle(Vector2 hinge, Vector2 closedEdge, float desired, IReadOnlyList<Vector2[]> obstacles,
            IReadOnlyList<Vector2> area, float margin = SwingMargin, float step = 1f)
        {
            var clear = 0f;
            var arm = closedEdge - hinge;
            for (var angle = step; angle <= desired + 1e-3f; angle += step)
            {
                var edge = hinge + Turn(arm, angle);
                if (!LeafClear(hinge, edge, obstacles, area, margin))
                    break;
                clear = angle;
            }

            return Mathf.Min(clear, desired);
        }

        /// <summary>Turns a floor-plane vector (X, Z) by a Unity yaw of <paramref name="degrees"/> (positive turns +X towards -Z).</summary>
        public static Vector2 Turn(Vector2 v, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }

        static bool LeafClear(Vector2 hinge, Vector2 edge, IReadOnlyList<Vector2[]> obstacles, IReadOnlyList<Vector2> area, float margin)
        {
            var leaf = new[] { hinge, edge };
            if (obstacles != null)
                foreach (var obstacle in obstacles)
                    if (obstacle != null && obstacle.Length >= 3 && StationLayout.Distance(leaf, obstacle) < margin)
                        return false;
            if (area == null || area.Count < 3)
                return true;
            // Inside the hall, with the margin to every wall.
            if (!StationLayout.Contains(area, edge))
                return false;
            for (var i = 0; i < area.Count; i++)
                if (DistanceToSegment(edge, area[i], area[(i + 1) % area.Count]) < margin)
                    return false;
            return true;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }
    }

    /// <summary>
    /// Pure rules of the door's GTA-style interaction pop-up (unit tested; <see cref="ChamberDoorPopup"/> draws it): which
    /// controller button toggles the door, what the button glyph and the action label say, and the pop-in, pop-out and press
    /// animation curves (normalised time 0..1 in, scale or alpha out).
    /// </summary>
    public static class ChamberDoorPrompt
    {
        /// <summary>Only this controller's primary button ({PrimaryButton}: A on the right controller) toggles the door in VR.</summary>
        public const InteractorHandedness ToggleHand = InteractorHandedness.Right;
        /// <summary>The glyph on the round badge in VR (the right controller's A).</summary>
        public const string VrGlyph = "A";
        /// <summary>The glyph on the keycap in the desktop demo (no headset).</summary>
        public const string DesktopGlyph = "O";

        /// <summary>Seconds of the pop-in (scale <see cref="PopInFrom"/> .. overshoot .. 1, alpha 0 .. 1).</summary>
        public const float PopInSeconds = 0.25f;
        /// <summary>Seconds of the pop-out (scale 1 .. <see cref="PopOutTo"/>, alpha 1 .. 0).</summary>
        public const float PopOutSeconds = 0.15f;
        /// <summary>Seconds of the press feedback (badge squash and flash, ring fill) before the pop-up hides for the swing.</summary>
        public const float PressSeconds = 0.2f;
        /// <summary>Seconds the progress ring takes to fill on a press.</summary>
        public const float RingFillSeconds = 0.15f;
        /// <summary>Scale the pop-up starts its pop-in from.</summary>
        public const float PopInFrom = 0.6f;
        /// <summary>Peak scale of the pop-in overshoot.</summary>
        public const float PopInPeak = 1.05f;
        /// <summary>Scale the pop-up shrinks to as it pops out.</summary>
        public const float PopOutTo = 0.85f;
        /// <summary>Smallest badge scale at the middle of a press.</summary>
        public const float PressSquash = 0.85f;
        /// <summary>Ease-out-back overshoot constant: peaks at 1 + 4k^3 / (27 (k + 1)^2) = 1.125, i.e. scale <see cref="PopInPeak"/> from <see cref="PopInFrom"/>.</summary>
        const float Overshoot = 1.94f;

        /// <summary>The glyph for the input mode: "A" (right controller) or "O" (keyboard, desktop demo).</summary>
        public static string GlyphFor(bool desktopMode) => desktopMode ? DesktopGlyph : VrGlyph;

        /// <summary>The one-word action label: what the button will do to the door ("Open" when closed, "Close" when open).</summary>
        public static string PromptLabel(bool open) => open ? "Close" : "Open";

        /// <summary>Ease-out-back from <see cref="PopInFrom"/> to 1 with an overshoot to about <see cref="PopInPeak"/>.</summary>
        public static float PopInScale(float t)
        {
            var u = Mathf.Clamp01(t) - 1f;
            var eased = 1f + (Overshoot + 1f) * u * u * u + Overshoot * u * u;
            return Mathf.LerpUnclamped(PopInFrom, 1f, eased);
        }

        /// <summary>The pop-in fade: fully opaque after the first 60 % of the pop-in.</summary>
        public static float PopInAlpha(float t) => Mathf.Clamp01(t / 0.6f);

        /// <summary>Ease-in shrink from 1 to <see cref="PopOutTo"/>.</summary>
        public static float PopOutScale(float t)
        {
            t = Mathf.Clamp01(t);
            return Mathf.Lerp(1f, PopOutTo, t * t);
        }

        /// <summary>The pop-out fade (linear, 1 .. 0).</summary>
        public static float PopOutAlpha(float t) => 1f - Mathf.Clamp01(t);

        /// <summary>The badge's scale over a press: down to <see cref="PressSquash"/> at the middle and back to 1.</summary>
        public static float PressScale(float t) => Mathf.Lerp(1f, PressSquash, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));

        /// <summary>The badge's brightening over a press: a flash at once that fades by the end.</summary>
        public static float PressFlash(float t) => 1f - Mathf.Clamp01(t);

        /// <summary>The progress ring's fill over a press (full after <see cref="RingFillSeconds"/>).</summary>
        public static float RingFill(float t) => Mathf.Clamp01(Mathf.Clamp01(t) * PressSeconds / RingFillSeconds);
    }

    /// <summary>
    /// The door's state (pure; unit tested): the user's wish (open or closed), the station override (a hands-on station
    /// retracts the whole front, see <see cref="TreatmentChamber.SetFrontOpen"/>) and the swing progress. The station
    /// override wins: while it holds, the user cannot toggle and the leaf is hidden; the user's wish is kept, so when the
    /// station releases the front the door comes back exactly as the user left it (no swing on the way back).
    /// </summary>
    public sealed class ChamberDoorState
    {
        /// <summary>What the user last asked for.</summary>
        public bool UserOpen { get; private set; }
        /// <summary>True while a hands-on station has retracted the front.</summary>
        public bool Retracted { get; private set; }
        /// <summary>Linear swing progress, 0 closed .. 1 open (ease it with <see cref="ChamberDoorLogic.Angle"/>).</summary>
        public float Progress { get; private set; }

        public bool FullyClosed => Progress <= 0f;
        public bool FullyOpen => Progress >= 1f;
        /// <summary>True while the leaf travels towards the user's wish.</summary>
        public bool Moving => !Retracted && Progress != (UserOpen ? 1f : 0f);
        /// <summary>The door collider is solid only while the leaf is shown and fully closed.</summary>
        public bool ColliderSolid => !Retracted && FullyClosed;

        /// <summary>The prompt shows only near the door, while the leaf is shown and at rest.</summary>
        public bool PromptVisible(bool inRange) => inRange && !Retracted && !Moving;

        /// <summary>Flips the user's wish (also mid-swing: the leaf turns back). Ignored while a station holds the front.</summary>
        public bool Toggle()
        {
            if (Retracted)
                return false;
            UserOpen = !UserOpen;
            return true;
        }

        /// <summary>Sets the user's wish directly (verification and tests); ignored while retracted.</summary>
        public bool SetUserOpen(bool open)
        {
            if (Retracted)
                return false;
            UserOpen = open;
            return true;
        }

        public void SetRetracted(bool retracted) => Retracted = retracted;

        /// <summary>Advances the swing. While retracted the leaf is hidden, so it settles at the user's wish at once.</summary>
        public void Tick(float deltaTime, float seconds = ChamberDoorLogic.SwingSeconds)
        {
            Progress = Retracted ? (UserOpen ? 1f : 0f) : ChamberDoorLogic.Step(Progress, UserOpen, deltaTime, seconds);
        }
    }
}
