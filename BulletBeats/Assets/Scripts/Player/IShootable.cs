/// <summary>
/// Anything the player's bullets can hit (enemies, asteroids, ...). Put it on the object that has
/// the collider or on one of its parents. Use <see cref="PlayerBullet.Damage"/> and
/// <see cref="PlayerBullet.Judgment"/> to decide what the hit does.
/// </summary>
public interface IShootable
{
    void OnShot(PlayerBullet bullet);
}
