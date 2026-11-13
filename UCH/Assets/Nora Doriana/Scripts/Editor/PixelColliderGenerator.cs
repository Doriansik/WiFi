using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

#region Pixel Collider Generator
// Ksztalt fizyczny (Custom Physics Shape) liczony z pikseli sprite'a - pod pixel art 32 px.
//   - wierzcholki tylko na rogach pikseli, prostokat = 4 wierzcholki,
//   - schodki 1 px (zaokraglenia, skosy) wygladzone w skos, wieksze stopnie zostaja ostre - gracz nie zahacza o drobiazgi,
//   - dziury w srodku wypelnione, pojedyncze piksele-paprochy pominiete.
// Gra czyta ten ksztalt sama (PlaceableItem.CreateView -> Sprite.GetPhysicsShape), wiec nic wiecej nie trzeba zmieniac.
// Obiekty do budowania: pliki "*_object.png" w Assets/Sprites. Po zmianie grafiki wystarczy uruchomic generator jeszcze raz.
public static class PixelColliderGenerator
{
    private const string MenuRoot = "Tools/Pixel Art/";
    private const string ItemsFolder = "Assets/Sprites";
    private const string ItemSuffix = "_object";
    private const byte AlphaThreshold = 26;      // ~10% - polprzezroczyste krawedzie tez sa "cialem" obiektu
    private const int MinIslandPixels = 3;       // mniejsze kawalki to paprochy, nie podloze
    private const float SmoothingPixels = 1f;    // schodki odchylone o max 1 px od skosu staja sie skosem
    private const float MinShapeArea = 2f;

    private enum Direction { East, North, West, South }

    [MenuItem(MenuRoot + "Collidery z pikseli - obiekty do budowania")]
    private static void GenerateForItems()
    {
        string[] paths = AssetDatabase.FindAssets("t:Texture2D", new[] { ItemsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith(ItemSuffix, System.StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Generate(paths);
    }

    [MenuItem(MenuRoot + "Collidery z pikseli - zaznaczone grafiki")]
    private static void GenerateForSelection()
    {
        Generate(Selection.objects.OfType<Texture2D>().Select(AssetDatabase.GetAssetPath).ToArray());
    }

    [MenuItem(MenuRoot + "Collidery z pikseli - zaznaczone grafiki", true)]
    private static bool CanGenerateForSelection()
    {
        return Selection.objects.OfType<Texture2D>().Any();
    }

    private static void Generate(string[] texturePaths)
    {
        if (texturePaths.Length == 0)
        {
            Debug.LogWarning("Collidery z pikseli: brak grafik do przetworzenia.");
            return;
        }

        var factories = new SpriteDataProviderFactories();
        factories.Init();

        foreach (string path in texturePaths)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                GenerateForTexture(path, importer, factories);
            }
        }

        AssetDatabase.Refresh();
    }

    private static void GenerateForTexture(string path, TextureImporter importer, SpriteDataProviderFactories factories)
    {
        // Piksele prosto z pliku PNG - bez wlaczania Read/Write na teksturze uzywanej w grze.
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!source.LoadImage(File.ReadAllBytes(path)))
        {
            Debug.LogWarning($"Collidery z pikseli: nie udalo sie odczytac {path}");
            Object.DestroyImmediate(source);
            return;
        }

        Color32[] pixels = source.GetPixels32();
        int textureWidth = source.width;
        Object.DestroyImmediate(source);

        ISpriteEditorDataProvider dataProvider = factories.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();
        var physics = dataProvider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
        var report = new List<string>();

        foreach (SpriteRect spriteRect in dataProvider.GetSpriteRects())
        {
            RectInt rect = new RectInt(
                Mathf.RoundToInt(spriteRect.rect.x), Mathf.RoundToInt(spriteRect.rect.y),
                Mathf.RoundToInt(spriteRect.rect.width), Mathf.RoundToInt(spriteRect.rect.height));
            List<Vector2[]> shapes = BuildShapes(pixels, textureWidth, rect);

            // Ksztalty zapisuje sie wzgledem srodka prostokata sprite'a, w pikselach.
            Vector2 center = new Vector2(rect.width, rect.height) * 0.5f;
            physics.SetOutlines(spriteRect.spriteID, shapes.Select(shape => shape.Select(point => point - center).ToArray()).ToList());
            physics.SetTessellationDetail(spriteRect.spriteID, 0f);
            report.Add($"{spriteRect.name}: {string.Join("+", shapes.Select(shape => shape.Length))} wierzch.");
        }

        dataProvider.Apply();
        importer.SaveAndReimport();
        Debug.Log($"Collidery z pikseli: {Path.GetFileName(path)} - {string.Join(", ", report)}");
    }

    #region Shape Building
    private static List<Vector2[]> BuildShapes(Color32[] pixels, int textureWidth, RectInt rect)
    {
        bool[,] solid = ReadMask(pixels, textureWidth, rect);
        FillHoles(solid);
        RemoveIslands(solid);

        var shapes = new List<Vector2[]>();

        foreach (List<Vector2Int> loop in TraceLoops(solid))
        {
            List<Vector2Int> corners = RemoveCollinear(loop);
            List<Vector2Int> smoothed = SimplifyClosed(corners, SmoothingPixels);

            // Wygladzenie nie moze przeciac ksztaltu samego ze soba - wtedy zostaje dokladny obrys.
            List<Vector2Int> shape = smoothed.Count >= 3 && !IsSelfIntersecting(smoothed) ? smoothed : corners;

            if (shape.Count >= 3 && Mathf.Abs(SignedArea(shape)) >= MinShapeArea)
            {
                shapes.Add(shape.Select(point => (Vector2)point).ToArray());
            }
        }

        return shapes;
    }

    private static bool[,] ReadMask(Color32[] pixels, int textureWidth, RectInt rect)
    {
        var solid = new bool[rect.width, rect.height];

        for (int y = 0; y < rect.height; y++)
        {
            for (int x = 0; x < rect.width; x++)
            {
                solid[x, y] = pixels[(rect.y + y) * textureWidth + rect.x + x].a >= AlphaThreshold;
            }
        }

        return solid;
    }

    // Puste piksele, do ktorych nie da sie dojsc z brzegu, to dziury w srodku - traktujemy je jak pelne.
    private static void FillHoles(bool[,] solid)
    {
        int width = solid.GetLength(0);
        int height = solid.GetLength(1);
        var outside = new bool[width, height];
        var queue = new Queue<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            Enqueue(x, 0);
            Enqueue(x, height - 1);
        }

        for (int y = 0; y < height; y++)
        {
            Enqueue(0, y);
            Enqueue(width - 1, y);
        }

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            Enqueue(cell.x + 1, cell.y);
            Enqueue(cell.x - 1, cell.y);
            Enqueue(cell.x, cell.y + 1);
            Enqueue(cell.x, cell.y - 1);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                solid[x, y] |= !outside[x, y];
            }
        }

        void Enqueue(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < width && y < height && !solid[x, y] && !outside[x, y])
            {
                outside[x, y] = true;
                queue.Enqueue(new Vector2Int(x, y));
            }
        }
    }

    private static void RemoveIslands(bool[,] solid)
    {
        int width = solid.GetLength(0);
        int height = solid.GetLength(1);
        var visited = new bool[width, height];

        for (int startY = 0; startY < height; startY++)
        {
            for (int startX = 0; startX < width; startX++)
            {
                if (!solid[startX, startY] || visited[startX, startY])
                {
                    continue;
                }

                var island = new List<Vector2Int>();
                var stack = new Stack<Vector2Int>();
                stack.Push(new Vector2Int(startX, startY));
                visited[startX, startY] = true;

                while (stack.Count > 0)
                {
                    Vector2Int cell = stack.Pop();
                    island.Add(cell);

                    foreach (Vector2Int next in new[] { cell + Vector2Int.right, cell + Vector2Int.left, cell + Vector2Int.up, cell + Vector2Int.down })
                    {
                        if (next.x >= 0 && next.y >= 0 && next.x < width && next.y < height && solid[next.x, next.y] && !visited[next.x, next.y])
                        {
                            visited[next.x, next.y] = true;
                            stack.Push(next);
                        }
                    }
                }

                if (island.Count < MinIslandPixels)
                {
                    foreach (Vector2Int cell in island)
                    {
                        solid[cell.x, cell.y] = false;
                    }
                }
            }
        }
    }

    // Krawedzie miedzy pelnym a pustym pikselem, skierowane tak, ze cialo jest po lewej - skladaja sie w obrysy (CCW).
    // Na rogu, gdzie dwa piksele stykaja sie tylko po skosie, skrecamy w lewo - takie piksele to osobne ksztalty.
    private static List<List<Vector2Int>> TraceLoops(bool[,] solid)
    {
        int width = solid.GetLength(0);
        int height = solid.GetLength(1);
        var outgoing = new Dictionary<Vector2Int, List<Direction>>();

        bool IsSolid(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && solid[x, y];

        void AddEdge(Vector2Int from, Direction direction)
        {
            if (!outgoing.TryGetValue(from, out List<Direction> list))
            {
                list = new List<Direction>(2);
                outgoing.Add(from, list);
            }

            list.Add(direction);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!solid[x, y])
                {
                    continue;
                }

                if (!IsSolid(x, y - 1)) AddEdge(new Vector2Int(x, y), Direction.East);
                if (!IsSolid(x + 1, y)) AddEdge(new Vector2Int(x + 1, y), Direction.North);
                if (!IsSolid(x, y + 1)) AddEdge(new Vector2Int(x + 1, y + 1), Direction.West);
                if (!IsSolid(x - 1, y)) AddEdge(new Vector2Int(x, y + 1), Direction.South);
            }
        }

        var loops = new List<List<Vector2Int>>();

        while (outgoing.Count > 0)
        {
            // Start w zwyklym rogu (jedna krawedz wychodzaca), nie na styku po skosie - obrys nie sklei sie w osemke.
            Vector2Int start = outgoing.FirstOrDefault(pair => pair.Value.Count == 1).Key;

            if (!outgoing.ContainsKey(start))
            {
                start = outgoing.Keys.First();
            }

            Direction direction = TakeEdge(outgoing, start, null);
            var loop = new List<Vector2Int> { start };
            Vector2Int point = start + Step(direction);

            while (point != start)
            {
                loop.Add(point);
                direction = TakeEdge(outgoing, point, direction);
                point += Step(direction);
            }

            loops.Add(loop);
        }

        return loops;
    }

    private static Direction TakeEdge(Dictionary<Vector2Int, List<Direction>> outgoing, Vector2Int from, Direction? arriving)
    {
        List<Direction> options = outgoing[from];
        Direction chosen = options[0];

        if (arriving.HasValue && options.Count > 1)
        {
            Direction current = arriving.Value;
            Direction[] preference = { (Direction)(((int)current + 1) % 4), current, (Direction)(((int)current + 3) % 4) };
            chosen = preference.First(options.Contains);
        }

        options.Remove(chosen);

        if (options.Count == 0)
        {
            outgoing.Remove(from);
        }

        return chosen;
    }

    private static Vector2Int Step(Direction direction)
    {
        switch (direction)
        {
            case Direction.East: return Vector2Int.right;
            case Direction.North: return Vector2Int.up;
            case Direction.West: return Vector2Int.left;
            default: return Vector2Int.down;
        }
    }

    private static List<Vector2Int> RemoveCollinear(List<Vector2Int> loop)
    {
        var corners = new List<Vector2Int>();

        for (int i = 0; i < loop.Count; i++)
        {
            Vector2Int previous = loop[(i - 1 + loop.Count) % loop.Count];
            Vector2Int next = loop[(i + 1) % loop.Count];
            Vector2Int point = loop[i];

            if ((point - previous) != (next - point))
            {
                corners.Add(point);
            }
        }

        return corners;
    }
    #endregion

    #region Simplification
    // Ramer-Douglas-Peucker na zamknietym obrysie: wierzcholki zostaja podzbiorem rogow pikseli.
    private static List<Vector2Int> SimplifyClosed(List<Vector2Int> points, float tolerance)
    {
        if (points.Count <= 4)
        {
            return new List<Vector2Int>(points);
        }

        int far = 0;
        float farDistance = -1f;

        for (int i = 1; i < points.Count; i++)
        {
            float distance = (points[i] - points[0]).sqrMagnitude;

            if (distance > farDistance)
            {
                farDistance = distance;
                far = i;
            }
        }

        var first = points.GetRange(0, far + 1);
        var second = points.GetRange(far, points.Count - far);
        second.Add(points[0]);

        var result = new List<Vector2Int>();
        result.AddRange(Simplify(first, tolerance));
        result.RemoveAt(result.Count - 1);
        result.AddRange(Simplify(second, tolerance));
        result.RemoveAt(result.Count - 1);
        return result;
    }

    private static List<Vector2Int> Simplify(List<Vector2Int> points, float tolerance)
    {
        if (points.Count < 3)
        {
            return new List<Vector2Int>(points);
        }

        Vector2 start = points[0];
        Vector2 end = points[points.Count - 1];
        int index = 0;
        float maxDistance = 0f;

        for (int i = 1; i < points.Count - 1; i++)
        {
            float distance = DistanceToSegment(points[i], start, end);

            if (distance > maxDistance)
            {
                maxDistance = distance;
                index = i;
            }
        }

        if (maxDistance <= tolerance)
        {
            return new List<Vector2Int> { points[0], points[points.Count - 1] };
        }

        List<Vector2Int> left = Simplify(points.GetRange(0, index + 1), tolerance);
        List<Vector2Int> right = Simplify(points.GetRange(index, points.Count - index), tolerance);
        left.RemoveAt(left.Count - 1);
        left.AddRange(right);
        return left;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;

        if (lengthSquared < Mathf.Epsilon)
        {
            return Vector2.Distance(point, start);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
        return Vector2.Distance(point, start + segment * t);
    }

    private static bool IsSelfIntersecting(List<Vector2Int> polygon)
    {
        int count = polygon.Count;

        for (int i = 0; i < count; i++)
        {
            Vector2 a1 = polygon[i];
            Vector2 a2 = polygon[(i + 1) % count];

            for (int j = i + 2; j < count; j++)
            {
                if (i == 0 && j == count - 1)
                {
                    continue;  // sasiednie przez zamkniecie obrysu
                }

                if (SegmentsIntersect(a1, a2, polygon[j], polygon[(j + 1) % count]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float d1 = Cross(q2 - q1, p1 - q1);
        float d2 = Cross(q2 - q1, p2 - q1);
        float d3 = Cross(p2 - p1, q1 - p1);
        float d4 = Cross(p2 - p1, q2 - p1);
        return d1 * d2 <= 0f && d3 * d4 <= 0f && !(d1 == 0f && d2 == 0f);
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float SignedArea(List<Vector2Int> polygon)
    {
        float area = 0f;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2Int a = polygon[i];
            Vector2Int b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5f;
    }
    #endregion
}
#endregion
