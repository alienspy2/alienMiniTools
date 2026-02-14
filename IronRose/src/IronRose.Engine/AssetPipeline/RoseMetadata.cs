using Tomlyn;
using Tomlyn.Model;
using System;
using System.IO;

namespace IronRose.AssetPipeline
{
    public class RoseMetadata
    {
        public string guid { get; set; } = Guid.NewGuid().ToString();
        public int version { get; set; } = 1;
        public string[]? labels { get; set; }
        public TomlTable importer { get; set; } = new();

        public static RoseMetadata LoadOrCreate(string assetPath)
        {
            var rosePath = assetPath + ".rose";

            if (File.Exists(rosePath))
            {
                var toml = Toml.ToModel(File.ReadAllText(rosePath));
                return FromToml(toml);
            }

            var meta = new RoseMetadata();
            meta.importer = InferImporter(assetPath);
            meta.Save(rosePath);
            return meta;
        }

        public void Save(string rosePath)
        {
            var toml = ToToml();
            File.WriteAllText(rosePath, Toml.FromModel(toml));
        }

        private static TomlTable InferImporter(string assetPath)
        {
            var ext = Path.GetExtension(assetPath).ToLowerInvariant();
            return ext switch
            {
                ".glb" or ".gltf" or ".fbx" or ".obj" => new TomlTable
                {
                    ["type"] = "MeshImporter",
                    ["scale"] = 1.0,
                    ["generate_normals"] = true,
                    ["flip_uvs"] = true,
                    ["triangulate"] = true,
                },
                ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" => new TomlTable
                {
                    ["type"] = "TextureImporter",
                    ["max_size"] = (long)2048,
                    ["compression"] = "none",
                    ["srgb"] = true,
                    ["filter_mode"] = "Bilinear",
                    ["wrap_mode"] = "Repeat",
                    ["generate_mipmaps"] = true,
                },
                ".prefab" => new TomlTable
                {
                    ["type"] = "PrefabImporter",
                },
                _ => new TomlTable { ["type"] = "DefaultImporter" },
            };
        }

        private TomlTable ToToml()
        {
            var table = new TomlTable
            {
                ["guid"] = guid,
                ["version"] = (long)version,
            };

            if (labels != null && labels.Length > 0)
            {
                var arr = new TomlArray();
                foreach (var label in labels)
                    arr.Add(label);
                table["labels"] = arr;
            }

            if (importer.Count > 0)
            {
                table["importer"] = importer;
            }

            return table;
        }

        private static RoseMetadata FromToml(TomlTable table)
        {
            var meta = new RoseMetadata();

            if (table.TryGetValue("guid", out var guidVal))
                meta.guid = guidVal?.ToString() ?? meta.guid;

            if (table.TryGetValue("version", out var verVal) && verVal is long v)
                meta.version = (int)v;

            if (table.TryGetValue("labels", out var labelsVal) && labelsVal is TomlArray labelsArr)
            {
                meta.labels = labelsArr
                    .Where(x => x != null)
                    .Select(x => x!.ToString()!)
                    .ToArray();
            }

            if (table.TryGetValue("importer", out var impVal) && impVal is TomlTable impTable)
                meta.importer = impTable;

            return meta;
        }
    }
}
