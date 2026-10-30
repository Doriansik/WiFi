using UnityEngine;

#region Level Finish
// Meta rundy (usta): kto do nich wejdzie, konczy bieg. Pozycje graczy sprawdza serwer w PlacementManager.
public class LevelFinish : MonoBehaviour
{
    private const int FreeRowsAbove = 1;  // nikt nie moze zasypac wejscia do ust

    [SerializeField] private SpriteRenderer _spriteRenderer;

    public static LevelFinish Instance { get; private set; }

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

    public bool IsReachedBy(Bounds playerBounds)
    {
        return _spriteRenderer.bounds.Intersects(playerBounds);
    }
}
#endregion
