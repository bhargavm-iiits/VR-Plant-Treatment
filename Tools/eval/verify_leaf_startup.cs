if (!UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Play mode required.");
if (BTP.Core.SelectionState.Instance == null || BTP.Core.EnvironmentController.Instance == null)
    throw new System.Exception("Direct Lab startup did not initialize the bootstrap singletons.");
var display = UnityEngine.Object.FindAnyObjectByType<BTP.Lab.LabPlantDisplay>();
if (display == null || display.CurrentModel == null || display.GetComponent<BTP.Lab.LeafViewerFacing>() == null)
    throw new System.Exception("Direct Lab startup did not initialize the leaf display.");
if (UnityEngine.GameObject.Find("Week6") == null) throw new System.Exception("Timeline did not initialize.");
return "PASS: direct Lab Play mode startup, bootstrap, leaf display, facing and timeline initialized.";
