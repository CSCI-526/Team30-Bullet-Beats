using UnityEngine;

/// <summary>
/// Finds the camera that is actually drawn on screen. The MainCamera-tagged camera is not always
/// the one players see (MainLevel draws player_start_cam on top of it).
/// </summary>
public static class DisplayCamera
{
    public static Camera Find()
    {
        Camera best = null;
        foreach (Camera cam in Camera.allCameras)
        {
            if (cam.targetTexture != null) continue;
            if (best == null || cam.depth > best.depth) best = cam;
        }
        return best;
    }
}
