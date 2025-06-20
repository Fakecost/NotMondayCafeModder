using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using Steamworks;
using UnityEditor.U2D;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

[Serializable]
public class SpriteLibraryGeneratorWindow : EditorWindow
{
    private int selectedTab = 0;
    private string lastBundlePath = null;
    private string characterName = "NewCharacter";
    private List<string> additionalNames = new();
    private Vector2 scroll;
    private Dictionary<string, int> frameIndex = new();
    private double lastFrameTime;
    private DefaultAsset spriteFolder;
    private string generatedAssetPath = null;
    private SheetCategory currentlyPreviewingCategory = null;
    private float uploadProgress = 0f;

    private class SheetCategory
    {
        public string categoryName;
        public Texture2D spriteSheet;
        public List<Sprite> previewSprites = new();
        public bool isPlaying;
        public bool isSliced;
    }

    private List<SheetCategory> categories = new List<SheetCategory>
    {
        new SheetCategory { categoryName = "Idle" },
        new SheetCategory { categoryName = "Idle-30-Happy" },
        new SheetCategory { categoryName = "Move-Back" },
        new SheetCategory { categoryName = "Move-Front" },
        new SheetCategory { categoryName = "Move-Side" },
        new SheetCategory { categoryName = "Sit-30-Happy" },
        new SheetCategory { categoryName = "Sit-30-Talk" },
        new SheetCategory { categoryName = "Sit-60-Talk" },
        new SheetCategory { categoryName = "Sit-Eat" },
        new SheetCategory { categoryName = "Sit-Idle" },
        new SheetCategory { categoryName = "Sit-Start" },
        new SheetCategory { categoryName = "Sit-Talk" }
    };
    
    private class EditorUploadProgress : IProgress<float>
    {
        private Action<float> onProgress;

        public EditorUploadProgress(Action<float> onProgress)
        {
            this.onProgress = onProgress;
        }

        public void Report(float value)
        {
            onProgress?.Invoke(value);
        }
    }

    [MenuItem("Tools/Sprite Library Generator")]
    public static void ShowWindow()
    {
        GetWindow<SpriteLibraryGeneratorWindow>("Sprite Library Generator");
    }

    private void OnEnable()
    {
        EditorApplication.update += Repaint;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Repaint;
    }

    private void OnGUI()
    {
        
        selectedTab = GUILayout.Toolbar(selectedTab, new[] { "Generate Sprite Library", "Steam Workshop" });

        switch (selectedTab)
        {
            case 0:
                DrawSpriteLibraryGeneratorGUI();
                break;
            case 1:
                DrawSteamWorkshopUploaderGUI();
                break;
        }
     
    }
 

    private void DrawSpriteLibraryGeneratorGUI()
    {
            GUILayout.Label("Sprite Library Generator", EditorStyles.boldLabel);

        characterName = EditorGUILayout.TextField("Character Name", characterName);
        spriteFolder = (DefaultAsset)EditorGUILayout.ObjectField("Auto Fill from Folder", spriteFolder, typeof(DefaultAsset), false);

        GUILayout.Label("Additional Names");
        int toRemove = -1;
        for (int i = 0; i < additionalNames.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            additionalNames[i] = EditorGUILayout.TextField(additionalNames[i]);
            if (GUILayout.Button("X", GUILayout.Width(20))) toRemove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (toRemove >= 0) additionalNames.RemoveAt(toRemove);
        if (GUILayout.Button("Add Additional Name")) additionalNames.Add("");

        if (spriteFolder != null && GUILayout.Button("🔍 Auto Assign Sprites from Folder"))
        {
            string folderPath = AssetDatabase.GetAssetPath(spriteFolder);
            List<Object> atlasSources = new();

            foreach (var cat in categories)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
                string bestMatch = null;
                int shortestLength = int.MaxValue;

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (fileName.Contains(cat.categoryName) && fileName.Length < shortestLength)
                    {
                        bestMatch = path;
                        shortestLength = fileName.Length;
                    }
                }

                if (!string.IsNullOrEmpty(bestMatch))
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(bestMatch);
                    ApplySpriteSheetImportSettings(texture);
                    SliceCategory(new SheetCategory { categoryName = cat.categoryName, spriteSheet = texture });
                    cat.spriteSheet = texture;
                    atlasSources.Add(texture);
                }
            }

            if (atlasSources.Count > 0)
            {
                string atlasPath = Path.Combine("Assets/Mods", characterName, characterName + "_Atlas.spriteatlas");
                Directory.CreateDirectory(Path.GetDirectoryName(atlasPath));
                atlasPath = atlasPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");

                var atlas = new SpriteAtlas();
                atlas.SetPackingSettings(new SpriteAtlasPackingSettings
                {
                    enableRotation = false,
                    enableTightPacking = false,
                    padding = 4
                });
                atlas.SetTextureSettings(new SpriteAtlasTextureSettings
                {
                    readable = false,
                    generateMipMaps = false,
                    sRGB = true,
                    filterMode = FilterMode.Point
                });
                var platformSettings = new TextureImporterPlatformSettings
                {
                    overridden = true,
                    name = "Standalone",
                    maxTextureSize = 2048,
                    format = TextureImporterFormat.RGBA32,
                    textureCompression = TextureImporterCompression.Uncompressed
                };
                atlas.SetPlatformSettings(platformSettings);
                atlas.Add(atlasSources.ToArray());
                AssetDatabase.CreateAsset(atlas, atlasPath);
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
                foreach (var tex in atlasSources)
                {
                    string texPath = AssetDatabase.GetAssetPath(tex);
                    AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                foreach (var cat in categories)
                {
                    if (cat.spriteSheet == null) continue;
                    string path = AssetDatabase.GetAssetPath(cat.spriteSheet);
                    Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                    cat.previewSprites = allAssets.OfType<Sprite>().Where(s => s.texture == cat.spriteSheet).OrderBy(s => s.name).ToList();
                    cat.isSliced = cat.previewSprites.Count >= 8;
                }

                Debug.Log("✅ Sprite Atlas created at: " + atlasPath);
            }
        }

        if (GUILayout.Button("✂ All Slice SpriteSheets"))
        {
            foreach (var cat in categories)
            {
                if (cat.spriteSheet != null)
                {
                    SliceCategory(cat);
                }
            }
        }

        GUILayout.Space(10);
        GUILayout.Label("🎯 Drop SpriteSheets by Category", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        foreach (var category in categories)
        {
            EditorGUILayout.BeginVertical("box");
            category.spriteSheet = (Texture2D)EditorGUILayout.ObjectField(category.categoryName, category.spriteSheet, typeof(Texture2D), false);

            if (category.spriteSheet != null)
            {
                string path = AssetDatabase.GetAssetPath(category.spriteSheet);
                Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                category.previewSprites = allAssets.OfType<Sprite>().Where(s => s.texture == category.spriteSheet).OrderBy(s => s.name).ToList();
                category.isSliced = category.previewSprites.Count >= 8;

                Rect previewRect = GUILayoutUtility.GetRect(128, 128);
                if (category.previewSprites.Count > 0)
                {
                    if (!frameIndex.ContainsKey(category.categoryName)) frameIndex[category.categoryName] = 0;

                    if (category == currentlyPreviewingCategory && category.isPlaying && EditorApplication.timeSinceStartup - lastFrameTime > (1f / 8f))
                    {
                        frameIndex[category.categoryName] = (frameIndex[category.categoryName] + 1) % category.previewSprites.Count;
                        lastFrameTime = EditorApplication.timeSinceStartup;
                    }

                    Sprite current = category.previewSprites[frameIndex[category.categoryName]];
                    Texture2D tex = AssetPreview.GetAssetPreview(current) ?? current.texture;
                    GUI.DrawTexture(previewRect, tex, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    GUI.Box(GUILayoutUtility.GetRect(128, 128), "No Sprites");
                }

                if (!category.isSliced)
                {
                    if (GUILayout.Button("✂ Slice SpriteSheet", GUILayout.Width(160)))
                    {
                        SliceCategory(category);
                    }
                }
                else
                {
                    bool playRequest = GUILayout.Toggle(category.isPlaying, category.isPlaying ? "⏸ Pause Preview" : "▶ Play Preview", "Button", GUILayout.Width(150));
                    if (playRequest && !category.isPlaying)
                    {
                        foreach (var cat in categories) cat.isPlaying = false;
                        category.isPlaying = true;
                        currentlyPreviewingCategory = category;
                    }
                    else if (!playRequest && category.isPlaying)
                    {
                        category.isPlaying = false;
                        if (currentlyPreviewingCategory == category) currentlyPreviewingCategory = null;
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Space(20);
        if (GUILayout.Button("📦 Generate Sprite Library", GUILayout.Height(40)))
        {
            foreach (var cat in categories)
            {
                if (cat.spriteSheet != null)
                {
                    ApplySpriteSheetImportSettings(cat.spriteSheet);
                    SliceCategory(cat);
                    string texPath = AssetDatabase.GetAssetPath(cat.spriteSheet);
                    Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(texPath);
                    cat.previewSprites = allAssets.OfType<Sprite>().Where(s => s.texture == cat.spriteSheet).OrderBy(s => s.name).ToList();
                    cat.isSliced = cat.previewSprites.Count >= 8;
                }
            }
            GenerateSpriteLibraryAsAsset();
        }
    }

    private void GenerateSpriteLibraryAsAsset()
    {
        string folder = Path.Combine("Assets/Mods", characterName);
        Directory.CreateDirectory(folder);

        string outputPath = Path.Combine(folder, $"{characterName}_Library.asset");
        generatedAssetPath = outputPath;

        SpriteLibraryAsset spriteLib = ScriptableObject.CreateInstance<SpriteLibraryAsset>();

        foreach (var cat in categories)
        {
            if (cat.spriteSheet == null) continue;
            string path = AssetDatabase.GetAssetPath(cat.spriteSheet);
            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            List<Sprite> sprites = allAssets.OfType<Sprite>().Where(s => s.texture == cat.spriteSheet).OrderBy(s => s.name).ToList();

            for (int i = 0; i < sprites.Count; i++)
            {
                spriteLib.AddCategoryLabel(sprites[i], cat.categoryName, i.ToString());
            }
        }

        string jsonPath = Path.Combine(folder, "AdditionalNames.json");
        var jsonData = new AdditionalNameData
        {
            mainName = characterName,
            names = new List<string>(additionalNames)
        };
        File.WriteAllText(jsonPath, JsonUtility.ToJson(jsonData, true));
        AssetDatabase.ImportAsset(jsonPath.Replace(Application.dataPath, "Assets"));

        AssetDatabase.CreateAsset(spriteLib, outputPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ExportAsAssetBundle(spriteLib, folder);
        Debug.Log("✅ SpriteLibraryAsset (.asset) + JSON created and bundled at: " + outputPath);
    }

    private void ApplySpriteSheetImportSettings(Texture2D texture)
    {
        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            bool changed = false;

            if (ti.spriteImportMode != SpriteImportMode.Multiple) { ti.spriteImportMode = SpriteImportMode.Multiple; changed = true; }
            if (Math.Abs(ti.spritePixelsPerUnit - 99.99f) > 0.01f) { ti.spritePixelsPerUnit = 99.99f; changed = true; }
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.alphaSource != TextureImporterAlphaSource.FromInput) { ti.alphaSource = TextureImporterAlphaSource.FromInput; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (ti.maxTextureSize != 4096) { ti.maxTextureSize = 4096; changed = true; }
#if UNITY_2023_1_OR_NEWER
            if (ti.resizeAlgorithm != TextureResizeAlgorithm.Mitchell) { ti.resizeAlgorithm = TextureResizeAlgorithm.Mitchell; changed = true; }
#endif
            if (ti.crunchedCompression) { ti.crunchedCompression = false; changed = true; }
            if (changed)
            {
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
            }
        }
    }

    private void SliceCategory(SheetCategory cat)
    {
        string path = AssetDatabase.GetAssetPath(cat.spriteSheet);
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null)
        {
            int columns = 8;
            int rows = 1;
            int width = cat.spriteSheet.width / columns;
            int height = cat.spriteSheet.height / rows;

            List<SpriteMetaData> metas = new List<SpriteMetaData>();
            for (int i = 0; i < columns; i++)
            {
                SpriteMetaData meta = new SpriteMetaData();
                meta.rect = new Rect(i * width, 0, width, height);
                meta.name = $"{cat.categoryName}_{i}";
                meta.alignment = (int)SpriteAlignment.Custom;
                meta.pivot = new Vector2(0.5f, 0f);
                metas.Add(meta);
            }

            ti.spriteImportMode = SpriteImportMode.Multiple;
            ti.spritesheet = metas.ToArray();
            EditorUtility.SetDirty(ti);
            ti.SaveAndReimport();
        }
    }

    private void ExportAsAssetBundle(SpriteLibraryAsset spriteLib, string folder)
    {
        string spriteLibPath = AssetDatabase.GetAssetPath(spriteLib);
        string jsonPath = Path.Combine(folder, "AdditionalNames.json");
        string atlasPath = Path.Combine(folder, $"{characterName}_Atlas.spriteatlas");
        atlasPath = atlasPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");

        string relativeJsonPath = jsonPath.Replace(Application.dataPath, "Assets").Replace("\\", "/");

        string outputDir = Path.Combine(folder, "Bundle");
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        List<string> validAssets = new();
        if (File.Exists(spriteLibPath)) validAssets.Add(spriteLibPath);
        if (File.Exists(relativeJsonPath)) validAssets.Add(relativeJsonPath);
        if (File.Exists(atlasPath)) validAssets.Add(atlasPath);

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = characterName.ToLowerInvariant() + ".customer",
            assetNames = validAssets.ToArray()
        };

        try
        {
            EditorUtility.DisplayProgressBar("Building Mod Bundle", $"Bundling {characterName}...", 0.5f);

            BuildPipeline.BuildAssetBundles(
                outputDir,
                new[] { build },
                BuildAssetBundleOptions.UncompressedAssetBundle,
                EditorUserBuildSettings.activeBuildTarget
            );
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        lastBundlePath = Path.Combine(outputDir, characterName.ToLowerInvariant() + ".customer");
        Debug.Log($"✅ AssetBundle exported to: {lastBundlePath}");
        AssetDatabase.Refresh();

        // foreach (var cat in categories)
        // {
        //     if (cat.spriteSheet == null) continue;
        //     string path = AssetDatabase.GetAssetPath(cat.spriteSheet);
        //     Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
        //     cat.previewSprites = allAssets.OfType<Sprite>().Where(s => s.texture == cat.spriteSheet).OrderBy(s => s.name).ToList();
        //     cat.isSliced = cat.previewSprites.Count >= 8;
        // }
    }
    
    private void DrawSteamWorkshopUploaderGUI()
    {
        GUILayout.Space(10);

        if (!string.IsNullOrEmpty(lastBundlePath) && File.Exists(lastBundlePath))
        {
            GUILayout.Label("Bundle Path:", EditorStyles.boldLabel);
            EditorGUILayout.TextField(lastBundlePath);

            GUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(Application.isPlaying == false);
            if (GUILayout.Button("☁ Upload to Steam Workshop", GUILayout.Height(30)))
            {
                UploadToSteamWorkshop(lastBundlePath);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(5);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), uploadProgress, $"Uploading... {Mathf.RoundToInt(uploadProgress * 100)}%");
        }
        else
        {
            EditorGUILayout.HelpBox("No bundle found. Please generate a mod bundle first.", MessageType.Info);
        }
    }

    private async void UploadToSteamWorkshop(string bundlePath)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Steam Upload Error", "Please enter Play Mode before uploading to Steam Workshop.", "OK");
            return;
        }

        if (!SteamClient.IsValid)
        {
            Debug.LogError("❌ Steam is not initialized. Please run in Play Mode or build the game and launch via Steam.");
            return;
        }

        uploadProgress = 0f;
        Debug.Log("📦 Creating item on Steam Workshop...");
        var result = await Steamworks.Ugc.Editor.NewCommunityFile
            .WithTitle("Mod: " + characterName)
            .WithDescription("Auto-uploaded mod from Sprite Library Generator")
            .WithContent(Path.GetDirectoryName(bundlePath))
            .WithTag("Mod")
            .WithTag("Character")
            .SubmitAsync(new EditorUploadProgress(p => uploadProgress = p));

        Debug.Log(result.Success.ToString() + result.NeedsWorkshopAgreement.ToString());

        if (!result.Success)
        {
            Debug.LogError("❌ Upload failed.");
        }
        else
        {
            Debug.Log("✅ Upload complete!");
        }
      
    }


    [Serializable]
    private class AdditionalNameData
    {
        public string mainName;
        public List<string> names;
    }
}
