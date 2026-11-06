using TMPro;
using UnityEngine;
using UnityEngine.UI;

#region Pad Join UI
// Kod QR z adresem pada - widoczny tylko na hoscie. Adres powstaje dopiero po utworzeniu lobby, wiec czekamy na niego.
public class PadJoinUI : MonoBehaviour
{
    private const int QuietZoneModules = 4;  // bialy margines wymagany przez skanery
    private const string PreparingText = "Przygotowanie...";
    private const string UnavailableText = "Pady wymagaja internetu";
    // Bez przekaznika TURN telefon z innej sieci zwykle sie nie polaczy - mowimy to wprost przy kodzie QR.
    private const string AnyNetworkText = "Dowolna siec: Wi-Fi lub dane komorkowe";
    private const string LocalOnlyText = "Najpewniej ta sama siec Wi-Fi (dane komorkowe: nie zawsze)";
    private const float HintMinFontSize = 14f;

    [SerializeField] private GameObject _panel;
    [SerializeField] private RawImage _qrImage;
    [SerializeField] private TMP_Text _urlText;
    [SerializeField] private TMP_Text _networkHintText;

    private Texture2D _qrTexture;

    private void Start()
    {
        _panel.SetActive(PadServer.Instance != null);
        _qrImage.enabled = false;
        _urlText.text = PreparingText;
    }

    private void Update()
    {
        PadServer padServer = PadServer.Instance;

        if (_qrTexture != null || padServer == null)
        {
            return;
        }

        if (padServer.IsUnavailable)
        {
            _urlText.text = UnavailableText;
            enabled = false;
            return;
        }

        if (string.IsNullOrEmpty(padServer.JoinUrl))
        {
            return;
        }

        _qrTexture = CreateQrTexture(padServer.JoinUrl);
        _qrImage.texture = _qrTexture;
        _qrImage.enabled = true;
        _urlText.text = padServer.JoinUrl;
        ShowNetworkHint(padServer.HasTurn ? AnyNetworkText : LocalOnlyText);
        enabled = false;
    }

    private void ShowNetworkHint(string text)
    {
        if (_networkHintText == null)
        {
            return;
        }

        // Dluzszy napis zmniejsza sie zamiast wychodzic poza pole pod kodem.
        _networkHintText.fontSizeMax = _networkHintText.fontSize;
        _networkHintText.fontSizeMin = Mathf.Min(HintMinFontSize, _networkHintText.fontSize);
        _networkHintText.enableAutoSizing = true;
        _networkHintText.text = text;
    }

    private void OnDestroy()
    {
        if (_qrTexture != null)
        {
            Destroy(_qrTexture);
        }
    }

    private static Texture2D CreateQrTexture(string text)
    {
        bool[,] modules = QrCodeEncoder.Encode(text);
        int moduleCount = modules.GetLength(0);
        int size = moduleCount + QuietZoneModules * 2;
        var pixels = new Color32[size * size];
        var white = new Color32(255, 255, 255, 255);
        var black = new Color32(0, 0, 0, 255);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int moduleX = x - QuietZoneModules;
                int moduleY = y - QuietZoneModules;
                bool isDark = moduleX >= 0 && moduleX < moduleCount && moduleY >= 0 && moduleY < moduleCount && modules[moduleY, moduleX];
                // Tekstura liczy wiersze od dolu, kod QR od gory.
                pixels[(size - 1 - y) * size + x] = isDark ? black : white;
            }
        }

        // Point - kazdy modul to ostry kwadrat po przeskalowaniu, inaczej skaner moze nie zlapac kodu.
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
#endregion
