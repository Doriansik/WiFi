using System.Collections;
using UnityEngine;

#region Item Bouncer
// Strefa na wierzchu odbijajacego obiektu (np. chleba). Postac, ktora na nia spadnie albo na niej stoi, wylatuje
// w strone "gory" obiektu - obrocony chleb odbija w bok. Odbicie nadaje wlasciciel postaci (tam liczy sie jej fizyka),
// animacje widza wszyscy (PlayerController wysyla ja do pozostalych graczy).
public class ItemBouncer : MonoBehaviour
{
    private const float PixelsPerUnit = 32f;
    private const float ZoneAbovePixels = 4f;   // strefa siega troche nad wierzch - stojaca postac tez jest w srodku
    private const float ZoneBelowPixels = 2f;
    private const float ZoneSideInsetPixels = 1f;  // bez samych krawedzi - otarcie sie bokiem nie odbija

    private PlaceableItem _item;
    private SpriteRenderer _renderer;
    private Transform _art;
    private Coroutine _animation;

    public float Height => _item.BounceHeight;  // w kratkach
    public Vector2 Direction => transform.up;
    public int PlacedIndex { get; set; } = -1;  // ktory to postawiony obiekt - po tym inni gracze znajduja go dla animacji

    public static void Attach(GameObject art, PlaceableItem item, SpriteRenderer spriteRenderer)
    {
        var zone = new GameObject("BounceZone");
        zone.transform.SetParent(art.transform, false);

        // Wierzch grafiki (pierwszej klatki) we wspolrzednych "Art" - nad nim cienki pas wyzwalacza.
        Rect bounds = PlaceableItem.GetShapeBounds(item.Sprite);  // chleb zajmuje tylko dol klatki 32x32
        float above = ZoneAbovePixels / PixelsPerUnit;
        float below = ZoneBelowPixels / PixelsPerUnit;
        BoxCollider2D trigger = zone.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(Mathf.Max(bounds.width - 2f * ZoneSideInsetPixels / PixelsPerUnit, 1f / PixelsPerUnit), above + below);
        trigger.offset = new Vector2(bounds.center.x, bounds.yMax + (above - below) * 0.5f);

        ItemBouncer bouncer = zone.AddComponent<ItemBouncer>();
        bouncer._item = item;
        bouncer._renderer = spriteRenderer;
        bouncer._art = art.transform;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryBounce(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryBounce(other);
    }

    private void TryBounce(Collider2D other)
    {
        if (other.TryGetComponent(out PlayerController player))
        {
            player.TryBounce(this);
        }
    }

    public void PlayAnimation()
    {
        if (_item.BounceFrames == null || _item.BounceFrames.Length == 0)
        {
            return;
        }

        if (_animation != null)
        {
            StopCoroutine(_animation);
        }

        _animation = StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        var wait = new WaitForSeconds(1f / Mathf.Max(1f, _item.BounceFramesPerSecond));

        foreach (Sprite frame in _item.BounceFrames)
        {
            if (frame != null)
            {
                ShowFrame(frame);
                yield return wait;
            }
        }

        ShowFrame(_item.Sprite);
        _animation = null;
    }

    // Klatki maja rozne rozmiary - kazda ustawiamy osobno, zeby obiekt stal w miejscu, a nie skakal o piksel.
    private void ShowFrame(Sprite frame)
    {
        _renderer.sprite = frame;
        _art.localPosition = _item.GetArtOffset(frame);
    }
}
#endregion
