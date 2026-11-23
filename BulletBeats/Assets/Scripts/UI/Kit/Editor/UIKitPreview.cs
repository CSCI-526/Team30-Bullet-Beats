using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders every piece of the UI kit (background, logo, ship, iPod, buttons in all states,
/// card frame, icons, fonts) into one PNG, to check the look without playing the game.
/// Menu: Bullet Beats > UI Kit Preview. Batch mode: -executeMethod UIKitPreview.RenderFromCommandLine
/// (writes UIKitPreview.png next to the project folder, or to the path after -previewOut).
/// </summary>
static class UIKitPreview
{
    [MenuItem("Bullet Beats/UI Kit Preview")]
    static void RenderFromMenu()
    {
        string path = EditorUtility.SaveFilePanel("Save UI kit preview", "", "UIKitPreview", "png");
        if (!string.IsNullOrEmpty(path)) Render(path);
    }

    static void RenderFromCommandLine()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-previewOut");
        string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("../UIKitPreview.png");
        Render(path);
    }

    static void Render(string path)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const int width = 1920, height = 1080;

        var cameraObject = new GameObject("Camera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 5f;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(width, height);
        Transform root = canvasObject.transform;

        MenuBackground.Create(root);

        Place(Image(root, UITheme.Logo, 560f), -560f, 300f);
        Place(Image(root, UITheme.MenuShip, 300f), -560f, -320f);
        Place(Image(root, UITheme.IPod, 330f, height: 470f), 720f, 180f);

        // Buttons: primary (orange outline) and secondary (blue) in normal / glow / pressed.
        float[] ys = { 300f, 180f, 60f };
        string[] states = { "_normal", "_hover", "_pressed" };
        for (int s = 0; s < 3; s++)
        {
            var primary = PixelButton.Create(root, "PLAY", null, UITheme.ButtonStyle.Primary, 420f, 92f, ys[s]);
            Shift(primary.transform, -10f);
            primary.GetComponent<Image>().sprite = UITheme.Art("btn_outline" + states[s]);
            var secondary = PixelButton.Create(root, "SONG SELECT", null, UITheme.ButtonStyle.Secondary, 420f, 92f, ys[s]);
            Shift(secondary.transform, 430f);
            secondary.GetComponent<Image>().sprite = UITheme.Art("btn_blue" + states[s]);
        }

        // A floating card with title, body text and icons.
        RectTransform card = PixelPanel.Create(root, new Vector2(900f, 380f), new Vector2(210f, -270f));
        var title = UITheme.Text(card, "LEVEL COMPLETED", 64f, UITheme.White);
        title.rectTransform.anchoredPosition = new Vector2(0f, 120f);
        title.rectTransform.sizeDelta = new Vector2(860f, 80f);
        var body = UITheme.Text(card, "Pink Panther  ·  Easy     Score 2,420", 30f, UITheme.White, display: false);
        body.rectTransform.anchoredPosition = new Vector2(0f, -10f);
        body.rectTransform.sizeDelta = new Vector2(860f, 40f);
        for (int i = 0; i < 3; i++)
        {
            Image star = PixelArt.IconImage(card, PixelArt.Icon.Star, 64f, UITheme.AccentYellow);
            star.rectTransform.anchoredPosition = new Vector2(-90f + 90f * i, 50f);
        }
        PixelArt.Icon[] icons = (PixelArt.Icon[])System.Enum.GetValues(typeof(PixelArt.Icon));
        for (int i = 0; i < icons.Length; i++)
        {
            Image icon = PixelArt.IconImage(card, icons[i], 46f, i % 2 == 0 ? UITheme.ElectricCyan : UITheme.ImpactOrange);
            icon.rectTransform.anchoredPosition = new Vector2(-375f + 75f * i, -110f);
        }

        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, image.EncodeToPNG());
        Debug.Log("UI kit preview written to " + path);
    }

    static RectTransform Image(Transform parent, string art, float width, float height = 0f)
    {
        var image = new GameObject(art, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.sprite = UITheme.Art(art);
        image.preserveAspect = true;
        image.rectTransform.sizeDelta = new Vector2(width, height > 0f ? height : width);
        if (height <= 0f && image.sprite != null)
            image.rectTransform.sizeDelta = new Vector2(width, width * image.sprite.rect.height / image.sprite.rect.width);
        return image.rectTransform;
    }

    static void Place(RectTransform rect, float x, float y) => rect.anchoredPosition = new Vector2(x, y);

    static void Shift(Transform t, float x)
    {
        var rect = (RectTransform)t;
        rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
    }
}
