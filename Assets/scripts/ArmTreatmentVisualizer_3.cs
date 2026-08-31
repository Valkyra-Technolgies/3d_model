using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls visibility of a 3D arm model and moves/shows a red highlight box
/// over predefined treatment regions on the arm (e.g. "upper_arm", "elbow",
/// "forearm", "wrist", "hand").
///
/// Designed to be driven by an external AI/backend: call
/// UpdateFromDetection(bodyPart, treatmentArea) whenever a new detection
/// result comes in (network response, voice command, manual UI selection, etc.).
///
/// Behavior:
///  - Arm model and highlight box are both hidden on start.
///  - If bodyPart == "arm" (case-insensitive), the arm model is shown.
///  - If treatmentArea matches one of the entries in treatmentAreas, the
///    highlight box snaps to that Transform's position/rotation/scale and
///    is shown.
///  - If bodyPart is anything other than "arm", both the arm model and the
///    highlight box are hidden.
/// </summary>
public class ArmTreatmentVisualizer : MonoBehaviour
{
    [Serializable]
    public class TreatmentAreaPoint
    {
        [Tooltip("Must match the treatment area string sent by the backend, e.g. \"forearm\". Matching is case-insensitive.")]
        public string areaName;

        [Tooltip("Empty GameObject positioned/rotated/scaled at this treatment location on the arm model.")]
        public Transform point;
    }

    [Header("Model & Highlight References")]
    [Tooltip("The 3D arm model GameObject. Hidden by default; shown only when the detected body part is 'arm'.")]
    public GameObject armModel;

    [Tooltip("The red highlight box (e.g. a Cube) that marks the treatment area.")]
    public GameObject highlightBox;

    [Header("Predefined Treatment Areas")]
    [Tooltip("List every treatment region here. areaName is matched case-insensitively against incoming strings.")]
    public List<TreatmentAreaPoint> treatmentAreas = new List<TreatmentAreaPoint>();

    [Header("Debug / Current State (read-only)")]
    [SerializeField] private string currentBodyPart = "";
    [SerializeField] private string currentTreatmentArea = "";

    private Dictionary<string, Transform> areaLookup;

    private void Awake()
    {
        BuildLookup();
        HideAll();
    }

    /// <summary>Builds a fast case-insensitive lookup from the Inspector list.</summary>
    private void BuildLookup()
    {
        areaLookup = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in treatmentAreas)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.areaName) || entry.point == null)
                continue;

            if (areaLookup.ContainsKey(entry.areaName))
            {
                Debug.LogWarning($"[ArmTreatmentVisualizer] Duplicate treatment area name '{entry.areaName}' - only the first entry will be used.");
                continue;
            }

            areaLookup.Add(entry.areaName, entry.point);
        }
    }

    // -----------------------------------------------------------------
    // PUBLIC API - call these from your AI/backend integration, a UI
    // dropdown, voice recognition, etc.
    // -----------------------------------------------------------------

    /// <summary>
    /// Main entry point for external systems. Call this whenever a new
    /// detection/selection result arrives.
    /// </summary>
    /// <param name="bodyPart">e.g. "arm", "leg", "shoulder"...</param>
    /// <param name="treatmentArea">e.g. "upper_arm", "elbow", "forearm", "wrist", "hand"</param>
    public void UpdateFromDetection(string bodyPart, string treatmentArea)
    {
        currentBodyPart = bodyPart ?? "";
        currentTreatmentArea = treatmentArea ?? "";
        Refresh();
    }

    /// <summary>Update only the body part, keeping the last known treatment area.</summary>
    public void SetBodyPart(string bodyPart)
    {
        currentBodyPart = bodyPart ?? "";
        Refresh();
    }

    /// <summary>Update only the treatment area, keeping the last known body part.</summary>
    public void SetTreatmentArea(string treatmentArea)
    {
        currentTreatmentArea = treatmentArea ?? "";
        Refresh();
    }

    // -----------------------------------------------------------------
    // ZERO-ARGUMENT CONVENIENCE WRAPPERS
    // These exist so a Unity UI Button's OnClick() list can call them
    // directly with no parameter to type in the Inspector - handy for
    // wiring up a quick demo UI without touching code.
    // -----------------------------------------------------------------

    /// <summary>Convenience wrapper for a UI Button: marks the body part as "arm".</summary>
    public void ShowArm() => SetBodyPart("arm");

    /// <summary>Convenience wrapper for a UI Button: simulates "no arm detected", hiding everything.</summary>
    public void ClearDetection() => SetBodyPart("");

    // -----------------------------------------------------------------
    // INTERNAL LOGIC
    // -----------------------------------------------------------------

    private void Refresh()
    {
        bool isArm = string.Equals((currentBodyPart ?? "").Trim(), "arm", StringComparison.OrdinalIgnoreCase);

        if (!isArm)
        {
            HideAll();
            return;
        }

        // Show the arm model.
        if (armModel != null && !armModel.activeSelf)
            armModel.SetActive(true);

        // Try to move & show the highlight box at the requested treatment area.
        string key = (currentTreatmentArea ?? "").Trim();
        if (areaLookup != null && areaLookup.TryGetValue(key, out Transform target))
        {
            ShowHighlightAt(target);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(key))
                Debug.LogWarning($"[ArmTreatmentVisualizer] No treatment area transform registered for '{key}'.");

            // Arm is shown, but no valid treatment area is selected yet - keep the box hidden.
            if (highlightBox != null)
                highlightBox.SetActive(false);
        }
    }

    private void ShowHighlightAt(Transform target)
    {
        if (highlightBox == null || target == null)
            return;

        highlightBox.transform.position = target.position;
        highlightBox.transform.rotation = target.rotation;
        highlightBox.transform.localScale = target.localScale;

        if (!highlightBox.activeSelf)
            highlightBox.SetActive(true);
    }

    private void HideAll()
    {
        if (armModel != null && armModel.activeSelf)
            armModel.SetActive(false);

        if (highlightBox != null && highlightBox.activeSelf)
            highlightBox.SetActive(false);
    }

    // -----------------------------------------------------------------
    // QUICK MANUAL TESTS
    // Right-click the component header (or click the gear icon) in the
    // Inspector while in Play Mode to run these without any backend.
    // -----------------------------------------------------------------

    [ContextMenu("Test/Arm - Upper Arm")]
    private void Test_UpperArm() => UpdateFromDetection("arm", "upper_arm");

    [ContextMenu("Test/Arm - Elbow")]
    private void Test_Elbow() => UpdateFromDetection("arm", "elbow");

    [ContextMenu("Test/Arm - Forearm")]
    private void Test_Forearm() => UpdateFromDetection("arm", "forearm");

    [ContextMenu("Test/Arm - Wrist")]
    private void Test_Wrist() => UpdateFromDetection("arm", "wrist");

    [ContextMenu("Test/Arm - Hand")]
    private void Test_Hand() => UpdateFromDetection("arm", "hand");

    [ContextMenu("Test/Not Arm (hide everything)")]
    private void Test_NotArm() => UpdateFromDetection("leg", "");
}
