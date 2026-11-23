using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Blurred, dimmed copy of the game behind a floating card (level start, Level Completed, Game Over,
/// Pause). It grabs the screen once when shown, shrinks it in a few bilinear steps (a cheap blur that
/// works in WebGL without post-processing) and draws it full screen with a dark tint on top.
/// The game is frozen or about to start whenever a card is up, so one still frame is enough.
/// </summary>
public class BlurBackdrop : MonoBehaviour
{
    [Tooltip("How much the grabbed frame is shrunk; higher is blurrier.")]
    public int downsample = 16;
    [Range(0f, 1f)] public float dim = 0.45f;

    RawImage image;
    Image shade;
    RenderTexture grab, small;

    /// <summary>Adds a hidden backdrop as the back-most child of <paramref name="parent"/>.</summary>
    public static BlurBackdrop Create(Transform parent)
    {
        var root = new GameObject("BlurBackdrop", typeof(RectTransform), typeof(BlurBackdrop));
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.SetAsFirstSibling();
        Stretch(rect);

        var backdrop = root.GetComponent<BlurBackdrop>();
        backdrop.image = new GameObject("Frame", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        backdrop.image.rectTransform.SetParent(rect, false);
        Stretch(backdrop.image.rectTransform);
        backdrop.image.raycastTarget = false;

        backdrop.shade = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        backdrop.shade.rectTransform.SetParent(rect, false);
        Stretch(backdrop.shade.rectTransform);
        // Blocks clicks from reaching the game or the HUD behind the card.
        backdrop.shade.raycastTarget = true;
        root.SetActive(false);
        return backdrop;
    }

    /// <summary>Grabs the current frame at the end of this frame, then shows it blurred.
    /// <paramref name="onReady"/> runs once it's up, so the card can appear on top of it.</summary>
    public void Show(MonoBehaviour runner, System.Action onReady = null)
    {
        runner.StartCoroutine(Grab(onReady));
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    IEnumerator Grab(System.Action onReady)
    {
        // The card's own canvas is hidden for this one frame so it isn't in the grab.
        yield return new WaitForEndOfFrame();
        int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
        if (grab == null || grab.width != w || grab.height != h)
        {
            if (grab != null) grab.Release();
            grab = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        }
        ScreenCapture.CaptureScreenshotIntoRenderTexture(grab);

        // Halve the size a few times with bilinear filtering: each step averages 2 x 2 pixels.
        RenderTexture current = grab;
        int targetW = Mathf.Max(8, w / downsample), targetH = Mathf.Max(8, h / downsample);
        int cw = w, ch = h;
        while (cw / 2 >= targetW && ch / 2 >= targetH)
        {
            cw /= 2; ch /= 2;
            var next = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32);
            next.filterMode = FilterMode.Bilinear;
            Graphics.Blit(current, next);
            if (current != grab) RenderTexture.ReleaseTemporary(current);
            current = next;
        }
        if (small == null || small.width != cw || small.height != ch)
        {
            if (small != null) small.Release();
            small = new RenderTexture(cw, ch, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
        }
        Graphics.Blit(current, small);
        if (current != grab) RenderTexture.ReleaseTemporary(current);

        image.texture = small;
        // On graphics APIs whose textures start at the top, the screen grab comes out upside down.
        image.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        shade.color = new Color(UITheme.SpaceBlack.r, UITheme.SpaceBlack.g, UITheme.SpaceBlack.b, dim);
        gameObject.SetActive(true);
        onReady?.Invoke();
    }

    void OnDestroy()
    {
        if (grab != null) grab.Release();
        if (small != null) small.Release();
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
