using System.Collections.Generic;
using UnityEngine;

#region Map Block
// Staly element mapy (np. skrzynia pod startem i meta) - nic nie mozna postawic w jego kratkach.
// Rozmiar bierze z SpriteRenderer (tryb Sliced), wiec wystarczy rozciagnac sprite w scenie i dopasowac kolider.
public class MapBlock : MonoBehaviour
{
    private static readonly List<MapBlock> _blocks = new List<MapBlock>();

    [SerializeField] private SpriteRenderer _spriteRenderer;

    public static IReadOnlyList<MapBlock> Blocks => _blocks;

    public RectInt BlockedCells => BuildGrid.GetCells(_spriteRenderer.bounds);

    private void OnEnable()
    {
        _blocks.Add(this);
    }

    private void OnDisable()
    {
        _blocks.Remove(this);
    }
}
#endregion
