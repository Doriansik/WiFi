using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

#region Build Grid
// Siatka budowania: 1 kratka = 1 jednostka = 32 px (PPU wszystkich sprite'ow). Obszar = to, co widzi kamera (640x360 px).
public static class BuildGrid
{
    public const int Rotations = 4;

    // Kratki od (-10,-5) do (9,4) - pelna szerokosc ekranu, bez polowek kratek przy gornej i dolnej krawedzi.
    public static readonly RectInt Area = new RectInt(-10, -5, 20, 10);

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

        // Sprite moze byc wycietym kawalkiem obrazka albo klatka arkusza - liczymy od poczatku jego "kafelka".
        Vector2 framePixels = (Vector2)Size * PixelsPerUnit;
        Vector2 frameOrigin = new Vector2(
            Mathf.Floor(Sprite.rect.x / framePixels.x) * framePixels.x,
            Mathf.Floor(Sprite.rect.y / framePixels.y) * framePixels.y);
        Vector2 pivotInFrame = Sprite.rect.position - frameOrigin + Sprite.pivot;
        art.transform.localPosition = pivotInFrame / PixelsPerUnit - (Vector2)Size * 0.5f;

        SpriteRenderer spriteRenderer = art.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = Sprite;

        if (Animation != null)
        {
            art.AddComponent<Animator>().runtimeAnimatorController = Animation;
        }

        if (withCollider)
        {
            AddCollider(art);
        }

        return root;
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
