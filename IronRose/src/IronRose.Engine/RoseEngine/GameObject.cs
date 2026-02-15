using System;
using System.Collections.Generic;

namespace RoseEngine
{
    public class GameObject : Object
    {
        public Transform transform { get; }
        public string tag { get; set; } = "Untagged";
        public int layer { get; set; } = 0;

        internal readonly List<Component> _components = new();

        internal IReadOnlyList<Component> InternalComponents => _components;

        private bool _activeSelf = true;

        public bool activeSelf => _activeSelf;

        public bool activeInHierarchy
        {
            get
            {
                if (!_activeSelf) return false;
                if (transform.parent == null) return true;
                return transform.parent.gameObject.activeInHierarchy;
            }
        }

        public GameObject(string name = "GameObject")
        {
            this.name = name;

            // Bootstrap Transform with self-reference
            var t = new Transform();
            t.gameObject = this;
            transform = t;
            _components.Add(t);

            // Register in scene
            SceneManager.RegisterGameObject(this);
        }

        public void SetActive(bool value)
        {
            if (_activeSelf == value) return;
            _activeSelf = value;

            // Notify MonoBehaviours
            foreach (var comp in _components)
            {
                if (comp is MonoBehaviour mb && mb.enabled && mb._hasAwoken)
                {
                    try
                    {
                        if (value) mb.OnEnable();
                        else mb.OnDisable();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Exception in {(value ? "OnEnable" : "OnDisable")}() of {mb.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        public bool CompareTag(string tag) => this.tag == tag;

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            _components.Add(component);
            component.OnAddedToGameObject();

            // Auto-register MonoBehaviours
            if (component is MonoBehaviour mb)
                SceneManager.RegisterBehaviour(mb);

            return component;
        }

        public Component AddComponent(Type type)
        {
            if (!typeof(Component).IsAssignableFrom(type))
                throw new ArgumentException($"{type.Name} does not derive from Component");

            var component = (Component)Activator.CreateInstance(type)!;
            component.gameObject = this;
            _components.Add(component);
            component.OnAddedToGameObject();

            // Auto-register MonoBehaviours
            if (component is MonoBehaviour mb)
                SceneManager.RegisterBehaviour(mb);

            return component;
        }

        internal void RemoveComponent(Component component)
        {
            _components.Remove(component);
        }

        public T? GetComponent<T>() where T : Component
        {
            foreach (var c in _components)
            {
                if (c is T typed && !c._isDestroyed) return typed;
            }
            return null;
        }

        public Component? GetComponent(Type type)
        {
            foreach (var c in _components)
            {
                if (type.IsInstanceOfType(c) && !c._isDestroyed) return c;
            }
            return null;
        }

        public T[] GetComponents<T>() where T : Component
        {
            var results = new List<T>();
            foreach (var c in _components)
            {
                if (c is T typed && !c._isDestroyed) results.Add(typed);
            }
            return results.ToArray();
        }

        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            var go = new GameObject(type.ToString());
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.material = new Material();

            filter.mesh = type switch
            {
                PrimitiveType.Cube => PrimitiveGenerator.CreateCube(),
                PrimitiveType.Sphere => PrimitiveGenerator.CreateSphere(),
                PrimitiveType.Capsule => PrimitiveGenerator.CreateCapsule(),
                PrimitiveType.Plane => PrimitiveGenerator.CreatePlane(),
                PrimitiveType.Quad => PrimitiveGenerator.CreateQuad(),
                _ => PrimitiveGenerator.CreateCube(),
            };

            return go;
        }

        // --- Static Find methods ---

        public static GameObject? Find(string name)
        {
            foreach (var go in SceneManager.AllGameObjects)
            {
                if (!go._isDestroyed && go.name == name)
                    return go;
            }
            return null;
        }

        public static GameObject? FindWithTag(string tag)
        {
            foreach (var go in SceneManager.AllGameObjects)
            {
                if (!go._isDestroyed && go.activeInHierarchy && go.tag == tag)
                    return go;
            }
            return null;
        }

        public static GameObject[] FindGameObjectsWithTag(string tag)
        {
            var results = new List<GameObject>();
            foreach (var go in SceneManager.AllGameObjects)
            {
                if (!go._isDestroyed && go.activeInHierarchy && go.tag == tag)
                    results.Add(go);
            }
            return results.ToArray();
        }

        public override string ToString() => $"GameObject({name})";
    }
}
