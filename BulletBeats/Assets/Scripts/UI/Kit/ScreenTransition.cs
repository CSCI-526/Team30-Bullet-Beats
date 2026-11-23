using System.Collections;
using UnityEngine;

/// <summary>
/// The one set of screen animations every menu uses, so they all move alike: a quick pixel
/// "pop" in and out (stepped scale plus fade) and a smooth slide. All run on unscaled time,
/// because menus are shown while the game is paused. Start them with StartCoroutine.
/// </summary>
public static class ScreenTransition
{
    /// <summary>Seconds for a pop in or out. Short, so navigation never waits on it.</summary>
    public const float PopSeconds = 0.18f;
    /// <summary>Seconds for a slide (e.g. the iPod moving left). The rulebook suggests 300 to 450 ms.</summary>
    public const float SlideSeconds = 0.35f;
    /// <summary>Scale steps per pop, for the pixel-arcade feel instead of a smooth zoom.</summary>
    const int PopSteps = 4;

    /// <summary>Shows <paramref name="target"/>: grows from 85% to full size in steps while fading in.</summary>
    public static IEnumerator PopIn(RectTransform target, float delay = 0f)
    {
        target.gameObject.SetActive(true);
        CanvasGroup group = Group(target);
        group.alpha = 0f;
        target.localScale = Vector3.one * 0.85f;
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        for (float t = 0f; t < PopSeconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Ceil(t / PopSeconds * PopSteps) / PopSteps;
            target.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, k);
            group.alpha = k;
            yield return null;
        }
        target.localScale = Vector3.one;
        group.alpha = 1f;
    }

    /// <summary>Hides <paramref name="target"/>: shrinks a little in steps while fading out, then deactivates it.</summary>
    public static IEnumerator PopOut(RectTransform target)
    {
        CanvasGroup group = Group(target);
        for (float t = 0f; t < PopSeconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Ceil(t / PopSeconds * PopSteps) / PopSteps;
            target.localScale = Vector3.one * Mathf.Lerp(1f, 0.9f, k);
            group.alpha = 1f - k;
            yield return null;
        }
        group.alpha = 0f;
        target.gameObject.SetActive(false);
        target.localScale = Vector3.one;
    }

    /// <summary>Moves <paramref name="target"/> to <paramref name="to"/> with an ease-out.</summary>
    public static IEnumerator SlideTo(RectTransform target, Vector2 to, float seconds = SlideSeconds, float delay = 0f)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        Vector2 from = target.anchoredPosition;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / seconds, 3f);
            target.anchoredPosition = Vector2.LerpUnclamped(from, to, k);
            yield return null;
        }
        target.anchoredPosition = to;
    }

    /// <summary>Fades a CanvasGroup to <paramref name="alpha"/>.</summary>
    public static IEnumerator Fade(CanvasGroup group, float alpha, float seconds = PopSeconds)
    {
        float from = group.alpha;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, alpha, t / seconds);
            yield return null;
        }
        group.alpha = alpha;
    }

    static CanvasGroup Group(RectTransform target) =>
        target.TryGetComponent(out CanvasGroup group) ? group : target.gameObject.AddComponent<CanvasGroup>();
}
