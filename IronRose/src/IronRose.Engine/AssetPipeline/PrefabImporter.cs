using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace IronRose.AssetPipeline
{
    public class PrefabImporter
    {
        private readonly UnityYamlParser _yamlParser;
        private readonly AssetDatabase _assetDatabase;

        public PrefabImporter(AssetDatabase assetDatabase)
        {
            _yamlParser = new UnityYamlParser();
            _assetDatabase = assetDatabase;
        }

        public GameObject LoadPrefab(string prefabPath)
        {
            if (!File.Exists(prefabPath))
            {
                Debug.LogError($"Prefab not found: {prefabPath}");
                return null!;
            }

            string yamlContent = File.ReadAllText(prefabPath);

            // Unity YAML은 여러 문서로 구성됨 (--- 구분자)
            var documents = yamlContent.Split(["---"], StringSplitOptions.RemoveEmptyEntries);

            GameObject rootObject = null!;

            foreach (var doc in documents)
            {
                var trimmed = doc.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("%"))
                    continue;

                if (trimmed.Contains("GameObject:"))
                {
                    rootObject = ParseGameObject(trimmed);
                }
                else if (trimmed.Contains("Transform:") && rootObject != null)
                {
                    var transformData = ParseTransform(trimmed);
                    if (transformData != null)
                    {
                        rootObject.transform.position = transformData.position;
                        rootObject.transform.rotation = transformData.rotation;
                        rootObject.transform.localScale = transformData.localScale;
                    }
                }
            }

            if (rootObject == null)
            {
                Debug.LogWarning($"Prefab has no GameObject: {prefabPath}");
                return new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            }

            Debug.Log($"Imported prefab: {prefabPath} -> {rootObject.name}");
            return rootObject;
        }

        private static GameObject ParseGameObject(string yaml)
        {
            string name = "Prefab";

            // m_Name: "이름" 패턴 추출
            var nameMatch = Regex.Match(yaml, @"m_Name:\s*(.+)");
            if (nameMatch.Success)
            {
                name = nameMatch.Groups[1].Value.Trim();
            }

            return new GameObject(name);
        }

        private static TransformData? ParseTransform(string yaml)
        {
            var data = new TransformData();

            // m_LocalPosition: {x: 0, y: 0, z: 0}
            var posMatch = Regex.Match(yaml,
                @"m_LocalPosition:\s*\{x:\s*([-\d.e]+),\s*y:\s*([-\d.e]+),\s*z:\s*([-\d.e]+)\}");
            if (posMatch.Success)
            {
                data.position = new Vector3(
                    float.Parse(posMatch.Groups[1].Value),
                    float.Parse(posMatch.Groups[2].Value),
                    float.Parse(posMatch.Groups[3].Value));
            }

            // m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
            var rotMatch = Regex.Match(yaml,
                @"m_LocalRotation:\s*\{x:\s*([-\d.e]+),\s*y:\s*([-\d.e]+),\s*z:\s*([-\d.e]+),\s*w:\s*([-\d.e]+)\}");
            if (rotMatch.Success)
            {
                data.rotation = new Quaternion(
                    float.Parse(rotMatch.Groups[1].Value),
                    float.Parse(rotMatch.Groups[2].Value),
                    float.Parse(rotMatch.Groups[3].Value),
                    float.Parse(rotMatch.Groups[4].Value));
            }

            // m_LocalScale: {x: 1, y: 1, z: 1}
            var scaleMatch = Regex.Match(yaml,
                @"m_LocalScale:\s*\{x:\s*([-\d.e]+),\s*y:\s*([-\d.e]+),\s*z:\s*([-\d.e]+)\}");
            if (scaleMatch.Success)
            {
                data.localScale = new Vector3(
                    float.Parse(scaleMatch.Groups[1].Value),
                    float.Parse(scaleMatch.Groups[2].Value),
                    float.Parse(scaleMatch.Groups[3].Value));
            }

            return data;
        }
    }

    internal class TransformData
    {
        public Vector3 position = Vector3.zero;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
    }
}
