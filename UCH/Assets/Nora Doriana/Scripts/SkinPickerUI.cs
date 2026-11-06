using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#region Skin Picker UI
// Wybor postaci w lobby dla gracza z aplikacji (host i dolaczajacy przez Join). Telefony wybieraja na stronie pada.
// Wiersze powstaja z listy postaci w prefabie gracza - nowa postac dodana w prefabie od razu pojawi sie tutaj.
public class SkinPickerUI : MonoBehaviour
{
    private const string TitleText = "Twoja postac";
    private const string TakenText = "zajeta";
    private const float Padding = 12f;
    private const float TitleHeight = 56f;
    // Panel rosnie z liczba postaci - 8 wierszy po 84 px miesci sie w 1080 px z zapasem.
    private const float RowHeight = 84f;
    private const float RowSpacing = 6f;
    private const float IconSize = 72f;
    private const float LabelMaxFontSize = 26f;
    private const float LabelMinFontSize = 16f;  // dluzsze imie ("Energetyk Niko") zmniejsza sie zamiast wychodzic poza wiersz

    private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color SelectedRowColor = new Color(1f, 1f, 1f, 0.32f);
    private static readonly Color TakenColor = new Color(1f, 1f, 1f, 0.35f);

    private sealed class SkinRow
    {
        public Button Button;
        public Image Background;
        public Image Icon;
        public TMP_Text Label;
    }

    private readonly List<SkinRow> _rows = new List<SkinRow>();
    private PlayerSkin _localSkin;

    private void Start()
    {
        LobbyPlayer.OnPlayerListChanged += Refresh;
        PlayerSkin.OnSkinsChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        LobbyPlayer.OnPlayerListChanged -= Refresh;
        PlayerSkin.OnSkinsChanged -= Refresh;
    }

    private void Refresh()
    {
        if (_localSkin == null)
        {
            _localSkin = FindLocalSkin();

            if (_localSkin == null)
            {
                return;  // postac jeszcze sie nie pojawila w sieci
            }

            if (_rows.Count == 0)
            {
                BuildRows(_localSkin.Skins);
            }
        }

        IReadOnlyList<SkinDefinition> skins = _localSkin.Skins;

        for (int i = 0; i < _rows.Count && i < skins.Count; i++)
        {
            SkinRow row = _rows[i];
            bool isSelected = i == _localSkin.SkinIndex;
            bool canSelect = isSelected || _localSkin.CanSelect(i);

            row.Background.color = isSelected ? SelectedRowColor : RowColor;
            row.Icon.color = canSelect ? Color.white : TakenColor;
            row.Label.color = canSelect ? Color.white : TakenColor;
            row.Label.text = canSelect ? skins[i].DisplayName : $"{skins[i].DisplayName}\n<size=70%>{TakenText}</size>";
            row.Button.interactable = canSelect && !isSelected;
        }
    }

    // Pady telefonow tez naleza do hosta - "moja" postac to ta bez pada.
    private static PlayerSkin FindLocalSkin()
    {
        foreach (LobbyPlayer player in LobbyPlayer.SpawnedPlayers)
        {
            if (player.IsOwner && !player.IsPadPlayer && player.TryGetComponent(out PlayerSkin playerSkin))
            {
                return playerSkin;
            }
        }

        return null;
    }

    private void OnSkinClicked(int index)
    {
        if (_localSkin != null)
        {
            _localSkin.RequestSkin(index);
        }
    }

    private void BuildRows(IReadOnlyList<SkinDefinition> skins)
    {
        var panel = (RectTransform)transform;

        TextMeshProUGUI title = CreateText("Title", panel, 34f, TextAlignmentOptions.Center);
        title.text = TitleText;
        SetTopRow(title.rectTransform, Padding, TitleHeight);

        for (int i = 0; i < skins.Count; i++)
        {
            var rowObject = new GameObject(skins[i].Id, typeof(RectTransform), typeof(Image), typeof(Button));
            var rowRect = (RectTransform)rowObject.transform;
            rowRect.SetParent(panel, false);
            SetTopRow(rowRect, Padding + TitleHeight + i * (RowHeight + RowSpacing), RowHeight);

            var row = new SkinRow
            {
                Background = rowObject.GetComponent<Image>(),
                Button = rowObject.GetComponent<Button>()
            };

            // Kolory ustawia Refresh - domyslne przyciemnianie przycisku zgasiloby zaznaczony wiersz.
            row.Button.transition = Selectable.Transition.None;
            int index = i;
            row.Button.onClick.AddListener(() => OnSkinClicked(index));

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(rowRect, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(Padding, 0f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            row.Icon = iconObject.GetComponent<Image>();
            row.Icon.sprite = skins[i].Preview;
            row.Icon.preserveAspect = true;
            row.Icon.raycastTarget = false;

            TextMeshProUGUI label = CreateText("Label", rowRect, LabelMaxFontSize, TextAlignmentOptions.MidlineLeft);
            label.enableAutoSizing = true;
            label.fontSizeMin = LabelMinFontSize;
            label.fontSizeMax = LabelMaxFontSize;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(Padding * 2f + IconSize, 0f);
            label.rectTransform.offsetMax = new Vector2(-Padding, 0f);
            row.Label = label;

            _rows.Add(row);
        }

        float height = Padding * 2f + TitleHeight + skins.Count * (RowHeight + RowSpacing) - RowSpacing;
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, height);
    }

    private static TextMeshProUGUI CreateText(string name, RectTransform parent, float fontSize, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void SetTopRow(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -top);
        rect.sizeDelta = new Vector2(-Padding * 2f, height);
    }
}
#endregion
