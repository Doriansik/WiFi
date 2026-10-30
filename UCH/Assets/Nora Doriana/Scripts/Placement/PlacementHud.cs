using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#region Placement Hud
// Pasek u gory ekranu: faza rundy, czas, pula obiektow do wziecia i co ma zrobic lokalny gracz.
// Tworzony z kodu przez PlacementManager.
public class PlacementHud : MonoBehaviour
{
    private const string KeyboardPickHint = "Wez obiekt z puli: klik albo strzalki + Spacja (klawisze 1-9)";
    private const string TouchPickHint = "Dotknij obiekt w puli albo < > i skok";
    private const string PhonePickHint = "Gracze z telefonow wybieraja na swoich ekranach";
    private const string KeyboardHint = "Strzalki/mysz - kursor   R, PPM - obrot   Spacja, klik - postaw";
    private const string TouchHint = "Dotknij - kursor, dotknij jeszcze raz - postaw   skok - obrot";
    private const string PlacedHint = "Postawione! Czekamy na reszte...";
    private const string FallenHint = "Poza plansza - poczekaj na nastepna runde";
    private const string FinishedHint = "Meta! Czekamy na reszte...";
    private const int MaxNameLength = 10;

    private const float TileWidth = 132f;
    private const float TileHeight = 150f;
    private const float TileSpacing = 12f;

    private static readonly Color FreeTileColor = new Color(0f, 0f, 0f, 0.65f);
    private static readonly Color HighlightTileColor = new Color(1f, 1f, 1f, 0.4f);
    private static readonly Color MyTileColor = new Color(0.13f, 0.77f, 0.37f, 0.6f);
    private static readonly Color TakenTileColor = new Color(0f, 0f, 0f, 0.3f);
    private static readonly Color TakenIconColor = new Color(1f, 1f, 1f, 0.35f);

    private sealed class PoolTile
    {
        public Button Button;
        public Image Background;
        public Image Icon;
        public TMP_Text Key;
        public TMP_Text Label;
    }

    private readonly List<PoolTile> _tiles = new List<PoolTile>();
    private TMP_Text _titleText;
    private TMP_Text _hintText;
    private RectTransform _poolRow;

    private void Awake()
    {
        var canvasObject = new GameObject("PlacementHud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;  // pod menu pauzy, nad gra

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _titleText = CreateText(canvasObject.transform, 52f, TextAlignmentOptions.Top);
        SetTopStretch(_titleText.rectTransform, -24f, 52f * 2.6f);
        _hintText = CreateText(canvasObject.transform, 30f, TextAlignmentOptions.Top);
        SetTopStretch(_hintText.rectTransform, -92f, 30f * 2.6f);

        _poolRow = new GameObject("PoolRow", typeof(RectTransform)).GetComponent<RectTransform>();
        _poolRow.SetParent(canvasObject.transform, false);
        _poolRow.anchorMin = _poolRow.anchorMax = new Vector2(0.5f, 1f);
        _poolRow.pivot = new Vector2(0.5f, 1f);
        _poolRow.anchoredPosition = new Vector2(0f, -190f);
        _poolRow.sizeDelta = new Vector2(0f, TileHeight);
    }

    private void Update()
    {
        PlacementManager manager = PlacementManager.Instance;

        if (manager == null || !manager.IsSpawned)
        {
            _titleText.text = string.Empty;
            _hintText.text = string.Empty;
            _poolRow.gameObject.SetActive(false);
            return;
        }

        int seconds = Mathf.CeilToInt(manager.RemainingSeconds);
        PlayerBuilder localBuilder = FindLocalBuilder();

        if (PlacementManager.IsPlacing)
        {
            int placedCount = 0;

            foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
            {
                placedCount += builder.HasPlaced ? 1 : 0;
            }

            _titleText.text = $"BUDOWANIE  {seconds}s   ({placedCount}/{PlayerBuilder.SpawnedBuilders.Count})";
            _hintText.text = GetPlacementHint(localBuilder);
        }
        else if (PlacementManager.IsPlatforming)
        {
            int finishedCount = 0;

            foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
            {
                finishedCount += builder.HasFinished ? 1 : 0;
            }

            _titleText.text = $"BIEG  {seconds}s   (meta: {finishedCount}/{PlayerBuilder.SpawnedBuilders.Count})";
            _hintText.text = GetPlatformingHint(localBuilder);
        }
        else
        {
            _titleText.text = string.Empty;
            _hintText.text = string.Empty;
        }

        UpdatePool(manager, localBuilder);
    }

    #region Pool
    // Pula wisi na ekranie, dopoki ktos jeszcze nie wzial obiektu - wszyscy widza, kto co zabral.
    private void UpdatePool(PlacementManager manager, PlayerBuilder localBuilder)
    {
        bool isVisible = PlacementManager.IsPlacing && manager.PoolCount > 0 && IsAnyonePicking();
        _poolRow.gameObject.SetActive(isVisible);

        if (!isVisible)
        {
            return;
        }

        if (_tiles.Count != manager.PoolCount)
        {
            RebuildTiles(manager.PoolCount);
        }

        bool isLocalPicking = localBuilder != null && !localBuilder.HasPickedItem;

        for (int i = 0; i < _tiles.Count; i++)
        {
            PoolSlot slot = manager.GetPoolSlot(i);
            PoolTile tile = _tiles[i];
            PlaceableItem item = manager.IsValidItem(slot.ItemIndex) ? manager.Items[slot.ItemIndex] : null;
            bool isMine = slot.IsTaken && localBuilder != null && slot.TakenBy == localBuilder.NetworkObjectId;

            if (item != null && tile.Icon.sprite != item.Sprite)
            {
                tile.Icon.sprite = item.Sprite;
            }

            tile.Icon.color = slot.IsTaken && !isMine ? TakenIconColor : Color.white;
            tile.Background.color = isMine ? MyTileColor
                : slot.IsTaken ? TakenTileColor
                : isLocalPicking && i == localBuilder.PickHighlight ? HighlightTileColor
                : FreeTileColor;
            tile.Key.text = !slot.IsTaken && i < 9 ? (i + 1).ToString() : string.Empty;
            tile.Label.text = slot.IsTaken ? GetTakerName(slot.TakenBy) : item != null ? item.DisplayName : string.Empty;
            tile.Button.interactable = isLocalPicking && !slot.IsTaken;
        }
    }

    private static bool IsAnyonePicking()
    {
        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            if (!builder.HasPickedItem && !builder.HasPlaced)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetTakerName(ulong builderObjectId)
    {
        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            if (builder.NetworkObjectId == builderObjectId && builder.TryGetComponent(out LobbyPlayer lobbyPlayer))
            {
                string name = lobbyPlayer.GetDeviceName();
                return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
            }
        }

        return "?";
    }

    private void RebuildTiles(int count)
    {
        foreach (Transform child in _poolRow)
        {
            Destroy(child.gameObject);
        }

        _tiles.Clear();
        float startX = -(count - 1) * (TileWidth + TileSpacing) * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var tileObject = new GameObject($"Slot{i + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
            var tileRect = (RectTransform)tileObject.transform;
            tileRect.SetParent(_poolRow, false);
            tileRect.anchorMin = tileRect.anchorMax = new Vector2(0.5f, 1f);
            tileRect.pivot = new Vector2(0.5f, 1f);
            tileRect.anchoredPosition = new Vector2(startX + i * (TileWidth + TileSpacing), 0f);
            tileRect.sizeDelta = new Vector2(TileWidth, TileHeight);

            var tile = new PoolTile
            {
                Background = tileObject.GetComponent<Image>(),
                Button = tileObject.GetComponent<Button>()
            };

            // Kolory ustawia UpdatePool - domyslne przyciemnianie przycisku mieszaloby sie z nimi.
            tile.Button.transition = Selectable.Transition.None;
            int slotIndex = i;
            tile.Button.onClick.AddListener(() => OnTileClicked(slotIndex));

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(tileRect, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -12f);
            iconRect.sizeDelta = new Vector2(TileWidth - 20f, 88f);
            tile.Icon = iconObject.GetComponent<Image>();
            tile.Icon.preserveAspect = true;
            tile.Icon.raycastTarget = false;

            tile.Key = CreateText(tileRect, 24f, TextAlignmentOptions.TopLeft);
            tile.Key.rectTransform.anchorMin = Vector2.zero;
            tile.Key.rectTransform.anchorMax = Vector2.one;
            tile.Key.rectTransform.offsetMin = new Vector2(8f, 0f);
            tile.Key.rectTransform.offsetMax = new Vector2(0f, -4f);

            tile.Label = CreateText(tileRect, 24f, TextAlignmentOptions.Bottom);
            tile.Label.rectTransform.anchorMin = Vector2.zero;
            tile.Label.rectTransform.anchorMax = Vector2.one;
            tile.Label.rectTransform.offsetMin = new Vector2(4f, 8f);
            tile.Label.rectTransform.offsetMax = new Vector2(-4f, 0f);

            _tiles.Add(tile);
        }
    }

    private static void OnTileClicked(int slotIndex)
    {
        PlayerBuilder localBuilder = FindLocalBuilder();

        if (localBuilder != null)
        {
            localBuilder.RequestPick(slotIndex);
        }
    }
    #endregion

    private static string GetPlatformingHint(PlayerBuilder localBuilder)
    {
        if (localBuilder == null)
        {
            return string.Empty;
        }

        if (localBuilder.HasFinished)
        {
            return FinishedHint;
        }

        return localBuilder.IsOut ? FallenHint : string.Empty;
    }

    private static string GetPlacementHint(PlayerBuilder localBuilder)
    {
        if (localBuilder == null)
        {
            return string.Empty;
        }

        if (localBuilder.HasPlaced)
        {
            return PlacedHint;
        }

        bool isHostDevice = MainMenuUI.IsHostDevice();  // tak samo jak przyciski mobilne

        if (!localBuilder.HasPickedItem)
        {
            string pickHint = isHostDevice ? KeyboardPickHint : TouchPickHint;
            return PadServer.Instance != null ? $"{pickHint}\n<size=70%>{PhonePickHint}</size>" : pickHint;
        }

        string controls = isHostDevice ? KeyboardHint : TouchHint;
        return $"Twoj obiekt: {localBuilder.CurrentItem.DisplayName}\n<size=70%>{controls}</size>";
    }

    // Postac tego komputera - pady telefonow tez naleza do hosta, ale maja wlasny ekran.
    private static PlayerBuilder FindLocalBuilder()
    {
        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            if (builder.IsOwner && builder.TryGetComponent(out LobbyPlayer lobbyPlayer) && !lobbyPlayer.IsPadPlayer)
            {
                return builder;
            }
        }

        return null;
    }

    private static TMP_Text CreateText(Transform parent, float fontSize, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.outlineWidth = 0.2f;
        text.outlineColor = Color.black;
        return text;
    }

    private static void SetTopStretch(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, top);
        rect.sizeDelta = new Vector2(-80f, height);
    }
}
#endregion
