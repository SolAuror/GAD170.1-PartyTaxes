using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace PartyTaxes
{
    /// <summary>
    /// Builds a piece of UI from code, or re-binds one that was baked into the scene or a prefab (Party Taxes > Bake UI).
    /// Every named element goes through Make: if the baked copy has an element with that name it's reused as it is,
    /// keeping whatever was changed in the editor (size, position, font, colour, text); otherwise it's created.
    /// So the same build code drives a fresh UI, a baked one, and the bake itself. Names must be unique within one UI.
    /// </summary>
    public class PTUIBuilder
    {
        public const string BakedPath = "PartyTaxes/UI/Baked/";                            // baked prefabs, under Resources

        private readonly Dictionary<string, Transform> baked = new Dictionary<string, Transform>();

        /// True while the editor is baking: there's no game state, so text gets a preview instead.
        public bool Baking { get; private set; }

        /// True when re-binding a baked copy (things set only at creation are left alone).
        public bool Rebinding => baked.Count > 0;

        public PTUIBuilder(Transform bakedRoot = null, bool baking = false)
        {
            Baking = baking;
            if (bakedRoot != null) Index(bakedRoot);
        }

        private void Index(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (!baked.ContainsKey(child.name)) baked[child.name] = child;
                Index(child);
            }
        }

        /// The baked element called id, or a new one from create (which is then given that name).
        public T Make<T>(string id, Func<T> create) where T : Component
        {
            Transform found;
            if (baked.TryGetValue(id, out found) && found != null)
            {
                T reused = found.GetComponent<T>();
                if (reused != null) return reused;
            }
            T made = create();
            made.gameObject.name = id;
            return made;
        }

        /// While baking, fills in a label so it can be seen and styled in the editor: its real text if that can be
        /// worked out without a game running, otherwise the fallback.
        public void Preview(TMP_Text text, Func<string> getText, string fallback)
        {
            if (!Baking || text == null) return;
            try
            {
                text.text = getText();
            }
            catch (Exception)
            {
                text.text = fallback;
            }
            if (string.IsNullOrEmpty(text.text)) text.text = fallback;
        }

        /// A baked copy of a screen UI that carries component T: the one in the open scene if there is one (even if
        /// it's switched off), otherwise a copy of the prefab saved in Resources/PartyTaxes/UI/Baked. Null if neither.
        public static T FindBaked<T>(string prefabName) where T : Component
        {
            T inScene = UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (inScene != null)
            {
                inScene.gameObject.SetActive(true);
                return inScene;
            }
            return InstantiateBaked<T>(prefabName);
        }

        /// A new copy of a baked prefab from Resources/PartyTaxes/UI/Baked, or null if it hasn't been baked.
        public static T InstantiateBaked<T>(string prefabName) where T : Component
        {
            GameObject prefab = Resources.Load<GameObject>(BakedPath + prefabName);
            if (prefab == null) return null;
            GameObject copy = UnityEngine.Object.Instantiate(prefab);
            copy.name = prefabName;
            return copy.GetComponent<T>();
        }
    }
}
