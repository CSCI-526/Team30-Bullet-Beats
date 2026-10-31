using UnityEngine;

/// <summary>
/// The songs a player can pick. Lives in Resources so <see cref="GameFlow"/> can load it
/// without a scene reference. Each entry is one song on the shared gameplay scene.
/// </summary>
[CreateAssetMenu(fileName = "LevelCatalog", menuName = "Bullet Beats/Level Catalog")]
public class LevelCatalog : ScriptableObject
{
    public GameFlow.Level[] levels;
}
