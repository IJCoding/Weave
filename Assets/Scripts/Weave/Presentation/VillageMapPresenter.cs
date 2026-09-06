using System;
using System.Collections.Generic;
using UnityEngine;
using Weave.Data;
using Weave.Simulation;

namespace Weave.Presentation
{
    public sealed class VillageMapPresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class CharacterMarker
        {
            public CharacterDefinition Character = null!;
            public SpriteRenderer MarkerRenderer = null!;
        }

        [SerializeField] private PrototypeGameSession session;
        [SerializeField] private List<CharacterMarker> markers = new List<CharacterMarker>();

        private void Update()
        {
            if (session == null || session.RunState == null)
            {
                return;
            }

            foreach (var marker in markers)
            {
                if (marker.Character == null || marker.MarkerRenderer == null)
                {
                    continue;
                }

                marker.MarkerRenderer.transform.position =
                    session.GetCharacterMapPosition(marker.Character.CharacterId);
                marker.MarkerRenderer.color = session.GetCharacterColor(marker.Character.CharacterId);
            }
        }
    }
}
