// Minimal signature-only stubs of the Unity / TMP / InputSystem API surface used by PTCore.
// Exists only to type-check scripts outside the editor. Behaviour is irrelevant.
using System;
using System.Collections;
namespace UnityEngine
{
    public class Object { public string name; public static T Instantiate<T>(T o, Transform p) where T : Object => o; public static T Instantiate<T>(T o, Vector3 pos, Quaternion r, Transform p) where T : Object => o; public static T Instantiate<T>(T o) where T : Object => o; public static void Destroy(Object o) {} public static T FindFirstObjectByType<T>() where T : Object => null; public static implicit operator bool(Object o) => o != null; }
    public class Component : Object { public GameObject gameObject; public Transform transform; public T GetComponent<T>() => default; public T GetComponentInChildren<T>() => default; public T GetComponentInParent<T>() => default; }
    public class Behaviour : Component { public bool enabled; public bool isActiveAndEnabled; }
    public class MonoBehaviour : Behaviour { public static bool RunCoroutines = true; public Coroutine StartCoroutine(IEnumerator e) { int guard = 0; if (RunCoroutines) while (guard++ < 1000000 && e.MoveNext()) { } return new Coroutine(); } public void StopAllCoroutines() {} public void StopCoroutine(Coroutine c) {} }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => default; }
    public sealed class GameObject : Object { public GameObject() {} public GameObject(string n, params Type[] c) {} public Transform transform; public bool activeSelf; public bool activeInHierarchy; public void SetActive(bool b) {} public T GetComponent<T>() => default; public T AddComponent<T>() where T : Component => default; public T GetComponentInChildren<T>() => default; public int layer; }
    public class Transform : Component, IEnumerable { public Vector3 position, localPosition, localScale; public Quaternion rotation, localRotation; public Transform parent; public int childCount; public Transform GetChild(int i) => null; public void SetParent(Transform p, bool w = true) {} public void SetAsLastSibling() {} public void SetAsFirstSibling() {} public IEnumerator GetEnumerator() => null; }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax; public Rect rect; }
    public struct Rect { public float width, height, x, y; }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero, one; public static Vector2 operator *(Vector2 a, float b) => a; }
    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x = x; this.y = y; } }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } public static Vector3 zero, one, up, forward, right; public static Vector3 operator +(Vector3 a, Vector3 b) => a; public static Vector3 operator -(Vector3 a, Vector3 b) => a; public static Vector3 operator *(Vector3 a, float b) => a; public static float Distance(Vector3 a, Vector3 b) => 0; }
    public struct Quaternion { public static Quaternion identity; public static Quaternion Euler(float x, float y, float z) => default; }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white, black, red, green, yellow, gray, grey, clear, cyan, magenta; }
    public struct Color32 { public Color32(byte r, byte g, byte b, byte a) {} public static implicit operator Color(Color32 c) => default; }
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); public static float Max(float a, float b) => Math.Max(a, b); public static int Max(params int[] v) { int m = int.MinValue; foreach (var x in v) m = Math.Max(m, x); return m; } public static int Min(int a, int b) => Math.Min(a, b); public static float Min(float a, float b) => Math.Min(a, b); public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v); public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v); public static float Clamp01(float v) => Clamp(v, 0f, 1f); public static int RoundToInt(float f) => (int)Math.Round(f, MidpointRounding.ToEven); public static int FloorToInt(float f) => (int)Math.Floor(f); public static int CeilToInt(float f) => (int)Math.Ceiling(f); public static float Abs(float f) => Math.Abs(f); public static float Sqrt(float f) => (float)Math.Sqrt(f); public static int Abs(int f) => Math.Abs(f); public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t); }
    public static class Random { public static System.Random R = new System.Random(7); public static int Range(int a, int b) => b <= a ? a : R.Next(a, b); public static float Range(float a, float b) => a + (float)R.NextDouble() * (b - a); public static float value => (float)R.NextDouble(); public static Vector3 insideUnitSphere => default; public static void InitState(int s) { R = new System.Random(s); } }
    public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} }
    public static class Time { public static float deltaTime, time, unscaledTime, unscaledDeltaTime, timeScale; }
    public static class Application { public static void Quit() {} public static string version; public static bool isEditor; }
    public static class PlayerPrefs { static readonly System.Collections.Generic.Dictionary<string, int> Store = new System.Collections.Generic.Dictionary<string, int>(); public static int GetInt(string k, int d = 0) => Store.TryGetValue(k, out int v) ? v : d; public static void SetInt(string k, int v) { Store[k] = v; } public static void Save() {} public static void DeleteAll() { Store.Clear(); } }
    public class Coroutine {}
    public class YieldInstruction {}
    public sealed class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) {} }
    public class Light : Behaviour { public float intensity; }
    public class AudioClip : Object {}
    public class AudioSource : Behaviour { public AudioClip clip; public bool loop; public float volume; public void Play() {} public void PlayOneShot(AudioClip c) {} }
    public class Sprite : Object {}
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public class Canvas : Behaviour { public static void ForceUpdateCanvases() {} public RenderMode renderMode; public int sortingOrder; public bool isRootCanvas; public Canvas rootCanvas; }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public class RectOffset { public RectOffset() {} public RectOffset(int l, int r, int t, int b) {} public int left, right, top, bottom; }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) {} }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class MinAttribute : Attribute { public MinAttribute(float a) {} }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class TextAreaAttribute : Attribute { public TextAreaAttribute() {} public TextAreaAttribute(int a, int b) {} }
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class SpaceAttribute : Attribute { public SpaceAttribute() {} public SpaceAttribute(float h) {} }
    [AttributeUsage(AttributeTargets.All)] public class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; public int order; }
    [AttributeUsage(AttributeTargets.All)] public class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.All)] public class RequireComponent : Attribute { public RequireComponent(Type t) {} }
    [AttributeUsage(AttributeTargets.All)] public class ContextMenu : Attribute { public ContextMenu(string s) {} }
}
namespace UnityEngine.Serialization { [AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public class FormerlySerializedAsAttribute : Attribute { public FormerlySerializedAsAttribute(string s) {} } }
namespace UnityEngine.Events { public class UnityEvent { public void AddListener(Action a) {} public void RemoveAllListeners() {} } }
namespace UnityEngine.EventSystems
{
    public class UIBehaviour : MonoBehaviour {}
    public class EventSystem : UIBehaviour { public static EventSystem current; public void SetSelectedGameObject(GameObject g) {} public bool IsPointerOverGameObject() => false; public bool IsPointerOverGameObject(int id) => false; }
}
namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;
    public class Graphic : UIBehaviour { public Color color; public bool raycastTarget; public RectTransform rectTransform; }
    public class MaskableGraphic : Graphic {}
    public class Image : MaskableGraphic { public Sprite sprite; }
    public struct Navigation { public enum Mode { None, Horizontal, Vertical, Automatic, Explicit } public Mode mode; }
    public class Selectable : UIBehaviour { public Navigation navigation; public bool interactable; public Graphic targetGraphic; public ColorBlock colors; }
    public struct ColorBlock { public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor; public float colorMultiplier, fadeDuration; public static ColorBlock defaultColorBlock; }
    public class Button : Selectable { public class ButtonClickedEvent : UnityEngine.Events.UnityEvent {} public ButtonClickedEvent onClick = new ButtonClickedEvent(); }
    public class Slider : Selectable { public float value, maxValue, minValue; }
    public class ScrollRect : UIBehaviour { public RectTransform content; public bool horizontal, vertical; public float verticalNormalizedPosition; public RectTransform viewport; }
    public class ContentSizeFitter : UIBehaviour { public enum FitMode { Unconstrained, MinSize, PreferredSize } public FitMode horizontalFit, verticalFit; }
    public abstract class LayoutGroup : UIBehaviour { public RectOffset padding; public TextAnchor childAlignment; }
    public class HorizontalOrVerticalLayoutGroup : LayoutGroup { public float spacing; public bool childForceExpandWidth, childForceExpandHeight, childControlWidth, childControlHeight; }
    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup {}
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup {}
    public class LayoutElement : UIBehaviour { public float minWidth, minHeight, preferredWidth, preferredHeight, flexibleWidth, flexibleHeight; }
    public class CanvasScaler : UIBehaviour { public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize } public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight; }
    public class GraphicRaycaster : UIBehaviour {}
    public static class LayoutRebuilder { public static void ForceRebuildLayoutImmediate(RectTransform r) {} }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; public string name; }
    public static class SceneManager { public static Scene GetActiveScene() => default; public static void LoadScene(int i) {} public static void LoadScene(string s) {} }
}
namespace UnityEngine.InputSystem
{
    public class InputAction { public struct CallbackContext { public bool performed, started, canceled; public InputControl control; public InputAction action; } }
    public class InputControl { public InputDevice device; public string path; }
    public class InputDevice : InputControl {}
    public class Pointer : InputDevice {}
    public class Mouse : Pointer { public static Mouse current; }
    public class Touchscreen : Pointer { public static Touchscreen current; }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center, Left, Right, Midline, TopLeft, Top, MidlineLeft }
    public enum FontStyles { Normal, Bold, Italic }
    public class TMP_Text : UnityEngine.UI.MaskableGraphic { public string text; public float fontSize; public TextAlignmentOptions alignment; public bool enableWordWrapping; public bool richText; public FontStyles fontStyle; public bool enableAutoSizing; public float fontSizeMin, fontSizeMax; }
    public class TextMeshProUGUI : TMP_Text {}
}

namespace UnityEngine.InputSystem.UI { public class InputSystemUIInputModule : UnityEngine.EventSystems.UIBehaviour {} }
