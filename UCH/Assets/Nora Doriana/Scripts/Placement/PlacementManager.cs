using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#region Placement Manager
// Runda jak w imprezowych platformowkach: losowa pula obiektow (graczy + 1), kazdy bierze jeden z puli
// i stawia go na siatce, potem wszyscy biegna.
// Postawione obiekty zostaja na kolejne rundy. Decyduje serwer - klienci dostaja liste obiektow i buduja je u siebie.
public class PlacementManager : NetworkBehaviour
{
    private const float PlacementDurationSeconds = 40f;  // wybor z puli + stawianie
    private const int ExtraPoolItems = 1;  // pula ma o jeden obiekt wiecej niz graczy - ostatni tez ma wybor
    private const float PlatformingDurationSeconds = 60f;
    private const float FallLimitY = -8f;  // ponizej dolnej krawedzi ekranu = gracz odpada z rundy
    private const int GridSortingOrder = -10;

    private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color BlockedColor = new Color(1f, 0.35f, 0.35f, 0.15f);

    [SerializeField] private PlaceableItem[] _items;

    private NetworkList<PlacedObjectState> _placedObjects = new NetworkList<PlacedObjectState>();
    private NetworkList<PoolSlot> _pool = new NetworkList<PoolSlot>();
    private NetworkVariable<double> _phaseEndTime = new NetworkVariable<double>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly List<GameObject> _placedViews = new List<GameObject>();
    private readonly List<RectInt> _blockedAreas = new List<RectInt>();
    private readonly List<RectInt> _markedAreas = new List<RectInt>();  // start i meta - pokazane na czerwono przy budowaniu
    private readonly List<Object> _generatedAssets = new List<Object>();
    private GameObject _gridOverlay;
    private bool _hasStarted;

    public static PlacementManager Instance { get; private set; }

    public IReadOnlyList<PlaceableItem> Items => _items;
    public int PoolCount => IsSpawned ? _pool.Count : 0;
    public static bool IsPlacing => GetState() == GameState.Placement;
    public static bool IsPlatforming => GetState() == GameState.Platforming;
    public float RemainingSeconds => IsSpawned ? Mathf.Max(0f, (float)(_phaseEndTime.Value - NetworkManager.ServerTime.Time)) : 0f;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        CollectBlockedAreas();
        _placedObjects.OnListChanged += HandlePlacedObjectsChanged;
        GameManager.Instance.OnGameStateChanged += HandleGameStateChanged;
        CreateGridOverlay();
        SyncPlacedViews();
        HandleGameStateChanged(GameManager.Instance.GetCurrentState());
        gameObject.AddComponent<PlacementHud>();
    }

    public override void OnNetworkDespawn()
    {
        _placedObjects.OnListChanged -= HandlePlacedObjectsChanged;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStateChanged -= HandleGameStateChanged;
        }
    }

    public override void OnDestroy()
    {
        foreach (GameObject view in _placedViews)
        {
            Destroy(view);
        }

        foreach (Object asset in _generatedAssets)
        {
            Destroy(asset);
        }

        if (_gridOverlay != null)
        {
            Destroy(_gridOverlay);
        }

        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        // Pierwsza runda dopiero po spawnie wszystkich NetworkBehaviour (GameManager musi juz dzialac).
        if (!_hasStarted)
        {
            _hasStarted = true;
            StartPlacement();
            return;
        }

        bool isTimeUp = RemainingSeconds <= 0f;

        if (IsPlacing && (isTimeUp || HaveAllBuildersPlaced()))
        {
            StartPlatforming();
        }
        else if (IsPlatforming)
        {
            MarkFinishedAndFallenPlayers();

            if (isTimeUp || HaveAllPlayersStopped())
            {
                StartPlacement();
            }
        }
    }

    public int GetPlayerIndex(PlayerBuilder builder)
    {
        int index = 0;

        foreach (PlayerBuilder other in PlayerBuilder.SpawnedBuilders)
        {
            if (other.NetworkObjectId < builder.NetworkObjectId)
            {
                index++;
            }
        }

        return index;
    }

    public bool IsValidItem(int itemIndex)
    {
        return _items != null && itemIndex >= 0 && itemIndex < _items.Length;
    }

    // Obiekt musi w calosci lezec na siatce, nie zaslaniac startu ani mety i nie nachodzic na inne obiekty.
    public bool CanPlace(int itemIndex, Vector2Int cell, int rotation)
    {
        if (!IsValidItem(itemIndex))
        {
            return false;
        }

        Vector2Int footprint = _items[itemIndex].GetFootprint(rotation);
        var area = new RectInt(cell, footprint);

        if (area.xMin < BuildGrid.Area.xMin || area.yMin < BuildGrid.Area.yMin ||
            area.xMax > BuildGrid.Area.xMax || area.yMax > BuildGrid.Area.yMax)
        {
            return false;
        }

        foreach (RectInt blocked in _blockedAreas)
        {
            if (area.Overlaps(blocked))
            {
                return false;
            }
        }

        foreach (PlacedObjectState placed in _placedObjects)
        {
            if (area.Overlaps(new RectInt(placed.Cell, _items[placed.ItemIndex].GetFootprint(placed.Rotation))))
            {
                return false;
            }
        }

        return true;
    }

    // Wolane tylko na serwerze przez PlayerBuilder.
    public bool TryPlace(int itemIndex, Vector2Int cell, int rotation)
    {
        if (!IsServer || !IsPlacing || !CanPlace(itemIndex, cell, rotation))
        {
            return false;
        }

        _placedObjects.Add(new PlacedObjectState { ItemIndex = itemIndex, CellX = cell.x, CellY = cell.y, Rotation = rotation });
        return true;
    }

    public PoolSlot GetPoolSlot(int slotIndex)
    {
        return _pool[slotIndex];
    }

    public bool IsSlotFree(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < PoolCount && !_pool[slotIndex].IsTaken;
    }

    // Wolane tylko na serwerze przez PlayerBuilder - kto pierwszy, ten lepszy.
    public bool TryTakeFromPool(PlayerBuilder builder, int slotIndex, out int itemIndex)
    {
        itemIndex = -1;

        if (!IsServer || !IsPlacing || !IsSlotFree(slotIndex))
        {
            return false;
        }

        PoolSlot slot = _pool[slotIndex];
        slot.IsTaken = true;
        slot.TakenBy = builder.NetworkObjectId;
        _pool[slotIndex] = slot;
        itemIndex = slot.ItemIndex;
        return true;
    }

    private void StartPlacement()
    {
        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            builder.ResetForPlacement();
        }

        // Losowanie z powtorzeniami - dwa noze w jednej puli sa w porzadku.
        _pool.Clear();

        for (int i = 0; i < PlayerBuilder.SpawnedBuilders.Count + ExtraPoolItems; i++)
        {
            _pool.Add(new PoolSlot { ItemIndex = Random.Range(0, _items.Length) });
        }

        _phaseEndTime.Value = NetworkManager.ServerTime.Time + PlacementDurationSeconds;
        GameManager.Instance.ChangeState(GameState.Placement);
    }

    private void StartPlatforming()
    {
        _phaseEndTime.Value = NetworkManager.ServerTime.Time + PlatformingDurationSeconds;
        GameManager.Instance.ChangeState(GameState.Platforming);
    }

    private static bool HaveAllBuildersPlaced()
    {
        if (PlayerBuilder.SpawnedBuilders.Count == 0)
        {
            return false;
        }

        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            if (!builder.HasPlaced)
            {
                return false;
            }
        }

        return true;
    }

    public Vector3 GetStartPosition(PlayerBuilder builder)
    {
        return LevelStart.Instance != null ? LevelStart.Instance.GetSpawnPosition(GetPlayerIndex(builder)) : Vector3.zero;
    }

    // Serwer widzi pozycje wszystkich graczy (NetworkTransform), wiec sam wie, kto dotarl do mety, a kto spadl.
    private static void MarkFinishedAndFallenPlayers()
    {
        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            if (builder.IsOut || builder.HasFinished)
            {
                continue;
            }

            if (LevelFinish.Instance != null && LevelFinish.Instance.IsReachedBy(GetPlayerBounds(builder)))
            {
                builder.MarkFinished();
            }
            else if (builder.transform.position.y < FallLimitY)
            {
                builder.MarkOut();
            }
        }
    }

    private static Bounds GetPlayerBounds(PlayerBuilder builder)
    {
        return builder.TryGetComponent(out Collider2D playerCollider)
            ? playerCollider.bounds
            : new Bounds(builder.transform.position, Vector3.one * 0.5f);
    }

    // Runda biegu konczy sie, gdy kazdy jest w ustach albo za plansza.
    private static bool HaveAllPlayersStopped()
    {
        if (PlayerBuilder.SpawnedBuilders.Count == 0)
        {
            return false;
        }

        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            if (!builder.IsOut && !builder.HasFinished)
            {
                return false;
            }
        }

        return true;
    }

    // Kratki zajete przez mape: start i meta (z miejscem nad nimi) oraz skrzynie, ktore i tak widac.
    private void CollectBlockedAreas()
    {
        _markedAreas.Clear();

        if (LevelStart.Instance != null)
        {
            _markedAreas.Add(LevelStart.Instance.BlockedCells);
        }

        if (LevelFinish.Instance != null)
        {
            _markedAreas.Add(LevelFinish.Instance.BlockedCells);
        }

        _blockedAreas.Clear();
        _blockedAreas.AddRange(_markedAreas);

        foreach (MapBlock block in MapBlock.Blocks)
        {
            _blockedAreas.Add(block.BlockedCells);
        }
    }

    private static GameState GetState()
    {
        return GameManager.Instance != null ? GameManager.Instance.GetCurrentState() : GameState.Lobby;
    }

    private void HandleGameStateChanged(GameState newState)
    {
        if (_gridOverlay != null)
        {
            _gridOverlay.SetActive(newState == GameState.Placement);
        }
    }

    private void HandlePlacedObjectsChanged(NetworkListEvent<PlacedObjectState> changeEvent)
    {
        SyncPlacedViews();
    }

    // Lista tylko rosnie w trakcie gry - dobudowujemy brakujace, a przy innej zmianie budujemy od nowa.
    private void SyncPlacedViews()
    {
        if (_placedViews.Count > _placedObjects.Count)
        {
            foreach (GameObject view in _placedViews)
            {
                Destroy(view);
            }

            _placedViews.Clear();
        }

        for (int i = _placedViews.Count; i < _placedObjects.Count; i++)
        {
            PlacedObjectState placed = _placedObjects[i];
            GameObject view = IsValidItem(placed.ItemIndex)
                ? _items[placed.ItemIndex].CreateView(placed.Cell, placed.Rotation, true)
                : new GameObject("UnknownItem");
            _placedViews.Add(view);
        }
    }

    // Siatka 32 px i zablokowane kratki przy starcie i mecie - widoczne tylko podczas budowania.
    private void CreateGridOverlay()
    {
        _gridOverlay = new GameObject("BuildGridOverlay");
        _gridOverlay.SetActive(false);

        var gridTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };

        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                gridTexture.SetPixel(x, y, x == 0 || y == 0 ? Color.white : Color.clear);
            }
        }

        gridTexture.Apply();
        Sprite gridSprite = Sprite.Create(gridTexture, new Rect(0, 0, 32, 32), Vector2.zero, 32f, 0, SpriteMeshType.FullRect);
        _generatedAssets.Add(gridTexture);
        _generatedAssets.Add(gridSprite);

        SpriteRenderer grid = CreateOverlayRenderer("Grid", gridSprite, GridColor, BuildGrid.Area);
        grid.drawMode = SpriteDrawMode.Tiled;
        grid.size = BuildGrid.Area.size;

        var zoneTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        zoneTexture.SetPixel(0, 0, Color.white);
        zoneTexture.Apply();
        Sprite zoneSprite = Sprite.Create(zoneTexture, new Rect(0, 0, 1, 1), Vector2.zero, 1f);
        _generatedAssets.Add(zoneTexture);
        _generatedAssets.Add(zoneSprite);

        foreach (RectInt blocked in _markedAreas)
        {
            SpriteRenderer zone = CreateOverlayRenderer("BlockedArea", zoneSprite, BlockedColor, blocked);
            zone.transform.localScale = new Vector3(blocked.width, blocked.height, 1f);
        }
    }

    private SpriteRenderer CreateOverlayRenderer(string name, Sprite sprite, Color color, RectInt area)
    {
        var overlayObject = new GameObject(name);
        overlayObject.transform.SetParent(_gridOverlay.transform, false);
        overlayObject.transform.position = new Vector3(area.xMin, area.yMin, 0f);

        SpriteRenderer overlayRenderer = overlayObject.AddComponent<SpriteRenderer>();
        overlayRenderer.sprite = sprite;
        overlayRenderer.color = color;
        overlayRenderer.sortingOrder = GridSortingOrder;
        return overlayRenderer;
    }
}
#endregion
