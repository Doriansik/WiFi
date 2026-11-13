using Unity.Netcode;
using UnityEngine;

#region Catapult Shooter
// Postawiona katapulta (albo toster): co ShootInterval sekund gra klatki rzutu i na klatce ShootReleaseFrame wypuszcza pocisk.
// Rytm liczymy z czasu serwera - u kazdego gracza katapulta rzuca w tej samej chwili, bez wysylania czegokolwiek.
// Strzela tylko w fazie biegu; przy budowaniu stoi w pierwszej klatce (toster pokazuje tost w najwyzszym punkcie).
public class CatapultShooter : MonoBehaviour
{
    private const float PixelsPerUnit = 32f;
    private const float PopMaskDepthCells = 3f;  // jak gleboko pod wylotem maska chowa wyskakujacy pocisk

    private static Sprite _maskSprite;

    private PlaceableItem _item;
    private SpriteRenderer _renderer;
    private Transform _art;
    private GameObject _preview;
    private long _lastFiredCycle = long.MinValue;
    private Vector2 _mouthLocal;  // wierzch obiektu (wylot tostera) we wspolrzednych grafiki
    private int _shownFrame = -1;
    private bool _isRunning;
    private double _runStartTime = double.MaxValue;

    public bool PopsBack => _item.ShootPopsBack;

    public static void Attach(GameObject art, PlaceableItem item, SpriteRenderer spriteRenderer)
    {
        CatapultShooter shooter = art.AddComponent<CatapultShooter>();
        shooter._item = item;
        shooter._renderer = spriteRenderer;
        shooter._art = art.transform;

        Transform preview = art.transform.Find(PlaceableItem.ProjectilePreviewName);
        shooter._preview = preview != null ? preview.gameObject : null;

        Rect shape = PlaceableItem.GetShapeBounds(item.Sprite);
        shooter._mouthLocal = new Vector2(shape.center.x, shape.yMax);

        if (item.ShootPopsBack)
        {
            CreatePopMask(art.transform, shape);
        }
    }

    // Maska od wierzchu obiektu w dol: tost schowany w tosterze (i ponizej) jest niewidoczny, widac tylko to, co wystaje.
    private static void CreatePopMask(Transform art, Rect shape)
    {
        if (_maskSprite == null)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            _maskSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        var mask = new GameObject("PopMask");
        mask.transform.SetParent(art, false);
        mask.transform.localPosition = new Vector3(shape.center.x, shape.yMax - PopMaskDepthCells * 0.5f, 0f);
        mask.transform.localScale = new Vector3(shape.width, PopMaskDepthCells, 1f);
        mask.AddComponent<SpriteMask>().sprite = _maskSprite;
    }

    private void Update()
    {
        NetworkManager network = NetworkManager.Singleton;
        bool isConnected = network != null && network.IsListening;
        bool isRunning = isConnected && GameManager.Instance != null && GameManager.Instance.GetCurrentState() == GameState.Platforming;

        double now = GetNow();
        double interval = GetInterval();
        long cycle = (long)System.Math.Floor(now / interval);

        if (isRunning != _isRunning)
        {
            _isRunning = isRunning;
            _runStartTime = isRunning ? now : double.MaxValue;

            if (_preview != null)
            {
                _preview.SetActive(!isRunning);
            }
        }

        if (!isRunning)
        {
            // Bieg zaczyna sie w polowie cyklu - rzut z cyklu, ktory juz minal, nie wylatuje z opoznieniem.
            _lastFiredCycle = cycle;
            ShowFrame(0);
            return;
        }

        float framesPerSecond = GetFramesPerSecond();
        double cycleTime = now - cycle * interval;
        int frame = (int)(cycleTime * framesPerSecond);
        int frameCount = _item.ShootFrames != null ? _item.ShootFrames.Length : 0;
        ShowFrame(frame < frameCount ? frame : 0);

        if (cycle != _lastFiredCycle && frame >= _item.ShootReleaseFrame)
        {
            _lastFiredCycle = cycle;
            double releaseTime = cycle * interval + _item.ShootReleaseFrame / framesPerSecond;
            Fire(releaseTime);
        }
    }

    // O ile wyskakujacy pocisk jest teraz wysuniety z miejsca, w ktorym stoi przy budowaniu (w tosterze) - w swiecie.
    // Tym samym wzorem co lecacy tost (CatapultBall), wiec przyklejony miod jest dokladnie na nim. Miedzy wyskokami 0.
    public Vector2 GetPopOffset()
    {
        if (!_isRunning || !_item.ShootPopsBack)
        {
            return Vector2.zero;
        }

        double interval = GetInterval();
        double releaseOffset = _item.ShootReleaseFrame / GetFramesPerSecond();
        double now = GetNow();
        double releaseTime = System.Math.Floor((now - releaseOffset) / interval) * interval + releaseOffset;
        float flightTime = (float)(now - releaseTime);
        float popEndTime = _item.ShootGravity > 0f ? 2f * _item.ShootSpeed / _item.ShootGravity : float.MaxValue;

        if (releaseTime < _runStartTime || flightTime >= popEndTime)
        {
            return Vector2.zero;
        }

        float distance = Mathf.Max(0f, _item.ShootSpeed * flightTime - 0.5f * _item.ShootGravity * flightTime * flightTime);
        Vector2 direction = _art.TransformDirection(_item.GetShootDirectionLocal());
        return direction * distance;
    }

    // Prostokat tostu z podgladu (w miejscu tostera) w swiecie - do sprawdzania, czy miod lezy na toscie.
    public bool TryGetPreviewRect(out Rect rect)
    {
        rect = default;

        if (_preview == null || !_preview.TryGetComponent(out SpriteRenderer previewRenderer) || previewRenderer.sprite == null)
        {
            return false;
        }

        // Liczone z grafiki, nie z renderera - w fazie biegu podglad jest wylaczony, a obiekty obracaja sie o 90 stopni.
        Bounds local = previewRenderer.sprite.bounds;
        Vector2 a = _preview.transform.TransformPoint(local.min);
        Vector2 b = _preview.transform.TransformPoint(local.max);
        rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return true;
    }

    private double GetNow()
    {
        NetworkManager network = NetworkManager.Singleton;
        return network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
    }

    private double GetInterval()
    {
        return Mathf.Max(0.1f, _item.ShootInterval);
    }

    private float GetFramesPerSecond()
    {
        return Mathf.Max(1f, _item.ShootFramesPerSecond);
    }

    private void Fire(double releaseTime)
    {
        // Kierunek i punkt wylotu sa w ukladzie grafiki - obrocona katapulta strzela w obrocona strone.
        Vector2 direction = _art.TransformDirection(_item.GetShootDirectionLocal());
        Vector2 origin = _art.TransformPoint(_item.ShootOrigin / PixelsPerUnit);
        Vector2 mouth = _art.TransformPoint(_mouthLocal);
        CatapultBall.Spawn(origin, direction * _item.ShootSpeed, _item.ShootGravity, releaseTime, _item.ShootProjectile,
            _item.ShootPopsBack, mouth, _art.rotation);
    }

    private void ShowFrame(int frame)
    {
        if (frame == _shownFrame || _item.ShootFrames == null || frame >= _item.ShootFrames.Length || _item.ShootFrames[frame] == null)
        {
            return;
        }

        _shownFrame = frame;
        Sprite sprite = _item.ShootFrames[frame];
        _renderer.sprite = sprite;
        _art.localPosition = _item.GetArtOffset(sprite);
    }
}
#endregion
