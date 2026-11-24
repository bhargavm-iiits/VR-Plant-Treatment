using System.Collections.Generic;
using BTP.Treatment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Tests
{
    public class ChamberDoorTests
    {
        // A 1 m door front on the floor plane: hinge on the left at x = 0, free edge at x = 1, outward towards -Z (the user).
        static readonly Vector2 Hinge = new Vector2(0f, 0f), Edge = new Vector2(1f, 0f), Outward = new Vector2(0f, -1f);

        // ------------------------------------------------------------------ range

        [Test]
        public void InRange_InFrontWithinRange_IsTrue_AndBeyondRange_IsFalse()
        {
            Assert.That(ChamberDoorLogic.InRange(new Vector2(0.5f, -0.8f), Hinge, Edge, Outward), Is.True);
            Assert.That(ChamberDoorLogic.InRange(new Vector2(0.5f, -1.29f), Hinge, Edge, Outward), Is.True);
            Assert.That(ChamberDoorLogic.InRange(new Vector2(0.5f, -1.31f), Hinge, Edge, Outward), Is.False, "Horizontal distance over 1.3 m.");
        }

        [Test]
        public void InRange_UsesTheNearestPointOnTheFront_NotItsCentre()
        {
            // 1.2 m in front of the free edge but 1.3+ m from the door's centre: still at the door.
            Assert.That(ChamberDoorLogic.InRange(new Vector2(1f, -1.2f), Hinge, Edge, Outward), Is.True);
        }

        [Test]
        public void InRange_BehindOrBesideTheDoor_IsFalse()
        {
            Assert.That(ChamberDoorLogic.InRange(new Vector2(0.5f, 0.5f), Hinge, Edge, Outward), Is.False, "Behind the glass (inside the chamber).");
            Assert.That(ChamberDoorLogic.InRange(new Vector2(-1f, -0.05f), Hinge, Edge, Outward), Is.False, "Beside the hinge, not in front.");
            Assert.That(ChamberDoorLogic.InRange(new Vector2(-0.4f, -0.6f), Hinge, Edge, Outward), Is.True, "Off to the side but still roughly in front.");
        }

        [Test]
        public void InRange_IgnoresHeight_AndAcceptsAHeadOnTheFront()
        {
            Assert.That(ChamberDoorLogic.InRange(new Vector2(0.5f, 0f), Hinge, Edge, Outward), Is.True);
        }

        // ------------------------------------------------------------------ easing

        [Test]
        public void Ease_IsSoftAtBothEnds_AndSymmetric()
        {
            Assert.That(ChamberDoorLogic.Ease(0f), Is.EqualTo(0f));
            Assert.That(ChamberDoorLogic.Ease(1f), Is.EqualTo(1f));
            Assert.That(ChamberDoorLogic.Ease(0.5f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(ChamberDoorLogic.Ease(0.05f), Is.LessThan(0.005f), "Slow start.");
            Assert.That(ChamberDoorLogic.Ease(0.95f), Is.GreaterThan(0.995f), "Damped stop.");
            Assert.That(ChamberDoorLogic.Ease(0.3f) + ChamberDoorLogic.Ease(0.7f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Swing_OpensToTheTargetInTheSwingTime_ThenClosesBackToZero()
        {
            var state = new ChamberDoorState();
            Assert.That(state.Toggle(), Is.True);
            var angles = new List<float>();
            // Seven 0.1 s frames of the 0.8 s swing: still on the way.
            for (var frame = 0; frame < 7; frame++)
            {
                state.Tick(0.1f);
                angles.Add(ChamberDoorLogic.Angle(state.Progress, ChamberDoorLogic.OpenAngle));
            }

            for (var i = 1; i < angles.Count; i++)
                Assert.That(angles[i], Is.GreaterThan(angles[i - 1]), "Opening only ever moves outwards.");
            Assert.That(state.Moving, Is.True);
            state.Tick(0.2f);
            Assert.That(state.FullyOpen, Is.True);
            Assert.That(state.Moving, Is.False);
            Assert.That(ChamberDoorLogic.Angle(state.Progress, ChamberDoorLogic.OpenAngle), Is.EqualTo(100f));

            Assert.That(state.Toggle(), Is.True);
            for (var frame = 0; frame < 9; frame++)
                state.Tick(0.1f);
            Assert.That(state.FullyClosed, Is.True);
            Assert.That(ChamberDoorLogic.Angle(state.Progress, ChamberDoorLogic.OpenAngle), Is.EqualTo(0f));
        }

        [Test]
        public void Swing_ReversesMidWay_WithoutJumping()
        {
            var state = new ChamberDoorState();
            state.Toggle();
            state.Tick(0.4f);
            var before = state.Progress;
            state.Toggle();
            state.Tick(0.016f);
            Assert.That(state.Progress, Is.LessThan(before));
            Assert.That(before - state.Progress, Is.LessThan(0.05f));
        }

        [Test]
        public void Collider_IsSolidOnlyWhenFullyClosed_AndPromptHidesWhileMoving()
        {
            var state = new ChamberDoorState();
            Assert.That(state.ColliderSolid, Is.True);
            Assert.That(state.PromptVisible(true), Is.True);
            Assert.That(state.PromptVisible(false), Is.False);
            state.Toggle();
            state.Tick(0.01f);
            Assert.That(state.ColliderSolid, Is.False, "Off as soon as the leaf moves.");
            Assert.That(state.PromptVisible(true), Is.False, "No prompt while the leaf swings.");
            state.Tick(1f);
            Assert.That(state.ColliderSolid, Is.False, "Off while open.");
            Assert.That(state.PromptVisible(true), Is.True);
        }

        // ------------------------------------------------------------------ station override

        [Test]
        public void StationRetract_OverridesTheUser_AndRestoresTheUsersChoiceOnRelease()
        {
            var state = new ChamberDoorState();
            state.Toggle();
            state.Tick(1f);
            Assert.That(state.FullyOpen, Is.True);

            state.SetRetracted(true);
            Assert.That(state.Toggle(), Is.False, "The station holds the front.");
            Assert.That(state.SetUserOpen(false), Is.False);
            Assert.That(state.PromptVisible(true), Is.False);
            Assert.That(state.ColliderSolid, Is.False);
            state.Tick(0.1f);
            Assert.That(state.UserOpen, Is.True);

            state.SetRetracted(false);
            state.Tick(0.016f);
            Assert.That(state.FullyOpen, Is.True, "Back exactly as the user left it.");
            Assert.That(state.Moving, Is.False);
        }

        [Test]
        public void StationRetract_OfAClosedDoor_ComesBackClosedAndSolid()
        {
            var state = new ChamberDoorState();
            state.SetRetracted(true);
            state.Tick(0.5f);
            state.SetRetracted(false);
            state.Tick(0.016f);
            Assert.That(state.FullyClosed, Is.True);
            Assert.That(state.ColliderSolid, Is.True);
        }

        [Test]
        public void StationRetract_MidSwing_SettlesAtTheUsersChoice()
        {
            var state = new ChamberDoorState();
            state.Toggle();
            state.Tick(0.3f);
            state.SetRetracted(true);
            state.Tick(0.016f);
            Assert.That(state.Progress, Is.EqualTo(1f));
            state.SetRetracted(false);
            Assert.That(state.Moving, Is.False);
        }

        // ------------------------------------------------------------------ clearance

        [Test]
        public void Turn_PositiveYaw_SwingsTheFreeEdgeOutwards()
        {
            var edge = ChamberDoorLogic.Turn(new Vector2(1f, 0f), 90f);
            Assert.That(edge.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(edge.y, Is.EqualTo(-1f).Within(1e-5f), "Towards -Z, the user's side.");
        }

        [Test]
        public void ClearAngle_IsTheFullSwing_WhenNothingIsInTheWay()
        {
            var hall = new[] { new Vector2(-5f, -5f), new Vector2(5f, -5f), new Vector2(5f, 5f), new Vector2(-5f, 5f) };
            Assert.That(ChamberDoorLogic.ClearAngle(Hinge, Edge, 100f, new List<Vector2[]>(), hall), Is.EqualTo(100f));
            Assert.That(ChamberDoorLogic.ClearAngle(Hinge, Edge, 100f, null, null), Is.EqualTo(100f));
        }

        [Test]
        public void ClearAngle_StopsShortOfAStationBase()
        {
            // A console base 0.6 m in front of the door, under the swing.
            var console = new[] { new Vector2(-0.5f, -1.6f), new Vector2(0.5f, -1.6f), new Vector2(0.5f, -0.6f), new Vector2(-0.5f, -0.6f) };
            var angle = ChamberDoorLogic.ClearAngle(Hinge, Edge, 100f, new List<Vector2[]> { console }, null);
            Assert.That(angle, Is.GreaterThan(10f).And.LessThan(100f));
            // At the limit the leaf keeps the margin; one more degree would not.
            var leaf = new[] { Hinge, Hinge + ChamberDoorLogic.Turn(Edge - Hinge, angle) };
            Assert.That(StationLayout.Distance(leaf, console), Is.GreaterThanOrEqualTo(ChamberDoorLogic.SwingMargin));
            var further = new[] { Hinge, Hinge + ChamberDoorLogic.Turn(Edge - Hinge, angle + 1f) };
            Assert.That(StationLayout.Distance(further, console), Is.LessThan(ChamberDoorLogic.SwingMargin));
        }

        [Test]
        public void ClearAngle_StaysInsideTheHall()
        {
            // The front wall 0.6 m in front of the door.
            var hall = new[] { new Vector2(-5f, -0.6f), new Vector2(5f, -0.6f), new Vector2(5f, 5f), new Vector2(-5f, 5f) };
            var angle = ChamberDoorLogic.ClearAngle(Hinge, Edge, 100f, null, hall);
            Assert.That(angle, Is.LessThan(40f));
            Assert.That(Mathf.Sin(angle * Mathf.Deg2Rad), Is.LessThanOrEqualTo(0.5f + 1e-4f), "Free edge at least 0.1 m from the wall.");
        }

        [Test]
        public void ClearAngle_DefaultStationLayout_LeavesTheTomatoChamberDoorItsFullSwing()
        {
            // Line space as in StationLayout: the chamber exterior is X [-w, 0], Z [0, d], the user is at -Z.
            const float width = 0.9f;
            var hall = Rect.MinMaxRect(-9.4f, -7f, 4.5f, 9.4f);
            var layout = StationLayout.Compute(width, 0.8f, hall, new Vector2(0.115f, -0.2f));
            var bases = new List<Vector2[]>();
            foreach (var s in layout.Stations)
                bases.Add(s.Footprint);
            var floor = new[] { new Vector2(hall.xMin, hall.yMin), new Vector2(hall.xMax, hall.yMin), new Vector2(hall.xMax, hall.yMax), new Vector2(hall.xMin, hall.yMax) };
            var angle = ChamberDoorLogic.ClearAngle(new Vector2(-width + 0.05f, -0.03f), new Vector2(-0.05f, 0f), 100f, bases, floor);
            Assert.That(angle, Is.EqualTo(100f));
        }

        // ------------------------------------------------------------------ prompt

        [Test]
        public void Prompt_IsAOneWordAction_NotASentence()
        {
            Assert.That(ChamberDoorPrompt.PromptLabel(false), Is.EqualTo("Open"), "Closed door: the button opens it.");
            Assert.That(ChamberDoorPrompt.PromptLabel(true), Is.EqualTo("Close"));
            Assert.That(ChamberDoorPrompt.PromptLabel(false), Does.Not.Contain(" ").And.Not.Contain("<"), "No sentence, no rich text.");
        }

        [Test]
        public void Prompt_Glyph_IsTheRightAButtonInVr_AndOOnTheKeyboard()
        {
            Assert.That(ChamberDoorPrompt.GlyphFor(false), Is.EqualTo("A"));
            Assert.That(ChamberDoorPrompt.GlyphFor(true), Is.EqualTo("O"));
        }

        [Test]
        public void Input_OnlyTheRightControllerTogglesTheDoor()
        {
            Assert.That(ChamberDoorPrompt.ToggleHand, Is.EqualTo(InteractorHandedness.Right), "Right A ({PrimaryButton}); left X is not read.");
        }

        [Test]
        public void PopIn_StartsSmallAndTransparent_OvershootsTo105_AndSettlesAtOne()
        {
            Assert.That(ChamberDoorPrompt.PopInScale(0f), Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(ChamberDoorPrompt.PopInScale(1f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(ChamberDoorPrompt.PopInScale(-1f), Is.EqualTo(0.6f).Within(1e-5f), "Clamped before the start.");
            Assert.That(ChamberDoorPrompt.PopInScale(2f), Is.EqualTo(1f).Within(1e-5f), "Clamped after the end.");
            Assert.That(ChamberDoorPrompt.PopInAlpha(0f), Is.EqualTo(0f));
            Assert.That(ChamberDoorPrompt.PopInAlpha(1f), Is.EqualTo(1f));

            var peak = 0f;
            for (var i = 0; i <= 1000; i++)
                peak = Mathf.Max(peak, ChamberDoorPrompt.PopInScale(i / 1000f));
            Assert.That(peak, Is.EqualTo(ChamberDoorPrompt.PopInPeak).Within(0.005f), "Ease-out-back overshoot.");
            Assert.That(ChamberDoorPrompt.PopInScale(0.5f), Is.GreaterThan(1f), "Past 1 by the middle (ease-out).");
        }

        [Test]
        public void PopOut_AndPress_CurvesHaveTheirEndpoints()
        {
            Assert.That(ChamberDoorPrompt.PopOutScale(0f), Is.EqualTo(1f));
            Assert.That(ChamberDoorPrompt.PopOutScale(1f), Is.EqualTo(ChamberDoorPrompt.PopOutTo).Within(1e-5f));
            Assert.That(ChamberDoorPrompt.PopOutAlpha(0f), Is.EqualTo(1f));
            Assert.That(ChamberDoorPrompt.PopOutAlpha(1f), Is.EqualTo(0f));

            Assert.That(ChamberDoorPrompt.PressScale(0f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(ChamberDoorPrompt.PressScale(0.5f), Is.EqualTo(ChamberDoorPrompt.PressSquash).Within(1e-5f), "Squashed mid-press.");
            Assert.That(ChamberDoorPrompt.PressScale(1f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(ChamberDoorPrompt.PressFlash(0f), Is.EqualTo(1f), "Brightest at once.");
            Assert.That(ChamberDoorPrompt.PressFlash(1f), Is.EqualTo(0f));
            Assert.That(ChamberDoorPrompt.RingFill(0f), Is.EqualTo(0f));
            var filled = ChamberDoorPrompt.RingFillSeconds / ChamberDoorPrompt.PressSeconds;
            Assert.That(ChamberDoorPrompt.RingFill(filled), Is.EqualTo(1f).Within(1e-5f), "Full after 0.15 s.");
            Assert.That(ChamberDoorPrompt.RingFill(filled * 0.5f), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void OpenDoor_ClosesFromEitherSideOfTheDoorLine_WithinCloseRange()
        {
            var hinge = new Vector2(0f, 0f);
            var edge = new Vector2(1f, 0f);
            var outward = new Vector2(0f, -1f);
            // Leaning into the chamber (past the door line): A could not open from here, but an open door closes.
            var inside = new Vector2(0.5f, 0.4f);
            Assert.That(ChamberDoorLogic.InRange(inside, hinge, edge, outward), Is.False, "the open rule needs the user in front");
            Assert.That(ChamberDoorLogic.InCloseRange(inside, hinge, edge), Is.True, "but an open door closes from inside its footprint");
            Assert.That(ChamberDoorLogic.InCloseRange(new Vector2(0.5f, -1.9f), hinge, edge), Is.True, "a step back in front");
            Assert.That(ChamberDoorLogic.InCloseRange(new Vector2(-1.5f, 0.3f), hinge, edge), Is.True, "beside the hinge");
            Assert.That(ChamberDoorLogic.InCloseRange(new Vector2(0.5f, -2.3f), hinge, edge), Is.False, "too far away");
            Assert.That(ChamberDoorLogic.CloseRange, Is.GreaterThan(ChamberDoorLogic.Range));
        }

        [Test]
        public void DoorPrompt_ShowsB_ToCloseAnOpenDoor()
        {
            Assert.That(ChamberDoorPrompt.GlyphFor(false, false), Is.EqualTo("A"), "A opens");
            Assert.That(ChamberDoorPrompt.GlyphFor(false, true), Is.EqualTo("B"), "B closes");
            Assert.That(ChamberDoorPrompt.GlyphFor(true, true), Is.EqualTo("O"), "the desktop keeps its key");
            Assert.That(ChamberDoorPrompt.PromptLabel(true), Is.EqualTo("Close"));
        }

        [Test]
        public void RightB_ClosesTheMenuFirst_ThenTheDoor_ElseFallsThrough()
        {
            var closes = 0;
            var menuClosed = 0;
            bool door() { closes++; return true; }
            bool notHere() => false;
            BTP.Core.VrControllerRig.AddBackHandler(door);
            try
            {
                Assert.That(BTP.Core.VrControllerRig.HandleBack(true, () => menuClosed++), Is.True);
                Assert.That(menuClosed, Is.EqualTo(1), "an open menu takes B");
                Assert.That(closes, Is.EqualTo(0), "the door is not touched while the menu is open");
                Assert.That(BTP.Core.VrControllerRig.HandleBack(false, () => menuClosed++), Is.True);
                Assert.That(closes, Is.EqualTo(1), "with the menu closed, B closes the door");
            }
            finally
            {
                BTP.Core.VrControllerRig.RemoveBackHandler(door);
            }

            BTP.Core.VrControllerRig.AddBackHandler(notHere);
            try
            {
                Assert.That(BTP.Core.VrControllerRig.HandleBack(false, null), Is.False, "away from an open door B keeps its old job");
            }
            finally
            {
                BTP.Core.VrControllerRig.RemoveBackHandler(notHere);
            }
        }
    }
}
