using UnityEngine;

#region Level Start
// Start rundy (lodowka): tu pojawiaja sie gracze. Mozna ja przesunac w scenie - spawn i zablokowane kratki pojda za nia.
public class LevelStart : MonoBehaviour
{
    private const int FreeRowsAbove = 2;  // miejsce na graczy nad lodowka - tam nic nie wolno postawic
    private const float SpawnSpread = 0.25f;
    private const int SpawnColumns = 5;

    [SerializeField] private SpriteRenderer _spriteRenderer;

    public static LevelStart Instance { get; private set; }

    public RectInt BlockedCells
    {
        get
        {
            RectInt cells = BuildGrid.GetCells(_spriteRenderer.bounds);
            return new RectInt(cells.x, cells.y, cells.width, cells.height + FreeRowsAbove);
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Gracze przenikaja sie nawzajem, wiec moga stac blisko siebie - lekkie przesuniecie, zeby bylo widac kazdego.
    public Vector3 GetSpawnPosition(int playerIndex)
    {
        Bounds bounds = _spriteRenderer.bounds;
        float offset = (playerIndex % SpawnColumns - SpawnColumns / 2) * SpawnSpread;
        return new Vector3(bounds.center.x + offset, bounds.max.y + 0.5f, 0f);
    }
}
#endregion
