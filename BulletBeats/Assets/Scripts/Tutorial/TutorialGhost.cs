using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A see-through copy of the player ship that acts out moves and shots for the <see cref="Tutorial"/>
/// (demonstrate, then do). It is built at runtime from the ship's meshes with the same unlit see-through
/// shader as the asteroid rings, so it needs no art and draws in WebGL. Moves run on unscaled time, so the
/// ghost keeps moving while the game is slowed down.
/// </summary>
public class TutorialGhost
{
    readonly PlayerMovement ship;
    readonly Transform muzzle;
    readonly GameObject root;
    readonly Material material;
    readonly Color color;
    readonly List<Renderer> shipRenderers = new List<Renderer>();
    Vector3 moveFrom, moveTo;
    float moveStart, moveSeconds;

    public TutorialGhost(PlayerMovement ship, Transform muzzle, Color color)
    {
        this.ship = ship;
        this.muzzle = muzzle;
        this.color = color;
        material = SeeThrough(color);

        root = new GameObject("GhostShip");
        root.transform.SetPositionAndRotation(ship.transform.position, ship.transform.rotation);
        root.transform.localScale = ship.transform.lossyScale;
        foreach (MeshRenderer source in ship.GetComponentsInChildren<MeshRenderer>())
        {
            shipRenderers.Add(source);
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            var part = new GameObject(source.name);
            part.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            part.transform.localScale = source.transform.lossyScale;
            part.transform.SetParent(root.transform, true);
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var meshRenderer = part.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }
        root.SetActive(false);
    }

    public bool Visible => root != null && root.activeSelf;

    /// <summary>
    /// Shows the ghost at <paramref name="at"/>. With <paramref name="inPlaceOfShip"/> the real ship is hidden
    /// while it shows (the ghost stands in for it), so a demo on the ship's own spot can be seen.
    /// </summary>
    public void Show(PlayerMovement.Spot at, bool inPlaceOfShip = false)
    {
        root.transform.position = ship.WorldPositionOf(at);
        moveSeconds = 0f;
        root.SetActive(true);
        SetShipVisible(!inPlaceOfShip);
    }

    public void Hide()
    {
        root.SetActive(false);
        SetShipVisible(true);
    }

    void SetShipVisible(bool visible)
    {
        foreach (Renderer r in shipRenderers)
            if (r != null) r.enabled = visible;
    }

    public void Destroy()
    {
        SetShipVisible(true);
        if (root != null) UnityEngine.Object.Destroy(root);
        UnityEngine.Object.Destroy(material);
    }

    /// <summary>Slides to <paramref name="spot"/> over <paramref name="seconds"/> (real time), easing out.</summary>
    public IEnumerator MoveTo(PlayerMovement.Spot spot, float seconds)
    {
        moveFrom = root.transform.position;
        moveTo = ship.WorldPositionOf(spot);
        moveStart = Time.unscaledTime;
        moveSeconds = Mathf.Max(0.01f, seconds);
        while (Time.unscaledTime - moveStart < moveSeconds) yield return null;
        root.transform.position = moveTo;
        moveSeconds = 0f;
    }

    /// <summary>Call every frame: the move, and a soft pulse.</summary>
    public void Update()
    {
        if (!Visible) return;
        if (moveSeconds > 0f)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - moveStart) / moveSeconds);
            root.transform.position = Vector3.LerpUnclamped(moveFrom, moveTo, 1f - (1f - t) * (1f - t));
        }
        Color c = color;
        c.a *= Mathf.Lerp(0.75f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
        material.color = c;
    }

    /// <summary>
    /// Fires a ghost bullet from the ghost's gun so it reaches <paramref name="target"/> when
    /// <paramref name="songTime"/> gets to <paramref name="arriveAt"/>, then pops the target
    /// (unless <paramref name="pop"/> is false). Follows the target, so it always lands.
    /// </summary>
    public IEnumerator Shoot(ChartTarget target, Func<float> songTime, float arriveAt, float flightSongSeconds, bool pop, Action<Vector3> landed = null)
    {
        while (target != null && songTime() < arriveAt - flightSongSeconds) yield return null;
        if (target == null) yield break;

        var bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bullet.name = "GhostBullet";
        UnityEngine.Object.Destroy(bullet.GetComponent<Collider>());
        bullet.GetComponent<MeshRenderer>().sharedMaterial = material;
        bullet.transform.localScale = Vector3.one * 0.9f;
        Vector3 from = MuzzlePosition();
        float launched = songTime();

        while (target != null)
        {
            float t = Mathf.Clamp01((songTime() - launched) / Mathf.Max(0.01f, arriveAt - launched));
            bullet.transform.position = Vector3.Lerp(from, target.transform.position, t);
            if (t >= 1f) break;
            yield return null;
        }

        Vector3 at = bullet.transform.position;
        UnityEngine.Object.Destroy(bullet);
        landed?.Invoke(at);
        if (pop && target != null) target.OnShot(null);
    }

    Vector3 MuzzlePosition()
    {
        if (muzzle == null) return root.transform.position;
        return root.transform.TransformPoint(ship.transform.InverseTransformPoint(muzzle.position));
    }

    static Material SeeThrough(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        return new Material(shader) { color = color };
    }
}
