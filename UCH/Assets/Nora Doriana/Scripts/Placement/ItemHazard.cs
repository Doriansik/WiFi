using UnityEngine;

#region Item Hazard
// Zabojcza czesc obiektu (zeby widelca od przodu, ostrze noza). Postac, ktora jej dotknie, odpada z rundy.
// Strefa jest wyzwalaczem lekko wystajacym poza grafike - wystarczy dotknac, nie trzeba wejsc w obiekt.
public class ItemHazard : MonoBehaviour
{
    private const float PixelsPerUnit = 32f;

    // area: piksele w kafelku obiektu (jak na arkuszu PNG, od lewego dolnego rogu, przed obrotem).
    public static void Attach(GameObject root, PlaceableItem item, RectInt area)
    {
        var zone = new GameObject("Hazard");
        zone.transform.SetParent(root.transform, false);

        Vector2 framePixels = (Vector2)item.Size * PixelsPerUnit;
        BoxCollider2D trigger = zone.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(area.width, area.height) / PixelsPerUnit;
        trigger.offset = (area.center - framePixels * 0.5f) / PixelsPerUnit;

        zone.AddComponent<ItemHazard>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Kill(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        Kill(other);
    }

    private static void Kill(Collider2D other)
    {
        if (other.TryGetComponent(out PlayerBuilder player))
        {
            player.ReportDeath();
        }
    }
}
#endregion
