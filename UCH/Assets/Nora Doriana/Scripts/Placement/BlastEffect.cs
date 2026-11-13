using UnityEngine;

#region Blast Effect
// Wybuch bomby: bomba miga chwile w miejscu postawienia, a kolo zasiegu rozszerza sie i znika.
// Kolo jest tez podgladem zasiegu przy stawianiu (PlaceableItem.CreateView).
public class BlastEffect : MonoBehaviour
{
    private const int CircleTexturePixels = 64;
    private const float RingThicknessPixels = 3f;
    private const float DurationSeconds = 0.45f;
    private const float BombVisibleSeconds = 0.12f;
    private const float StartScale = 0.3f;
    private const int EffectSortingOrder = 20;

    private static readonly Color CircleFillColor = new Color(1f, 0.55f, 0.15f, 0.25f);
    private static readonly Color CircleRingColor = new Color(1f, 0.85f, 0.3f, 0.9f);

    private static Sprite _circleSprite;

    private SpriteRenderer _circle;
    private SpriteRenderer _bomb;
    private float _radius;
    private float _startTime;

    public static SpriteRenderer CreateCircle(Transform parent, float radius, int sortingOrder)
    {
        var circleObject = new GameObject("BlastRadius");
        circleObject.transform.SetParent(parent, false);
        circleObject.transform.localScale = Vector3.one * radius * 2f;

        SpriteRenderer circle = circleObject.AddComponent<SpriteRenderer>();
        circle.sprite = GetCircleSprite();
        circle.sortingOrder = sortingOrder;
        return circle;
    }

    public static void Play(Vector2 center, float radius, Sprite bombSprite)
    {
        var effectObject = new GameObject("BombBlast");
        effectObject.transform.position = center;

        BlastEffect effect = effectObject.AddComponent<BlastEffect>();
        effect._radius = radius;
        effect._startTime = Time.time;
        effect._circle = CreateCircle(effectObject.transform, radius, EffectSortingOrder);

        if (bombSprite != null)
        {
            var bombObject = new GameObject("Bomb");
            bombObject.transform.SetParent(effectObject.transform, false);
            effect._bomb = bombObject.AddComponent<SpriteRenderer>();
            effect._bomb.sprite = bombSprite;
            effect._bomb.sortingOrder = EffectSortingOrder + 1;
        }

        effect.Update();
    }

    private void Update()
    {
        float elapsed = Time.time - _startTime;
        float progress = Mathf.Clamp01(elapsed / DurationSeconds);

        _circle.transform.localScale = Vector3.one * (_radius * 2f * Mathf.Lerp(StartScale, 1f, Mathf.Sqrt(progress)));
        _circle.color = new Color(1f, 1f, 1f, 1f - progress);

        if (_bomb != null)
        {
            _bomb.enabled = elapsed < BombVisibleSeconds;
        }

        if (progress >= 1f)
        {
            Destroy(gameObject);
        }
    }

    // Kolo o srednicy 1 jednostki: polprzezroczyste wnetrze i wyrazny brzeg, ostre piksele jak reszta grafiki.
    private static Sprite GetCircleSprite()
    {
        if (_circleSprite != null)
        {
            return _circleSprite;
        }

        var texture = new Texture2D(CircleTexturePixels, CircleTexturePixels, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        float half = CircleTexturePixels * 0.5f;

        for (int y = 0; y < CircleTexturePixels; y++)
        {
            for (int x = 0; x < CircleTexturePixels; x++)
            {
                float distance = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
                Color color = distance > half ? Color.clear
                    : distance > half - RingThicknessPixels ? CircleRingColor
                    : CircleFillColor;
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        _circleSprite = Sprite.Create(texture, new Rect(0, 0, CircleTexturePixels, CircleTexturePixels),
            new Vector2(0.5f, 0.5f), CircleTexturePixels, 0, SpriteMeshType.FullRect);
        return _circleSprite;
    }
}
#endregion
