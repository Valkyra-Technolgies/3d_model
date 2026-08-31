using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// EDITOR-ONLY automation. Put this file in an "Editor" folder anywhere under Assets
/// (e.g. Assets/Editor/ArmDemoSceneBuilder.cs) - it must NOT be in the same folder as
/// your runtime scripts (ArmTreatmentVisualizer.cs, AIBackendConnector.cs), which should
/// stay in a regular folder like Assets/Scripts.
///
/// Run it from the menu: Tools > Arm Demo > Build Scene (One-Click Setup)
///
/// It will, in one click:
///  1. Find your imported arm model (any Model asset with "arm" in its name).
///  2. Instantiate it into the scene as "ArmModel" and strip any imported Camera/Light.
///  3. Locate the real arm bones (RightUpperArm/RightLowerArm/RightHand, falling back to
///     Left* if the right side isn't found) and compute anatomically correct positions,
///     rotations and sizes for the 5 treatment points directly from the actual bone
///     transforms - no manual placement needed.
///  4. Build a red "HighlightBox" cube + material.
///  5. Create "ArmTreatmentController" with the ArmTreatmentVisualizer component and wire
///     every Inspector field (Arm Model, Highlight Box, Treatment Areas list).
///  6. Build a "DemoCanvas" with 7 buttons (Detect: Arm / 5 regions / No Detection),
///     each wired directly to the matching public method - no scripting needed on your end.
///  7. Set the arm model and highlight box inactive so the scene starts hidden, like the
///     spec requires.
///
/// Safe to re-run: it deletes and rebuilds its own objects each time, so you can run it
/// again after re-importing a different model.
/// </summary>
public static class ArmDemoSceneBuilder
{
    private const float ButtonWidth = 170f;
    private const float ButtonHeight = 52f;
    private const float ButtonSpacing = 12f;

    [MenuItem("Tools/Arm Demo/Build Scene (One-Click Setup)")]
    public static void BuildScene()
    {
        Undo.SetCurrentGroupName("Build Arm Demo Scene");
        int undoGroup = Undo.GetCurrentGroup();

        // ---- 0. Clean up any previous run ----
        DestroyIfExists("ArmModel");
        DestroyIfExists("HighlightBox");
        DestroyIfExists("ArmTreatmentController");
        DestroyIfExists("DemoCanvas");

        // ---- 1. Find the imported arm model ----
        GameObject armPrefab = FindArmModelAsset();
        if (armPrefab == null)
        {
            Debug.LogError("[ArmDemoSceneBuilder] Couldn't find an imported Model asset with \"arm\" in its name. Import arm.fbx into the project first, then run this again.");
            return;
        }

        // ---- 2. Instantiate the arm into the scene ----
        GameObject armInstance = (GameObject)PrefabUtility.InstantiatePrefab(armPrefab);
        armInstance.name = "ArmModel";
        Undo.RegisterCreatedObjectUndo(armInstance, "Create ArmModel");

        foreach (var cam in armInstance.GetComponentsInChildren<Camera>(true))
            Object.DestroyImmediate(cam.gameObject);
        foreach (var light in armInstance.GetComponentsInChildren<Light>(true))
            Object.DestroyImmediate(light.gameObject);

        // ---- 3. Locate the real bones and build the 5 treatment points ----
        string side = "Right";
        Transform upperArm = FindBone(armInstance.transform, side + "UpperArm");
        if (upperArm == null)
        {
            side = "Left";
            upperArm = FindBone(armInstance.transform, side + "UpperArm");
        }
        Transform lowerArm = upperArm != null ? FindBone(armInstance.transform, side + "LowerArm") : null;
        Transform hand = lowerArm != null ? FindBone(armInstance.transform, side + "Hand") : null;
        Transform middleProximal = hand != null ? FindBone(armInstance.transform, side + "MiddleProximal") : null;

        if (upperArm == null || lowerArm == null || hand == null)
        {
            Debug.LogError("[ArmDemoSceneBuilder] Could not find the expected arm bones (UpperArm / LowerArm / Hand) under ArmModel. " +
                            "Your rig may use different bone names - the arm model and highlight box were still created, " +
                            "but you'll need to create the 5 Point_* transforms manually (see Setup_Instructions.md, Step 2).");
        }
        else
        {
            float upperLen = Vector3.Distance(upperArm.position, lowerArm.position);
            float lowerLen = Vector3.Distance(lowerArm.position, hand.position);
            float handLen = middleProximal != null ? Vector3.Distance(hand.position, middleProximal.position) : lowerLen * 0.5f;

            Transform ptUpperArm = CreatePoint("Point_UpperArm", upperArm,
                Vector3.Lerp(upperArm.position, lowerArm.position, 0.5f),
                lowerArm.position - upperArm.position, upperLen * 0.7f, 0.35f);

            Transform ptElbow = CreatePoint("Point_Elbow", lowerArm,
                lowerArm.position,
                hand.position - upperArm.position, Mathf.Min(upperLen, lowerLen) * 0.4f, 0.9f);

            Transform ptForearm = CreatePoint("Point_Forearm", lowerArm,
                Vector3.Lerp(lowerArm.position, hand.position, 0.5f),
                hand.position - lowerArm.position, lowerLen * 0.7f, 0.35f);

            Transform ptWrist = CreatePoint("Point_Wrist", hand,
                hand.position,
                (middleProximal != null ? middleProximal.position : hand.position) - lowerArm.position,
                lowerLen * 0.3f, 0.9f);

            Vector3 handWorldPos = middleProximal != null
                ? Vector3.Lerp(hand.position, middleProximal.position, 0.8f)
                : hand.position + (hand.position - lowerArm.position).normalized * handLen * 0.5f;
            Vector3 handDir = (middleProximal != null ? middleProximal.position : hand.position) - hand.position;
            if (handDir.sqrMagnitude < 0.0001f) handDir = hand.position - lowerArm.position;
            Transform ptHand = CreatePoint("Point_Hand", hand, handWorldPos, handDir, handLen * 0.9f, 0.5f);

            // ---- 4. Highlight box + red material ----
            GameObject highlightBox = CreateHighlightBox();

            // ---- 5. Controller + wiring ----
            GameObject controllerGO = new GameObject("ArmTreatmentController");
            Undo.RegisterCreatedObjectUndo(controllerGO, "Create ArmTreatmentController");
            var visualizer = controllerGO.AddComponent<ArmTreatmentVisualizer>();

            SerializedObject so = new SerializedObject(visualizer);
            so.FindProperty("armModel").objectReferenceValue = armInstance;
            so.FindProperty("highlightBox").objectReferenceValue = highlightBox;

            SerializedProperty areasProp = so.FindProperty("treatmentAreas");
            areasProp.ClearArray();
            var entries = new (string area, Transform point)[]
            {
                ("upper_arm", ptUpperArm),
                ("elbow", ptElbow),
                ("forearm", ptForearm),
                ("wrist", ptWrist),
                ("hand", ptHand),
            };
            for (int i = 0; i < entries.Length; i++)
            {
                areasProp.InsertArrayElementAtIndex(i);
                var elem = areasProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("areaName").stringValue = entries[i].area;
                elem.FindPropertyRelative("point").objectReferenceValue = entries[i].point;
            }
            so.ApplyModifiedProperties();

            // ---- 6. Demo Canvas + buttons ----
            BuildDemoCanvas(visualizer);

            // ---- 7. Hide by default ----
            armInstance.SetActive(false);
            highlightBox.SetActive(false);

            Selection.activeGameObject = controllerGO;
            EditorGUIUtility.PingObject(controllerGO);

            Debug.Log($"[ArmDemoSceneBuilder] Done. Used bones on the '{side}' side. Press Play, then click the buttons in the bottom row of the Game view to demo.");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // -----------------------------------------------------------------

    private static void DestroyIfExists(string name)
    {
        var go = GameObject.Find(name);
        if (go != null)
            Undo.DestroyObjectImmediate(go);
    }

    private static GameObject FindArmModelAsset()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Contains("arm"))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    private static Transform FindBone(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindBone(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Creates a treatment-area empty at a world position, oriented along the bone,
    /// elongated along whichever local axis best matches the given world direction hint.</summary>
    private static Transform CreatePoint(string name, Transform parentBone, Vector3 worldPos, Vector3 worldDirHint, float length, float thicknessRatio)
    {
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parentBone, false);
        go.transform.position = worldPos;
        go.transform.rotation = parentBone.rotation;
        go.transform.localScale = ElongatedScale(parentBone, worldDirHint, Mathf.Max(length, 0.01f), thicknessRatio);
        return go.transform;
    }

    private static Vector3 ElongatedScale(Transform anchor, Vector3 worldDirHint, float length, float thicknessRatio)
    {
        float thickness = Mathf.Max(length * thicknessRatio, 0.005f);
        Vector3 scale = new Vector3(thickness, thickness, thickness);

        if (worldDirHint.sqrMagnitude < 0.0001f)
        {
            scale.z = length;
            return scale;
        }

        Vector3 localDir = anchor.InverseTransformDirection(worldDirHint.normalized);
        Vector3 abs = new Vector3(Mathf.Abs(localDir.x), Mathf.Abs(localDir.y), Mathf.Abs(localDir.z));

        if (abs.x >= abs.y && abs.x >= abs.z) scale.x = length;
        else if (abs.y >= abs.x && abs.y >= abs.z) scale.y = length;
        else scale.z = length;

        return scale;
    }

    private static GameObject CreateHighlightBox()
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "HighlightBox";
        Undo.RegisterCreatedObjectUndo(box, "Create HighlightBox");

        var collider = box.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        const string matPath = "Assets/Materials/Mat_HighlightRed.mat";
        Material redMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (redMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("HDRP/Lit")
                             ?? Shader.Find("Standard")
                             ?? Shader.Find("Unlit/Color");
            redMat = new Material(shader);
            if (redMat.HasProperty("_BaseColor")) redMat.SetColor("_BaseColor", Color.red);
            if (redMat.HasProperty("_Color")) redMat.SetColor("_Color", Color.red);
            AssetDatabase.CreateAsset(redMat, matPath);
        }
        box.GetComponent<Renderer>().sharedMaterial = redMat;

        return box;
    }

    private static void BuildDemoCanvas(ArmTreatmentVisualizer visualizer)
    {
        GameObject canvasGO = new GameObject("DemoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create DemoCanvas");
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem", typeof(EventSystem));
            Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
#if ENABLE_INPUT_SYSTEM
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            esGO.AddComponent<StandaloneInputModule>();
#endif
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        string[] labels = { "Detect: Arm", "Upper Arm", "Elbow", "Forearm", "Wrist", "Hand", "No Detection" };
        float totalWidth = labels.Length * ButtonWidth + (labels.Length - 1) * ButtonSpacing;
        float startX = -totalWidth / 2f + ButtonWidth / 2f;

        for (int i = 0; i < labels.Length; i++)
        {
            float x = startX + i * (ButtonWidth + ButtonSpacing);
            Button btn = CreateButton(canvasGO.transform, "Btn_" + labels[i].Replace(" ", "").Replace(":", ""), labels[i],
                new Vector2(x, 60f), new Vector2(ButtonWidth, ButtonHeight), font);

            switch (i)
            {
                case 0: UnityEventTools.AddVoidPersistentListener(btn.onClick, visualizer.ShowArm); break;
                case 1: UnityEventTools.AddStringPersistentListener(btn.onClick, visualizer.SetTreatmentArea, "upper_arm"); break;
                case 2: UnityEventTools.AddStringPersistentListener(btn.onClick, visualizer.SetTreatmentArea, "elbow"); break;
                case 3: UnityEventTools.AddStringPersistentListener(btn.onClick, visualizer.SetTreatmentArea, "forearm"); break;
                case 4: UnityEventTools.AddStringPersistentListener(btn.onClick, visualizer.SetTreatmentArea, "wrist"); break;
                case 5: UnityEventTools.AddStringPersistentListener(btn.onClick, visualizer.SetTreatmentArea, "hand"); break;
                case 6: UnityEventTools.AddVoidPersistentListener(btn.onClick, visualizer.ClearDetection); break;
            }
        }
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size, Font font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.12f, 0.92f);

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(go.transform, false);
        var textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var text = textGO.GetComponent<Text>();
        text.text = label;
        text.font = font;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.fontSize = 16;
        text.raycastTarget = false;

        return go.GetComponent<Button>();
    }
}
