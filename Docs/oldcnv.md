# Previous Conversation Summary (VR Plant Disease, BTP Project)

## 1. Requests and intent

### Earlier phases (complete)
- VR Plant Disease project in Unity 6000.5.5f1 with URP, following `Docs/PLAN.md`.
- The Farm scene is `Assets/Scenes/Envirornment.unity`. Lab and XRBootstrap are separate scenes.
- The demo is Tomato Early Blight over 6 weeks. Grape and Peach are left out.
- The Quest APK and the Windows build are both done. 16/16 tests pass, and preview cameras were added.

### Follow-up requests
- **"save it as abc.md" / "do it"**: `abc.md` in the project root is an exact copy of the implementation plan.
- **"VR controllers not working with keyboard for demo, analyse and fix it"**: added a keyboard/mouse demo mode so the project can be shown without a headset. Done and verified.
- **"controllers are moving fast with user, controllers alone are moving, fix it"**: fixed and verified in the Editor.

## 2. Key technical concepts
- **XR Interaction Toolkit 3.5.1**
  - The mouse ray is an `XRRayInteractor`. Its select and UI-press inputs are set to `XRInputButtonReader.InputSourceMode.ManualValue` and fed with `QueueManualState`.
  - World-space UI goes through `TrackedDeviceGraphicRaycaster` and `XRUIInputModule`.
- **XR Interaction Simulator**
  - It starts by itself through `XRInteractionSimulatorLoader`, controlled by `XRDeviceSimulatorSettings.m_AutomaticallyInstantiateSimulatorPrefab`.
  - It creates `XRSimulatedHMD` and `XRSimulatedController` devices that move from the keyboard. This was the cause of the "controllers moving alone" bug.
- **Head tracking**
  - The head camera has a `TrackedPoseDriver`, which is turned off in desktop mode.
  - Without a headset, XROrigin floor mode raises the camera offset, so eye height is set in world space instead.
- **Headset detection** checks `XRSettings.isDeviceActive`, then `XRDisplaySubsystem.running`, then any `InputSystem` device of type XRHMD that is not an XRSimulated* type.

## 3. Files and code
- **`abc.md`** (project root): copy of the implementation plan.
- **`Assets/_Project/Scripts/Runtime/Core/DesktopDemoControls.cs`** (new)
  - When no headset is active:
    - The mouse cursor acts as the controller ray. Left click selects plants and presses buttons.
    - Hold the right mouse button to look around.
    - WASD or the arrow keys walk, Shift makes it faster, and Q/E turn.
    - L enters the Lab, F returns to the Farm, R recenters (handled by `RecenterControl`), and H toggles help.
  - The eye height is kept at 1.6 m.
  - It turns off the `TrackedPoseDriver` while active and switches back automatically when a real headset becomes active.
- **`Assets/_Project/Scripts/Editor/BootstrapSceneBuilder.cs`** now adds `DesktopDemoControls` to the rig.
- **`Assets/Scenes/XRBootstrap.unity`**: the component is on the XR Origin, with `rigRoot` and `head` wired.
- **`Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset`**: `m_AutomaticallyInstantiateSimulatorPrefab: 0`, set through SerializedObject and SaveAssets.
- **Test helper**: `Tools/eval/desktop_test.cs` simulates clicks on plants and UI buttons and reports state.

## 4. Errors and fixes
| Problem | Fix |
|---|---|
| New script did not compile | ImportAsset ForceUpdate + `RequestScriptCompilation` |
| `interactionLayers` cast error | Removed the cast |
| Mouse ray only hit the ground (camera at 2.96 m) | `KeepEyeHeight()`, and the ray is aimed after look and height correction |
| UI click test failed at first | The ray was inactive in the test harness; clicking Enter Lab now loads the Lab |
| Controllers moving on their own | XR Interaction Simulator was still auto-spawning; disabled properly through SerializedObject |
| Desktop mode turned off in Play mode | The simulated HMD was treated as a real headset; XRSimulated* devices are now excluded |

## 5. Verified in Play mode
- Without a headset, desktop mode turns on by itself.
- Clicking the tomato with the mouse selects it (`selected=tomato`).
- Clicking Enter Lab loads the Lab.
- No simulator object appears, and the controllers no longer move on their own.
- The ray highlights plants under the cursor.
- **Not tested on real hardware:** a headset connected over Link.

## 6. Final reply given
The controllers were moving on their own because Unity's XR controller simulator was still running alongside the keyboard/mouse controls. It uses the same keys, so every key press or mouse move also moved its fake controllers.

Changes:
- The simulator's auto-start is now off, set from inside Unity so it stays saved.
- The headset check ignores the simulator's fake headset. A real headset counts when XR is actually rendering, which also covers Quest Link and PC VR.

## 7. Pending
- Test with a real headset to confirm the demo mode stays off and the headset and controllers track together.
- Rebuild `Builds/Windows/PlantDiseaseVR.exe`; the current exe predates desktop mode. The active platform is Android.
