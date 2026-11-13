using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#region Build Grid
// Siatka budowania: 1 kratka = 1 jednostka = 32 px (PPU wszystkich sprite'ow). Obszar = to, co widzi kamera: Pixel Perfect
// Camera w Gamephase renderuje zawsze 1280x720 px (Stretch Fill) i rozciaga na ekran, wiec plansza wypelnia kazde okno.
// Wiekszy obszar = zmienic rozdzielczosc kamery i ten prostokat razem (oraz polozenie mapy przy krawedziach).
public static class BuildGrid
{
    public const int Rotations = 4;

    // Kratki od (-20,-11) do (19,10) - pelna szerokosc ekranu (40), 22 z 22,5 rzedow - bez polowek kratek przy krawedziach.
    public static readonly RectInt Area = new RectInt(-20, -11, 40, 22);

    public static Vector3 GetCenter(Vector2Int cell, Vector2Int footprint)
    {
        return new Vector3(cell.x + footprint.x * 0.5f, cell.y + footprint.y * 0.5f, 0f);
    }

    // Kursor myszy wskazuje srodek obiektu, a nie jego lewy dolny rog.
    public static Vector2Int WorldToCell(Vector2 world, Vector2Int footprint)
    {
        return new Vector2Int(
            Mathf.FloorToInt(world.x - footprint.x * 0.5f + 0.5f),
            Mathf.FloorToInt(world.y - footprint.y * 0.5f + 0.5f));
    }

    public static Vector2Int Clamp(Vector2Int cell, Vector2Int footprint)
    {
        return new Vector2Int(
            Mathf.Clamp(cell.x, Area.xMin, Area.xMax - footprint.x),
            Mathf.Clamp(cell.y, Area.yMin, Area.yMax - footprint.y));
    }

    // Kratki zajete przez obiekt sceny (start, meta) - zaokraglenie wybacza sprite'om przycietym o kilka pikseli.
    public static RectInt GetCells(Bounds bounds)
    {
        int xMin = Mathf.RoundToInt(bounds.min.x);
        int yMin = Mathf.RoundToInt(bounds.min.y);
        int xMax = Mathf.Max(xMin + 1, Mathf.RoundToInt(bounds.max.x));
        int yMax = Mathf.Max(yMin + 1, Mathf.RoundToInt(bounds.max.y));
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
#endregion

#region Placeable Item
[Serializable]
public class PlaceableItem
{
    private const float PixelsPerUnit = 32f;

    public string Id;
    public string DisplayName;
    public Sprite Sprite;
    public Vector2Int Size = Vector2Int.one;  // w kratkach, przed obrotem
    public RuntimeAnimatorController Animation;  // opcjonalnie - np. katapulta

    // Odbijanie (np. chleb): postac, ktora spadnie na wierzch obiektu, wylatuje na tyle kratek w strone jego "gory"
    // (obrocony obiekt odbija w bok). 0 = zwykly obiekt. Klatki graja przy kazdym odbiciu, potem wraca Sprite.
    public float BounceHeight;
    public Sprite[] BounceFrames;
    public float BounceFramesPerSecond = 12f;

    // Sliski (np. lod): stojaca na nim postac rozpedza sie i hamuje powoli.
    public bool Slippery;

    // Klej (np. miod): nie zajmuje miejsca - kladzie sie na inne obiekty, postac przez niego przechodzi.
    // Skleja obiekty, na ktorych lezy (przyda sie, gdy obiekty beda sie ruszac).
    public bool Glue;

    private const int GlueSortingOrder = -5;  // zawsze najnizsza warstwa - pod wszystkimi obiektami (tylko siatka budowania nizej)

    // Strzelanie (np. katapulta): co ShootInterval sekund (0 = nie strzela) graja klatki ShootFrames, a na klatce
    // ShootReleaseFrame wylatuje pocisk. Kierunek i punkt wylotu (w pikselach od srodka klatki) sa wzgledem grafiki -
    // obrocony obiekt strzela w obrocona strone. Pocisk leci lukiem - ShootGravity sciaga go w dol (0 = prosto).
    // Bez ShootProjectile leci brazowa kulka.
    public float ShootInterval;
    public Sprite[] ShootFrames;
    public float ShootFramesPerSecond = 12f;
    public int ShootReleaseFrame;
    public Vector2 ShootDirection = Vector2.right;
    public Vector2 ShootOrigin;
    public float ShootSpeed = 6f;     // kratki na sekunde
    public float ShootGravity;        // kratki na sekunde^2
    public Sprite ShootProjectile;
    // Wyskakuje i wraca (tost z tostera): pocisk wylania sie zza obiektu, leci w strone ShootDirection, hamuje
    // (ShootGravity ciagnie go z powrotem do obiektu, takze w obroconym tosterze) i chowa sie z powrotem.
    // Przelatuje przez sciany, zabija kazda postac, ktorej dotknie.
    public bool ShootPopsBack;

    public const string ProjectilePreviewName = "ProjectilePreview";
    private const int ProjectilePreviewSortingOrder = 1;  // przed tosterem - przy budowaniu widac tost w jego miejscu

    // Zabojcze strefy (np. zeby widelca, ostrze noza): dotkniecie zabija postac. Prostokaty w pikselach kafelka
    // obiektu - jak na arkuszu PNG, od lewego dolnego rogu, przed obrotem. Moga wystawac poza grafike.
    public PixelArea[] HazardAreas;

    public Vector2Int GetFootprint(int rotation)
    {
        return rotation % 2 == 0 ? Size : new Vector2Int(Size.y, Size.x);
    }

    // Obiekt sklada sie z korzenia w srodku zajetych kratek (obrot) i grafiki przesunietej tak,
    // zeby obrazek lezal piksel w piksel na kratkach - tak jak na arkuszu PNG.
    public GameObject CreateView(Vector2Int cell, int rotation, bool withCollider)
    {
        var root = new GameObject(Id);
        root.transform.position = BuildGrid.GetCenter(cell, GetFootprint(rotation));
        root.transform.rotation = Quaternion.Euler(0f, 0f, 90f * rotation);

        var art = new GameObject("Art");
        art.transform.SetParent(root.transform, false);
        art.transform.localPosition = GetArtOffset(Sprite);

        SpriteRenderer spriteRenderer = art.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = Sprite;

        if (Glue)
        {
            spriteRenderer.sortingOrder = GlueSortingOrder;
        }

        if (Animation != null)
        {
            art.AddComponent<Animator>().runtimeAnimatorController = Animation;
        }

        if (ShootPopsBack && ShootProjectile != null)
        {
            CreateProjectilePreview(art);
        }

        if (withCollider && !Glue)
        {
            AddCollider(art);

            if (Slippery)
            {
                art.AddComponent<SlipperySurface>();
            }

            if (ShootInterval > 0f)
            {
                CatapultShooter.Attach(art, this, spriteRenderer);
            }

            if (HazardAreas != null)
            {
                foreach (PixelArea area in HazardAreas)
                {
                    ItemHazard.Attach(root, this, new RectInt(area.Min, area.Size));
                }
            }

            if (BounceHeight > 0f)
            {
                ItemBouncer.Attach(art, this, spriteRenderer);
            }
        }

        return root;
    }

    // Gdzie postawic grafike, zeby klatka lezala piksel w piksel na kratkach - tak jak na arkuszu PNG.
    // Klatki moga byc wyciete z roznym rozmiarem (chleb przy zgnieceniu jest szerszy) - kafelek liczymy od srodka klatki.
    public Vector3 GetArtOffset(Sprite sprite)
    {
        Vector2 framePixels = (Vector2)Size * PixelsPerUnit;
        Vector2 frameOrigin = new Vector2(
            Mathf.Floor(sprite.rect.center.x / framePixels.x) * framePixels.x,
            Mathf.Floor(sprite.rect.center.y / framePixels.y) * framePixels.y);
        Vector2 pivotInFrame = sprite.rect.position - frameOrigin + sprite.pivot;
        return pivotInFrame / PixelsPerUnit - (Vector2)Size * 0.5f;
    }

    // Kierunek wylotu pocisku we wspolrzednych grafiki (przed obrotem obiektu).
    public Vector2 GetShootDirectionLocal()
    {
        return ShootDirection.sqrMagnitude > 0f ? ShootDirection.normalized : Vector2.right;
    }

    // Przy budowaniu tost stoi w miejscu tostera (w punkcie wylotu, skad wyskakuje) - widac go i mozna do niego
    // dokleic miod. W fazie biegu chowa go CatapultShooter (wtedy lata prawdziwy tost).
    private void CreateProjectilePreview(GameObject art)
    {
        var preview = new GameObject(ProjectilePreviewName);
        preview.transform.SetParent(art.transform, false);
        preview.transform.localPosition = ShootOrigin / PixelsPerUnit;

        SpriteRenderer previewRenderer = preview.AddComponent<SpriteRenderer>();
        previewRenderer.sprite = ShootProjectile;
        previewRenderer.sortingOrder = ProjectilePreviewSortingOrder;
    }

    // Prostokat ksztaltu fizycznego sprite'a (wspolrzedne grafiki "Art"), a gdy go brak - prostokat obrazka.
    // Klatka ma pelny kafelek, a obiekt zajmuje tylko czesc - tu jest jego prawdziwy wierzch.
    public static Rect GetShapeBounds(Sprite sprite)
    {
        int shapeCount = sprite.GetPhysicsShapeCount();

        if (shapeCount == 0)
        {
            Bounds spriteBounds = sprite.bounds;
            return new Rect(spriteBounds.min, spriteBounds.size);
        }

        var points = new List<Vector2>();
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < shapeCount; i++)
        {
            sprite.GetPhysicsShape(i, points);

            foreach (Vector2 point in points)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    // Ksztalt fizyczny z importu sprite'a (dokladnie po pikselach), a gdy go brak - prostokat obrazka.
    private void AddCollider(GameObject art)
    {
        int shapeCount = Sprite.GetPhysicsShapeCount();

        if (shapeCount == 0)
        {
            BoxCollider2D box = art.AddComponent<BoxCollider2D>();
            box.size = Sprite.bounds.size;
            box.offset = Sprite.bounds.center;
            return;
        }

        PolygonCollider2D polygon = art.AddComponent<PolygonCollider2D>();
        polygon.pathCount = shapeCount;
        var points = new List<Vector2>();

        for (int i = 0; i < shapeCount; i++)
        {
            Sprite.GetPhysicsShape(i, points);
            polygon.SetPath(i, points);
        }
    }
}
#endregion

#region Pixel Area
// Prostokat w pikselach (lewy dolny rog + rozmiar) - do ustawiania w Inspektorze.
[Serializable]
public struct PixelArea
{
    public Vector2Int Min;
    public Vector2Int Size;
}
#endregion

#region Placed Object State
// Jeden postawiony obiekt - tyle wystarczy, zeby kazdy komputer zbudowal go u siebie identycznie.
public struct PlacedObjectState : INetworkSerializeByMemcpy, IEquatable<PlacedObjectState>
{
    public int ItemIndex;
    public int CellX;
    public int CellY;
    public int Rotation;

    public Vector2Int Cell => new Vector2Int(CellX, CellY);

    public bool Equals(PlacedObjectState other)
    {
        return ItemIndex == other.ItemIndex && CellX == other.CellX && CellY == other.CellY && Rotation == other.Rotation;
    }

    public override bool Equals(object obj)
    {
        return obj is PlacedObjectState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ItemIndex, CellX, CellY, Rotation);
    }
}
#endregion

#region Pool Slot
// Jedno miejsce w losowej puli rundy - kto pierwszy je wezmie, ten stawia ten obiekt.
public struct PoolSlot : INetworkSerializeByMemcpy, IEquatable<PoolSlot>
{
    public int ItemIndex;
    public bool IsTaken;
    public ulong TakenBy;  // NetworkObjectId postaci, ktora wziela obiekt

    public bool Equals(PoolSlot other)
    {
        return ItemIndex == other.ItemIndex && IsTaken == other.IsTaken && TakenBy == other.TakenBy;
    }

    public override bool Equals(object obj)
    {
        return obj is PoolSlot other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ItemIndex, IsTaken, TakenBy);
    }
}
#endregion
