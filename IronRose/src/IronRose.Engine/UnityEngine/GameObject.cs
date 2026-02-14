using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class GameObject
    {
        public string name { get; set; }
        public Transform transform { get; }

        private readonly List<Component> _components = new();

        public GameObject(string name = "GameObject")
        {
            this.name = name;

            // Bootstrap Transform with self-reference
            var t = new Transform();
            t.gameObject = this;
            transform = t;
            _components.Add(t);
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public Component AddComponent(Type type)
        {
            if (!typeof(Component).IsAssignableFrom(type))
                throw new ArgumentException($"{type.Name} does not derive from Component");

            var component = (Component)Activator.CreateInstance(type)!;
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public T? GetComponent<T>() where T : Component
        {
            foreach (var c in _components)
            {
                if (c is T typed) return typed;
            }
            return null;
        }

        public Component? GetComponent(Type type)
        {
            foreach (var c in _components)
            {
                if (type.IsInstanceOfType(c)) return c;
            }
            return null;
        }

        public override string ToString() => $"GameObject({name})";
    }
}
