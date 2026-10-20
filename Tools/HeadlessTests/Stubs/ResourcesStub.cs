// Headless stand-ins for Unity's Resources and JsonUtility: Resources.Load reads the real files under
// Assets/**/Resources, and JsonUtility maps onto System.Text.Json (public fields, missing fields keep defaults).
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UnityEngine
{
    public class TextAsset : Object { public string text; public TextAsset(string t) { text = t; } }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object
        {
            if (typeof(T) != typeof(TextAsset)) return null;                     // fonts and textures: the game falls back to defaults
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets"))) dir = Path.GetDirectoryName(dir);
            if (dir == null) return null;
            string suffix = Path.Combine("Resources", path.Replace('/', Path.DirectorySeparatorChar));
            string file = Directory.GetFiles(Path.Combine(dir, "Assets"), "*.*", SearchOption.AllDirectories)
                                   .FirstOrDefault(f => !f.EndsWith(".meta") && Path.ChangeExtension(f, null).EndsWith(suffix));
            if (file == null) return null;
            return new TextAsset(File.ReadAllText(file)) as T;
        }
    }

    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
}
