using System;
using System.Collections.Generic;

namespace Weave.World.WFC
{
    public sealed class WFCGridCell
    {
        private readonly HashSet<int> possibleTileIndices = new HashSet<int>();

        public WFCGridCell(IEnumerable<int> initialTiles)
        {
            foreach (var tileIndex in initialTiles)
            {
                possibleTileIndices.Add(tileIndex);
            }
        }

        public int Entropy => possibleTileIndices.Count;

        public bool IsCollapsed => possibleTileIndices.Count == 1;

        public IEnumerable<int> PossibleTileIndices => possibleTileIndices;

        public int GetCollapsedTileIndex()
        {
            if (possibleTileIndices.Count != 1)
            {
                throw new InvalidOperationException("Cell is not collapsed.");
            }

            foreach (var tileIndex in possibleTileIndices)
            {
                return tileIndex;
            }

            throw new InvalidOperationException("Collapsed tile index was not found.");
        }

        public bool CollapseTo(int tileIndex)
        {
            if (!possibleTileIndices.Contains(tileIndex))
            {
                return false;
            }

            if (possibleTileIndices.Count == 1)
            {
                return true;
            }

            possibleTileIndices.Clear();
            possibleTileIndices.Add(tileIndex);
            return true;
        }

        public bool RemoveWhere(Func<int, bool> predicate)
        {
            var toRemove = new List<int>();
            foreach (var tileIndex in possibleTileIndices)
            {
                if (predicate(tileIndex))
                {
                    toRemove.Add(tileIndex);
                }
            }

            if (toRemove.Count == 0)
            {
                return false;
            }

            foreach (var tileIndex in toRemove)
            {
                possibleTileIndices.Remove(tileIndex);
            }

            return true;
        }
    }
}
