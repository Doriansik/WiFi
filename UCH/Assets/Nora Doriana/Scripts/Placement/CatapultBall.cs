using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#region Catapult Ball
// Kulka z katapulty: leci lukiem (rzut ukosny, grawitacja zawsze w dol swiata). Pozycje liczymy wzorem z czasu serwera,
// wiec u wszystkich jest w tym samym miejscu.
// Znika na pierwszej scianie albo postaci. Trafiona postac odpada z rundy - zglasza to jej wlasciciel (u niego liczy sie jej pozycja).
// Odmiana "wyskakuje i wraca" (tost z tostera): wylania sie zza obiektu, hamuje, chowa z powrotem; przelatuje przez sciany
// i zabija kazda postac, ktorej dotknie wystajaca czescia.
public class CatapultBall : MonoBehaviour
{
    private const float PixelsPerUnit = 32f;
    private const int SpritePixels = 8;
    private const int SortingOrder = 1;
    private const int PopSortingOrder = -1;      // za obiektem - tost wysuwa sie z wnetrza tostera
    private const float PopHitInsetPixels = 2f;  // zabija porzadne dotkniecie, nie otarcie o rog grafiki
    private const float MaxLifetimeSeconds = 15f;
    private const int AreaMargin = 2;  // kratki poza obszarem budowania - dalej kulka znika

    private static Sprite _defaultSprite;
    private static readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
    private static readonly List<Collider2D> _overlaps = new List<Collider2D>();

    private Vector2 _origin;
    private Vector2 _velocity;
    private Vector2 _acceleration;
    private double _releaseTime;
    private Vector2 _lastPosition;
    private float _radius;  // z rozmiaru grafiki - wieksza kulka trafia z dalszej odleglosci
    private SpriteRenderer _renderer;

    // Tylko dla "wyskakuje i wraca": kiedy pocisk jest z powrotem w obiekcie i gdzie jest wylot (wierzch tostera).
    private bool _popsBack;
    private float _popEndTime;
    private Vector2 _mouth;

    public static void Spawn(Vector2 origin, Vector2 velocity, float gravity, double releaseTime, Sprite sprite,
        bool popsBack, Vector2 mouth, Quaternion rotation)
    {
        var ball = new GameObject(popsBack ? "PoppingProjectile" : "CatapultBall");
        ball.transform.SetPositionAndRotation(origin, popsBack ? rotation : Quaternion.identity);

        SpriteRenderer spriteRenderer = ball.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite != null ? sprite : GetDefaultSprite();
        spriteRenderer.sortingOrder = popsBack ? PopSortingOrder : SortingOrder;
        // Czesc jeszcze schowana w obiekcie zaslania maska tostera (CatapultShooter) - widac tylko to, co wystaje.
        spriteRenderer.maskInteraction = popsBack ? SpriteMaskInteraction.VisibleOutsideMask : SpriteMaskInteraction.None;
        Vector3 extents = spriteRenderer.sprite.bounds.extents;

        CatapultBall component = ball.AddComponent<CatapultBall>();
        component._origin = origin;
        component._velocity = velocity;
        component._releaseTime = releaseTime;
        component._lastPosition = origin;
        component._radius = Mathf.Min(extents.x, extents.y);
        component._renderer = spriteRenderer;
        component._popsBack = popsBack;
        component._mouth = mouth;

        if (popsBack)
        {
            // Hamowanie wzdluz kierunku wylotu - obrocony toster wyrzuca tost w bok i tez go chowa.
            float speed = velocity.magnitude;
            component._acceleration = speed > 0f ? -velocity / speed * gravity : Vector2.zero;
            component._popEndTime = gravity > 0f ? 2f * speed / gravity : MaxLifetimeSeconds;
        }
        else
        {
            component._acceleration = Vector2.down * gravity;
        }
    }

    private void Update()
    {
        NetworkManager network = NetworkManager.Singleton;
        bool isConnected = network != null && network.IsListening;
        bool isRunning = isConnected && GameManager.Instance != null && GameManager.Instance.GetCurrentState() == GameState.Platforming;
        double now = isConnected ? network.ServerTime.Time : Time.timeAsDouble;
        float flightTime = Mathf.Max(0f, (float)(now - _releaseTime));

        if (!isRunning || flightTime > MaxLifetimeSeconds || _popsBack && flightTime >= _popEndTime)
        {
            Destroy(gameObject);
            return;
        }

        Vector2 position = _origin + _velocity * flightTime + _acceleration * (0.5f * flightTime * flightTime);

        if (_popsBack)
        {
            transform.position = SnapToPixels(position);
            KillPlayersInVisiblePart();
            return;
        }

        if (CheckHit(_lastPosition, position, _radius))
        {
            Destroy(gameObject);
            return;
        }

        transform.position = SnapToPixels(position);
        _lastPosition = position;

        RectInt area = BuildGrid.Area;

        if (position.x < area.xMin - AreaMargin || position.x > area.xMax + AreaMargin ||
            position.y < area.yMin - AreaMargin || position.y > area.yMax + AreaMargin)
        {
            Destroy(gameObject);
        }
    }

    // Pozycja co do piksela - pocisk nie migocze miedzy pikselami ekranu.
    private static Vector3 SnapToPixels(Vector2 position)
    {
        return new Vector3(
            Mathf.Round(position.x * PixelsPerUnit) / PixelsPerUnit,
            Mathf.Round(position.y * PixelsPerUnit) / PixelsPerUnit,
            0f);
    }

    // Zabija tylko czesc wystajaca za wylot - postac stojaca obok tostera nie ginie od tostu schowanego w srodku.
    // Obiekty stoja w 4 obrotach, wiec wystarcza prostokat wyrownany do osi.
    private void KillPlayersInVisiblePart()
    {
        Bounds bounds = _renderer.bounds;
        Vector2 direction = _velocity.sqrMagnitude > 0f ? _velocity.normalized : Vector2.up;
        bool isHorizontal = Mathf.Abs(direction.x) > Mathf.Abs(direction.y);
        Vector2 axis = isHorizontal ? new Vector2(Mathf.Sign(direction.x), 0f) : new Vector2(0f, Mathf.Sign(direction.y));

        float center = Vector2.Dot(bounds.center, axis);
        float halfLength = isHorizontal ? bounds.extents.x : bounds.extents.y;
        float halfWidth = isHorizontal ? bounds.extents.y : bounds.extents.x;
        float inset = PopHitInsetPixels / PixelsPerUnit;
        float visibleStart = Mathf.Max(center - halfLength, Vector2.Dot(_mouth, axis)) + inset;
        float visibleEnd = center + halfLength - inset;

        if (visibleEnd <= visibleStart)
        {
            return;
        }

        Vector2 across = (Vector2)bounds.center - axis * center;
        Vector2 boxCenter = across + axis * ((visibleStart + visibleEnd) * 0.5f);
        float length = visibleEnd - visibleStart;
        float width = Mathf.Max(0f, 2f * (halfWidth - inset));
        Vector2 boxSize = isHorizontal ? new Vector2(length, width) : new Vector2(width, length);

        Physics2D.OverlapBox(boxCenter, boxSize, 0f, new ContactFilter2D().NoFilter(), _overlaps);

        foreach (Collider2D overlap in _overlaps)
        {
            if (overlap != null && !overlap.isTrigger && overlap.TryGetComponent(out PlayerBuilder player))
            {
                player.ReportDeath();
            }
        }
    }

    // Caly odcinek od ostatniej klatki, nie tylko punkt - szybka kulka nie przeskoczy cienkiej sciany przy niskim FPS.
    private static bool CheckHit(Vector2 from, Vector2 to, float radius)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        Vector2 direction = distance > 0f ? delta / distance : Vector2.right;
        int count = Physics2D.CircleCastNonAlloc(from, radius, direction, _hits, distance);
        bool isHit = false;

        for (int i = 0; i < count; i++)
        {
            Collider2D hitCollider = _hits[i].collider;

            if (hitCollider == null || hitCollider.isTrigger)
            {
                continue;
            }

            PlayerBuilder player = hitCollider.GetComponentInParent<PlayerBuilder>();

            if (player != null)
            {
                player.ReportDeath();
            }

            isHit = true;
        }

        return isHit;
    }

    // Brazowa kulka 8x8 px, gdy przedmiot nie ma wlasnej grafiki pocisku.
    private static Sprite GetDefaultSprite()
    {
        if (_defaultSprite != null)
        {
            return _defaultSprite;
        }

        string[] rows =
        {
            "..####..",
            ".#oooo#.",
            "#oxxooo#",
            "#oxoooo#",
            "#oooooo#",
            "#oooooo#",
            ".#oooo#.",
            "..####..",
        };

        var outline = new Color32(74, 40, 18, 255);
        var fill = new Color32(139, 84, 40, 255);
        var shine = new Color32(186, 128, 72, 255);
        var clear = new Color32(0, 0, 0, 0);
        var texture = new Texture2D(SpritePixels, SpritePixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        for (int y = 0; y < SpritePixels; y++)
        {
            string row = rows[SpritePixels - 1 - y];  // tekstura liczy wiersze od dolu

            for (int x = 0; x < SpritePixels; x++)
            {
                char c = row[x];
                texture.SetPixel(x, y, c == '#' ? outline : c == 'o' ? fill : c == 'x' ? shine : clear);
            }
        }

        texture.Apply();
        _defaultSprite = Sprite.Create(texture, new Rect(0, 0, SpritePixels, SpritePixels), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        return _defaultSprite;
    }
}
#endregion
