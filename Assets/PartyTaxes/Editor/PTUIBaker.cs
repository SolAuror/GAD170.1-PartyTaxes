using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;
using PartyTaxes;

/// <summary>
/// Party Taxes > Bake UI into Scene: turns the UI the game builds at runtime into real objects you can edit.
///  1. Saves the UI fonts as TMP font assets (Resources/PartyTaxes/UI/Fonts/*  SDF.asset), so prefabs can use them.
///  2. Bakes the ledger book (PT Action Bar), the writ (PT Run Setup), the Final Ledger and the character tag
///     (PT World Tag) as prefabs in Resources/PartyTaxes/UI/Baked, and puts the three screens into the open scene.
///     The writ and the Final Ledger are switched off in the scene (they open themselves); switch them on to edit.
///     Each view of the ledger book is its own "View.*" object under Book: switch one on to edit it.
///  3. Restyles the HUD prefab (UI_PTonScreen) in place: the note, banner, hang tags and the log scroll.
/// At runtime the game finds the baked copies and re-binds them by name instead of building its own, so what you
/// change in the editor (positions, sizes, fonts, colours, wording) is what the player sees. Keep the object names.
/// Baking again replaces the baked screens in the scene (and their edits), so commit first.
/// </summary>
public static class PTUIBaker
{
    private const string UIFolder = "Assets/PartyTaxes/Resources/PartyTaxes/UI";
    private const string FontFolder = UIFolder + "/Fonts";
    private const string BakedFolder = UIFolder + "/Baked";
    private const string HudPrefab = "Assets/PartyTaxes/UI/UI_PTonScreen.prefab";

    [MenuItem("Party Taxes/Bake UI into Scene")]
    public static void BakeUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Bake UI", "Stop Play mode first.", "OK");
            return;
        }
        PTManager manager = Object.FindAnyObjectByType<PTManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Bake UI", "Open the game scene (the one with the PTManager) first.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Bake UI",
                "This will:\n" +
                "- save the UI fonts as TMP font assets\n" +
                "- bake the ledger book, writ, Final Ledger and character tag as prefabs in " + BakedFolder + "\n" +
                "- put the screens into the open scene, replacing any baked before (and their edits)\n" +
                "- restyle " + HudPrefab + " in place\n\n" +
                "Commit first so you can revert.", "Bake", "Cancel"))
        {
            return;
        }

        SaveFontAssets();
        PTUITheme.ClearCache();                                                            // use the saved fonts and imported sprites from here on
        if (!AssetDatabase.IsValidFolder(BakedFolder)) AssetDatabase.CreateFolder(UIFolder, "Baked");

        RemoveFromScene<PTActionBar>();
        RemoveFromScene<PTRunSetupPanel>();
        RemoveFromScene<PTFinalLedgerPanel>();
        PlaceInScene(PTActionBar.Bake(manager), PTActionBar.PrefabName, true);
        PlaceInScene(PTRunSetupPanel.Bake(manager), PTRunSetupPanel.PrefabName, false);
        PlaceInScene(PTFinalLedgerPanel.Bake(manager), PTFinalLedgerPanel.PrefabName, false);

        GameObject tag = PTWorldTag.Bake();                                                // characters' tags: a prefab only, copied per character at runtime
        PrefabUtility.SaveAsPrefabAsset(tag, BakedFolder + "/" + PTWorldTag.PrefabName + ".prefab");
        Object.DestroyImmediate(tag);

        BakeHud();

        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Party Taxes: UI baked. Edit the screens in the scene (PT Action Bar, PT Run Setup, PT Final Ledger), the tag prefab in " +
                  BakedFolder + " and the HUD in " + HudPrefab + ". Save the scene to keep them.");
    }

    /// The TTFs as dynamic TMP font assets beside them; the body font gets its bold and italic faces.
    private static void SaveFontAssets()
    {
        foreach (string file in PTUITheme.FontFiles)
        {
            string path = FontAssetPath(file);
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) continue;
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "/" + file + ".ttf");
            if (font == null)
            {
                Debug.LogWarning("Party Taxes: missing font " + FontFolder + "/" + file + ".ttf");
                continue;
            }
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = file + PTUITheme.FontAssetSuffix;
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = file + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = file + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            if (TMP_Settings.defaultFontAsset != null)
            {
                asset.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };   // symbols the old fonts lack
            }
            EditorUtility.SetDirty(asset);
        }
        AssetDatabase.SaveAssets();

        TMP_FontAsset body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath(PTUITheme.BodyFont));
        if (body != null && body.fontWeightTable != null && body.fontWeightTable.Length > 7)
        {
            body.fontWeightTable[7].regularTypeface = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath(PTUITheme.BodyBoldFont));
            body.fontWeightTable[4].italicTypeface = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath(PTUITheme.BodyItalicFont));
            EditorUtility.SetDirty(body);
            AssetDatabase.SaveAssets();
        }
    }

    private static string FontAssetPath(string file) => FontFolder + "/" + file + PTUITheme.FontAssetSuffix + ".asset";

    /// Saves a baked screen as a prefab and leaves it in the scene as an instance of that prefab.
    private static void PlaceInScene(GameObject root, string name, bool active)
    {
        GameObject instance = PrefabUtility.SaveAsPrefabAssetAndConnect(root, BakedFolder + "/" + name + ".prefab", InteractionMode.AutomatedAction);
        instance.SetActive(active);
        Undo.RegisterCreatedObjectUndo(instance, "Bake UI");
    }

    private static void RemoveFromScene<T>() where T : Component
    {
        foreach (T found in Resources.FindObjectsOfTypeAll<T>())
        {
            if (found == null || EditorUtility.IsPersistent(found) || !found.gameObject.scene.IsValid()) continue;   // scene objects only, not prefab assets
            Undo.DestroyObjectImmediate(found.gameObject);
        }
    }

    /// Restyles the HUD prefab itself (it's nested in the manager prefab, so it can't be restructured in the scene).
    private static void BakeHud()
    {
        GameObject hud = PrefabUtility.LoadPrefabContents(HudPrefab);
        try
        {
            PTUIFactory.StyleHud(hud.transform);
            PTAdventureLog log = hud.GetComponentInChildren<PTAdventureLog>(true);
            if (log != null) log.ApplyTheme();
            PrefabUtility.SaveAsPrefabAsset(hud, HudPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(hud);
        }
    }
}
