using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the health and menu systems to every gameplay scene (any scene with a
/// <see cref="PlayerShooter"/>) when it loads, unless the scene already has them. This keeps them
/// out of the scene and prefab files, which are in Git LFS and can't be merged. Once someone adds
/// <see cref="PlayerHealth"/> / <see cref="GameFlow"/> to the scene by hand, this does nothing.
/// </summary>
static class GameBootstrap
{
    // The first scene is already loaded by AfterSceneLoad; later loads come through sceneLoaded.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SetUpScene();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) SetUpScene();
    }

    static void SetUpScene()
    {
        PlayerShooter shooter = Object.FindFirstObjectByType<PlayerShooter>();
        if (shooter == null) return;

        if (Object.FindFirstObjectByType<PlayerHealth>() == null)
            shooter.gameObject.AddComponent<PlayerHealth>();
        PlayerHealth health = Object.FindFirstObjectByType<PlayerHealth>();
        if (health.GetComponent<HealthHUD>() == null)
            health.gameObject.AddComponent<HealthHUD>();

        if (Object.FindFirstObjectByType<GameFlow>() == null)
            new GameObject("GameFlow", typeof(GameFlow), typeof(GameMenus));
    }
}
