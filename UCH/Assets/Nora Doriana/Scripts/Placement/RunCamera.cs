using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

#region Run Camera
// Kamera w trakcie biegu: patrzy na srodek wszystkich biegnacych graczy i przybliza albo oddala sie tak, zeby nikt
// nie podchodzil do krawedzi ekranu. Przy budowaniu (i poza biegiem) pokazuje cala plansze piksel w piksel (Pixel Perfect Camera).
// Kazdy komputer liczy kamere sam z pozycji graczy (NetworkTransform) - nic nie idzie przez siec.
// Dodawana sama do glownej kamery sceny gry - nie trzeba jej ustawiac w scenie.
[RequireComponent(typeof(Camera))]
public class RunCamera : MonoBehaviour
{
    private const float MarginX = 4f;            // kratki od skrajnego gracza do bocznej krawedzi ekranu
    private const float MarginY = 3f;            // kratki od skrajnego gracza do gornej/dolnej krawedzi
    private const float MinSize = 5.625f;        // najblizej: tyle co dawny widok 640x360 (na 1080p 3x)
    private const float SmoothSeconds = 0.35f;
    private const float SettleDistance = 0.02f;  // tak blisko widoku planszy - wracamy do Pixel Perfect

    private Camera _camera;
    private PixelPerfectCamera _pixelPerfect;
    private Vector3 _boardPosition;
    private float _boardSize;
    private Vector3 _velocity;
    private float _sizeVelocity;
    private bool _isFollowing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneNames.Game)
        {
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.CompareTag("MainCamera") && root.TryGetComponent(out Camera _) && !root.TryGetComponent(out RunCamera _))
            {
                root.AddComponent<RunCamera>();
                return;
            }
        }
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _pixelPerfect = GetComponent<PixelPerfectCamera>();
        _boardPosition = transform.position;
        _boardSize = _camera.orthographicSize;
    }

    private void LateUpdate()
    {
        Bounds players = default;
        bool hasPlayers = PlacementManager.IsPlatforming && TryGetRunningPlayersBounds(out players);

        if (hasPlayers && !_isFollowing)
        {
            // Widok planszy zapamietany z Pixel Perfect - to jednoczesnie najdalsze oddalenie i granica kamery.
            // Wylaczona Pixel Perfect przywraca rozmiar ze sceny, wiec od razu wpisujemy z powrotem ten, ktory byl widac.
            _boardSize = _camera.orthographicSize;
            _isFollowing = true;
            SetPixelPerfect(false);
            _camera.orthographicSize = _boardSize;
        }

        if (!_isFollowing)
        {
            return;
        }

        if (hasPlayers)
        {
            float size = GetSizeToFit(players);
            MoveTowards(ClampToBoard(players.center, size), size);
            return;
        }

        // Koniec biegu - plynnie wracamy do calej planszy, a na miejscu oddajemy kamere Pixel Perfect.
        MoveTowards(_boardPosition, _boardSize);

        if (Vector3.Distance(transform.position, _boardPosition) < SettleDistance && Mathf.Abs(_camera.orthographicSize - _boardSize) < SettleDistance)
        {
            transform.position = _boardPosition;
            _camera.orthographicSize = _boardSize;
            _velocity = Vector3.zero;
            _sizeVelocity = 0f;
            _isFollowing = false;
            SetPixelPerfect(true);
        }
    }

    private void OnDisable()
    {
        SetPixelPerfect(true);
    }

    // Najmniejszy widok, w ktorym wszyscy gracze mieszcza sie z marginesem - ale nie blizej niz MinSize i nie dalej niz plansza.
    private float GetSizeToFit(Bounds players)
    {
        float halfHeight = players.extents.y + MarginY;
        float halfWidth = players.extents.x + MarginX;
        float size = Mathf.Max(halfHeight, halfWidth / _camera.aspect);
        return Mathf.Clamp(size, Mathf.Min(MinSize, _boardSize), _boardSize);
    }

    // Kamera nie pokazuje nic poza plansza - przy krawedzi zatrzymuje sie, a gracz idzie dalej w strone brzegu ekranu.
    private Vector3 ClampToBoard(Vector3 center, float size)
    {
        // Okno szersze niz plansza (np. 21:9) - przy pelnym oddaleniu kamera zostaje na srodku.
        float roomX = Mathf.Max(0f, BoardHalfWidth - size * _camera.aspect);
        float roomY = Mathf.Max(0f, _boardSize - size);
        return new Vector3(
            Mathf.Clamp(center.x, _boardPosition.x - roomX, _boardPosition.x + roomX),
            Mathf.Clamp(center.y, _boardPosition.y - roomY, _boardPosition.y + roomY),
            _boardPosition.z);
    }

    // Polowa szerokosci planszy - tyle, ile pokazuje Pixel Perfect (Stretch Fill trzyma proporcje rozdzielczosci kamery).
    private float BoardHalfWidth => _pixelPerfect != null && _pixelPerfect.refResolutionY > 0
        ? _boardSize * _pixelPerfect.refResolutionX / _pixelPerfect.refResolutionY
        : _boardSize * _camera.aspect;

    private void MoveTowards(Vector3 position, float size)
    {
        transform.position = Vector3.SmoothDamp(transform.position, position, ref _velocity, SmoothSeconds);
        _camera.orthographicSize = Mathf.SmoothDamp(_camera.orthographicSize, size, ref _sizeVelocity, SmoothSeconds);
    }

    // Biegnacy = jeszcze nie w ustach i nie za plansza.
    private static bool TryGetRunningPlayersBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasAny = false;

        foreach (PlayerBuilder builder in PlayerBuilder.SpawnedBuilders)
        {
            if (builder == null || builder.IsOut || builder.HasFinished)
            {
                continue;
            }

            Bounds playerBounds = builder.TryGetComponent(out Collider2D playerCollider)
                ? playerCollider.bounds
                : new Bounds(builder.transform.position, Vector3.one);

            if (hasAny)
            {
                bounds.Encapsulate(playerBounds);
            }
            else
            {
                bounds = playerBounds;
                hasAny = true;
            }
        }

        return hasAny;
    }

    // Zoom w biegu jest plynny, wiec nie moze byc piksel w piksel - Pixel Perfect wylaczamy na czas sledzenia graczy.
    private void SetPixelPerfect(bool isEnabled)
    {
        if (_pixelPerfect != null)
        {
            _pixelPerfect.enabled = isEnabled;
        }
    }
}
#endregion
