using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

#region Player Builder
// Budowanie jednego gracza: wybor obiektu z puli rundy, kursor na siatce, obrot i postawienie.
// Sterowanie: klawiatura/mysz/dotyk w aplikacji albo telefon-pad. Stawianie zatwierdza serwer.
public class PlayerBuilder : NetworkBehaviour
{
    private const float KeyRepeatDelaySeconds = 0.3f;
    private const float KeyRepeatIntervalSeconds = 0.08f;

    private static readonly List<PlayerBuilder> _spawnedBuilders = new List<PlayerBuilder>();
    private static readonly Color GhostColor = new Color(1f, 1f, 1f, 0.7f);
    private static readonly Color BlockedGhostColor = new Color(1f, 0.3f, 0.3f, 0.7f);

    private NetworkVariable<int> _itemIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _rotation = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2Int> _cursorCell = new NetworkVariable<Vector2Int>(Vector2Int.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _hasPlaced = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _isOut = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _hasFinished = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PadInput _padInput;
    private Rigidbody2D _rigidbody;
    private GameManager _subscribedGameManager;

    // Wlasciciel-klient przesuwa kursor od razu u siebie i tylko informuje serwer - bez czekania na odpowiedz.
    private Vector2Int _localCursor;
    private Vector2Int _heldDirection;
    private float _nextRepeatTime;
    private float _previousHorizontalInput;
    private bool _wasMobileJumpPressed;
    private Vector3 _lastMousePosition;
    private int _pickHighlight;

    private GameObject _ghost;
    private SpriteRenderer[] _ghostRenderers;
    private int _ghostItemIndex = -1;
    private int _ghostRotation = -1;

    public static IReadOnlyList<PlayerBuilder> SpawnedBuilders => _spawnedBuilders;

    public int ItemIndex => _itemIndex.Value;
    public bool HasPickedItem => _itemIndex.Value >= 0;
    public int PickHighlight => _pickHighlight;  // podswietlony kafelek puli przy wyborze klawiatura
    public bool HasPlaced => _hasPlaced.Value;
    public bool IsOut => _isOut.Value;
    public bool HasFinished => _hasFinished.Value;
    public PlaceableItem CurrentItem =>
        PlacementManager.Instance != null && PlacementManager.Instance.IsValidItem(ItemIndex) ? PlacementManager.Instance.Items[ItemIndex] : null;

    private Vector2Int DisplayedCursor => IsOwner && !IsServer ? _localCursor : _cursorCell.Value;

    public override void OnNetworkSpawn()
    {
        TryGetComponent(out _padInput);
        TryGetComponent(out _rigidbody);
        _localCursor = _cursorCell.Value;
        _itemIndex.OnValueChanged += HandleItemOrRotationChanged;
        _rotation.OnValueChanged += HandleItemOrRotationChanged;
        _hasFinished.OnValueChanged += HandleFinishedChanged;
        _spawnedBuilders.Add(this);
        SetVisible(!HasFinished);
    }

    public override void OnNetworkDespawn()
    {
        _itemIndex.OnValueChanged -= HandleItemOrRotationChanged;
        _rotation.OnValueChanged -= HandleItemOrRotationChanged;
        _hasFinished.OnValueChanged -= HandleFinishedChanged;
        _spawnedBuilders.Remove(this);
        DestroyGhost();
        SubscribeToGameManager(null);
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (_subscribedGameManager != GameManager.Instance)
        {
            SubscribeToGameManager(GameManager.Instance);
        }

        UpdateGhost();

        if (!IsOwner || !PlacementManager.IsPlacing || HasPlaced || PlacementManager.Instance == null)
        {
            // Klikniecia z telefonu sprzed rundy budowania nie moga postawic obiektu w nastepnej.
            while (_padInput != null && _padInput.TryDequeueCommand(out _))
            {
            }

            return;
        }

        if (_padInput != null)
        {
            ProcessPadCommands();
        }
        else
        {
            ProcessLocalInput();
        }
    }

    // Postac przechodzi z lobby do gry jako ten sam obiekt - GameManager sceny gry pojawia sie dopiero po jej spawnie.
    private void SubscribeToGameManager(GameManager gameManager)
    {
        if (_subscribedGameManager != null)
        {
            _subscribedGameManager.OnGameStateChanged -= HandleGameStateChanged;
        }

        _subscribedGameManager = gameManager;

        if (gameManager != null)
        {
            gameManager.OnGameStateChanged += HandleGameStateChanged;

            // Klient wszedl do sceny, gdy budowanie juz trwalo - zmiany stanu nie zobaczy, wiec startujemy od razu.
            if (gameManager.GetCurrentState() == GameState.Placement)
            {
                HandleGameStateChanged(GameState.Placement);
            }
        }
    }

    #region Server
    public void ResetForPlacement()
    {
        if (!IsServer)
        {
            return;
        }

        _hasPlaced.Value = false;
        _isOut.Value = false;
        _hasFinished.Value = false;
        _rotation.Value = 0;
        _itemIndex.Value = -1;  // obiekt kazdy bierze sam z puli
    }

    public void MarkOut()
    {
        if (IsServer)
        {
            _isOut.Value = true;
        }
    }

    public void MarkFinished()
    {
        if (IsServer)
        {
            _hasFinished.Value = true;
        }
    }

    private void ServerSetCursor(Vector2Int cell)
    {
        _cursorCell.Value = ClampCursor(cell, _itemIndex.Value, _rotation.Value);
    }

    private void ServerPick(int slotIndex)
    {
        if (!HasPickedItem && !_hasPlaced.Value && PlacementManager.Instance.TryTakeFromPool(this, slotIndex, out int itemIndex))
        {
            _itemIndex.Value = itemIndex;
            _cursorCell.Value = ClampCursor(_cursorCell.Value, itemIndex, _rotation.Value);
        }
    }

    private void ServerRotate()
    {
        _rotation.Value = (_rotation.Value + 1) % BuildGrid.Rotations;
        _cursorCell.Value = ClampCursor(_cursorCell.Value, _itemIndex.Value, _rotation.Value);
    }

    private void ServerPlace()
    {
        if (!_hasPlaced.Value && PlacementManager.Instance.TryPlace(_itemIndex.Value, _cursorCell.Value, _rotation.Value))
        {
            _hasPlaced.Value = true;
        }
    }

    // Klient wysyla tylko gotowa pozycje kursora i polecenia - serwer sprawdza wszystko jeszcze raz.
    [Rpc(SendTo.Server)]
    private void SetCursorRpc(Vector2Int cell, RpcParams rpcParams = default)
    {
        if (IsFromOwner(rpcParams) && PlacementManager.IsPlacing)
        {
            ServerSetCursor(cell);
        }
    }

    [Rpc(SendTo.Server)]
    private void PickRpc(int slotIndex, RpcParams rpcParams = default)
    {
        if (IsFromOwner(rpcParams) && PlacementManager.IsPlacing)
        {
            ServerPick(slotIndex);
        }
    }

    [Rpc(SendTo.Server)]
    private void RotateRpc(RpcParams rpcParams = default)
    {
        if (IsFromOwner(rpcParams) && PlacementManager.IsPlacing && HasPickedItem && !_hasPlaced.Value)
        {
            ServerRotate();
        }
    }

    [Rpc(SendTo.Server)]
    private void PlaceRpc(Vector2Int cell, RpcParams rpcParams = default)
    {
        if (IsFromOwner(rpcParams) && PlacementManager.IsPlacing && HasPickedItem)
        {
            ServerSetCursor(cell);
            ServerPlace();
        }
    }

    private bool IsFromOwner(RpcParams rpcParams)
    {
        return rpcParams.Receive.SenderClientId == OwnerClientId && PlacementManager.Instance != null;
    }
    #endregion

    #region Owner Input
    private void MoveCursor(Vector2Int delta)
    {
        SetCursor(DisplayedCursor + delta);
    }

    private void SetCursor(Vector2Int cell)
    {
        if (IsServer)
        {
            ServerSetCursor(cell);
            return;
        }

        Vector2Int clamped = ClampCursor(cell, _itemIndex.Value, _rotation.Value);

        if (clamped != _localCursor)
        {
            _localCursor = clamped;
            SetCursorRpc(clamped);
        }
    }

    // Wolane tez z kafelkow puli na ekranie (klik/dotyk) - tylko dla postaci tego komputera.
    public void RequestPick(int slotIndex)
    {
        if (!IsOwner || HasPickedItem || !PlacementManager.IsPlacing)
        {
            return;
        }

        if (IsServer)
        {
            ServerPick(slotIndex);
        }
        else
        {
            PickRpc(slotIndex);
        }
    }

    private void Rotate()
    {
        if (!HasPickedItem)
        {
            return;
        }

        if (IsServer)
        {
            ServerRotate();
        }
        else
        {
            RotateRpc();
        }
    }

    private void Place()
    {
        if (!HasPickedItem)
        {
            return;
        }

        if (IsServer)
        {
            ServerPlace();
        }
        else
        {
            PlaceRpc(_localCursor);
        }
    }

    private void ProcessPadCommands()
    {
        while (_padInput.TryDequeueCommand(out PadCommand command))
        {
            switch (command.Type)
            {
                case PadCommandType.MoveCursor:
                    MoveCursor(new Vector2Int(command.X, command.Y));
                    break;
                case PadCommandType.PickItem:
                    RequestPick(command.X);
                    break;
                case PadCommandType.Rotate:
                    Rotate();
                    break;
                case PadCommandType.Place:
                    Place();
                    return;
            }
        }
    }

    // Wybor z puli: klik/dotyk w kafelek (PlacementHud), strzalki lewo/prawo + Spacja/Enter, klawisze 1-9,
    //   przyciski mobilne lewo/prawo + skok.
    // Stawianie: strzalki/WASD i mysz - kursor, R/prawy przycisk - obrot, Spacja/Enter/klik w kursor - postaw,
    //   przycisk mobilny skok - obrot.
    private void ProcessLocalInput()
    {
        if (!HasPickedItem)
        {
            ProcessPickInput();
            return;
        }

        ProcessKeyboardCursor();

        if (Input.GetKeyDown(KeyCode.R) || Input.GetMouseButtonDown(1) || ConsumeMobileJumpPress())
        {
            Rotate();
        }

        if (ProcessPointer() || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            Place();
        }
    }

    private void ProcessPickInput()
    {
        int step = 0;

        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        {
            step = -1;
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        {
            step = 1;
        }

        MobileInputManager mobileInput = MobileInputManager.Instance;
        float horizontal = mobileInput != null ? mobileInput.GetHorizontalInput() : 0f;

        if (horizontal != 0f && _previousHorizontalInput == 0f)
        {
            step = horizontal > 0f ? 1 : -1;
        }

        _previousHorizontalInput = horizontal;

        if (step != 0)
        {
            MovePickHighlight(step);
        }

        for (int i = 0; i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                RequestPick(i);
                return;
            }
        }

        if (ConsumeMobileJumpPress() || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            RequestPick(_pickHighlight);
        }
    }

    // Podswietlenie przeskakuje zajete kafelki - ktos inny juz je wzial.
    private void MovePickHighlight(int step)
    {
        PlacementManager manager = PlacementManager.Instance;
        int count = manager.PoolCount;

        for (int tries = 0; tries < count; tries++)
        {
            _pickHighlight = ((_pickHighlight + step) % count + count) % count;

            if (manager.IsSlotFree(_pickHighlight))
            {
                return;
            }
        }
    }

    private void ProcessKeyboardCursor()
    {
        var direction = new Vector2Int(
            (IsKeyHeld(KeyCode.RightArrow, KeyCode.D) ? 1 : 0) - (IsKeyHeld(KeyCode.LeftArrow, KeyCode.A) ? 1 : 0),
            (IsKeyHeld(KeyCode.UpArrow, KeyCode.W) ? 1 : 0) - (IsKeyHeld(KeyCode.DownArrow, KeyCode.S) ? 1 : 0));

        if (direction != _heldDirection)
        {
            _heldDirection = direction;

            if (direction != Vector2Int.zero)
            {
                MoveCursor(direction);
                _nextRepeatTime = Time.unscaledTime + KeyRepeatDelaySeconds;
            }
        }
        else if (direction != Vector2Int.zero && Time.unscaledTime >= _nextRepeatTime)
        {
            MoveCursor(direction);
            _nextRepeatTime = Time.unscaledTime + KeyRepeatIntervalSeconds;
        }
    }

    private static bool IsKeyHeld(KeyCode first, KeyCode second)
    {
        return Input.GetKey(first) || Input.GetKey(second);
    }

    // Przycisk skoku jest trzymany, a nie klikany - liczy sie tylko moment nacisniecia.
    private bool ConsumeMobileJumpPress()
    {
        MobileInputManager mobileInput = MobileInputManager.Instance;
        bool isJumpPressed = mobileInput != null && mobileInput.GetJumpInput();
        bool wasPressedNow = isJumpPressed && !_wasMobileJumpPressed;
        _wasMobileJumpPressed = isJumpPressed;
        return wasPressedNow;
    }

    // Zwraca true, gdy gracz kliknal w miejsce, gdzie juz stoi kursor (dotyk: pierwszy tap przesuwa, drugi stawia).
    private bool ProcessPointer()
    {
        Camera camera = Camera.main;

        if (camera == null || !Input.mousePresent && Input.touchCount == 0)
        {
            return false;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return false;  // przyciski interfejsu (pauza, sterowanie mobilne) maja pierwszenstwo
        }

        Vector3 mousePosition = Input.mousePosition;
        Vector2Int pointedCell = BuildGrid.WorldToCell(camera.ScreenToWorldPoint(mousePosition), GetFootprint(_itemIndex.Value, _rotation.Value));
        pointedCell = ClampCursor(pointedCell, _itemIndex.Value, _rotation.Value);

        if (Input.GetMouseButtonDown(0))
        {
            _lastMousePosition = mousePosition;

            if (pointedCell == DisplayedCursor)
            {
                return true;
            }

            SetCursor(pointedCell);
            return false;
        }

        // Kursor podaza za myszka tylko, gdy sie rusza - inaczej klawiatura nie moglaby nim sterowac.
        if (mousePosition != _lastMousePosition && !Input.GetMouseButton(0))
        {
            _lastMousePosition = mousePosition;
            SetCursor(pointedCell);
        }

        return false;
    }
    #endregion

    #region Ghost
    // Podglad obiektu pod kursorem - widza go wszyscy, czerwony tam, gdzie nie da sie postawic.
    private void UpdateGhost()
    {
        PlaceableItem item = CurrentItem;

        if (!PlacementManager.IsPlacing || HasPlaced || item == null)
        {
            DestroyGhost();
            return;
        }

        if (_ghost == null || _ghostItemIndex != ItemIndex || _ghostRotation != _rotation.Value)
        {
            DestroyGhost();
            _ghostItemIndex = ItemIndex;
            _ghostRotation = _rotation.Value;
            _ghost = item.CreateView(DisplayedCursor, _rotation.Value, false);
            _ghostRenderers = _ghost.GetComponentsInChildren<SpriteRenderer>();
        }

        Vector2Int cell = DisplayedCursor;
        _ghost.transform.position = BuildGrid.GetCenter(cell, item.GetFootprint(_rotation.Value));
        Color color = PlacementManager.Instance.CanPlace(ItemIndex, cell, _rotation.Value) ? GhostColor : BlockedGhostColor;

        foreach (SpriteRenderer ghostRenderer in _ghostRenderers)
        {
            ghostRenderer.color = color;
            ghostRenderer.sortingOrder = 10;
        }
    }

    private void DestroyGhost()
    {
        if (_ghost != null)
        {
            Destroy(_ghost);
        }

        _ghost = null;
        _ghostItemIndex = -1;
        _ghostRotation = -1;
    }
    #endregion

    private void HandleItemOrRotationChanged(int previousValue, int newValue)
    {
        // Obrot albo inny obiekt zmienia rozmiar - lokalny kursor tez musi zmiescic sie na siatce.
        if (IsOwner && !IsServer)
        {
            _localCursor = ClampCursor(_localCursor, _itemIndex.Value, _rotation.Value);
        }
    }

    // Nowa runda budowania: wlasciciel wraca na start (NetworkTransform jest po stronie wlasciciela).
    private void HandleGameStateChanged(GameState newState)
    {
        if (!IsOwner || newState != GameState.Placement || PlacementManager.Instance == null)
        {
            return;
        }

        int playerIndex = PlacementManager.Instance.GetPlayerIndex(this);
        Vector3 startPosition = PlacementManager.Instance.GetStartPosition(this);
        transform.position = startPosition;

        if (_rigidbody != null)
        {
            _rigidbody.position = startPosition;
            _rigidbody.linearVelocity = Vector2.zero;
        }

        _lastMousePosition = Input.mousePosition;
        _pickHighlight = 0;

        // Kazdy zaczyna z kursorem w innym miejscu srodka planszy, zeby podglady sie nie nakladaly.
        SetCursor(new Vector2Int(-2 + playerIndex % 5, -1 - playerIndex / 5));
    }

    // Postac, ktora weszla do ust, znika do konca rundy (razem ze znacznikiem pada).
    private void HandleFinishedChanged(bool previousValue, bool newValue)
    {
        SetVisible(!newValue);
    }

    private void SetVisible(bool isVisible)
    {
        foreach (SpriteRenderer spriteRenderer in GetComponentsInChildren<SpriteRenderer>(true))
        {
            spriteRenderer.enabled = isVisible;
        }
    }

    private Vector2Int ClampCursor(Vector2Int cell, int itemIndex, int rotation)
    {
        return BuildGrid.Clamp(cell, GetFootprint(itemIndex, rotation));
    }

    private static Vector2Int GetFootprint(int itemIndex, int rotation)
    {
        PlacementManager manager = PlacementManager.Instance;
        return manager != null && manager.IsValidItem(itemIndex) ? manager.Items[itemIndex].GetFootprint(rotation) : Vector2Int.one;
    }
}
#endregion
