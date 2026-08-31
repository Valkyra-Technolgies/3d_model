using System;
using UnityEngine;

/// <summary>
/// OPTIONAL example showing how to wire a real AI/backend response into
/// ArmTreatmentVisualizer. This is a stub - replace the networking code
/// (UnityWebRequest, WebSocket, SignalR, etc.) with whatever your backend
/// actually uses. The important part is the last line: it just calls
/// armVisualizer.UpdateFromDetection(...).
///
/// Expected JSON shape from the backend, e.g.:
/// { "body_part": "arm", "treatment_area": "forearm" }
/// </summary>
public class AIBackendConnector : MonoBehaviour
{
    [Tooltip("Drag the GameObject that has the ArmTreatmentVisualizer component on it here.")]
    public ArmTreatmentVisualizer armVisualizer;

    [Serializable]
    private class DetectionResult
    {
        public string body_part;
        public string treatment_area;
    }

    /// <summary>
    /// Call this with the raw JSON string received from your backend
    /// (e.g. from a UnityWebRequest.downloadHandler.text, a WebSocket
    /// message callback, etc.).
    /// </summary>
    public void ReceiveDetectionJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || armVisualizer == null)
            return;

        DetectionResult result = JsonUtility.FromJson<DetectionResult>(json);
        if (result == null)
            return;

        armVisualizer.UpdateFromDetection(result.body_part, result.treatment_area);
    }

    /// <summary>
    /// Call this directly if your backend gives you already-parsed values
    /// instead of raw JSON (e.g. from a Python-to-Unity bridge, gRPC, etc.).
    /// </summary>
    public void ReceiveDetectionValues(string bodyPart, string treatmentArea)
    {
        if (armVisualizer == null)
            return;

        armVisualizer.UpdateFromDetection(bodyPart, treatmentArea);
    }
}
