namespace Cthangover.Tools.Map2d
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Map2d;
    using Cthangover.Tools.EntityEditor;

    /// <summary>
    /// Map forms edit the original JSON through EntityDocument, retaining unknown fields and keyed foreign patches.
    /// Each title, layer or point operation is one history step; native imagery is owned separately by the editor.
    /// </summary>
    public sealed class Map2dDocument
    {
        /// <summary>Borrowed file/patch document used by the window's validated save and history controls.</summary>
        public EntityDocument Data { get; }

        /// <summary>Wraps only the terrain-map kind so a form cannot write map fields into another resource kind.</summary>
        public Map2dDocument(EntityDocument data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Kind != Map2dDefinition.ResourceKind)
            {
                throw new ArgumentException("Terrain-map forms require a map2d document", nameof(data));
            }

            Data = data;
        }

        /// <summary>Returns a detached recipe; an unconfigured map gets unsaved cell dimensions with no invented image.</summary>
        public Map2dDefinition Read(string map, int width, int height)
        {
            var record = Data.FindAny(map);
            if (record?.Data == null)
            {
                return new Map2dDefinition
                {
                    ID = map,
                    Width = width,
                    Height = height
                };
            }

            return JsonSerializer.Deserialize<Map2dDefinition>(record.Data.ToJsonString(), JsonOptionsProvider.ReadOptions);
        }

        /// <summary>Changes both translations together, preserving the layer, marker metadata and unrelated provider fields.</summary>
        public bool Titles(string map, int width, int height, string russian, string english)
        {
            ValidateTitle(russian);
            ValidateTitle(english);
            var before = Read(map, width, height);
            if (before.TitleRu == russian && before.TitleEn == english)
            {
                return false;
            }

            var record = Begin(map, width, height);
            Put(record.Data, "TitleRu", JsonValue.Create(russian));
            Put(record.Data, "TitleEn", JsonValue.Create(english));
            Commit(record);
            return true;
        }

        /// <summary>
        /// Sets a prepared image and its grid metadata atomically. Existing points remain editable when regenerating
        /// a resized map; full point validation still precedes Save.
        /// </summary>
        public bool Layer(string map, int width, int height, string image, int pixelsX, int pixelsY)
        {
            new Map2dDefinition
            {
                ID = map,
                Width = width,
                Height = height,
                Image = image,
                PixelsX = pixelsX,
                PixelsY = pixelsY
            }.Validate();
            var before = Read(map, width, height);
            if (before.Image == image && before.Width == width && before.Height == height
                && before.PixelsX == pixelsX && before.PixelsY == pixelsY)
            {
                return false;
            }

            var record = Begin(map, width, height);
            Put(record.Data, "Image", JsonValue.Create(image));
            Put(record.Data, "Width", JsonValue.Create(width));
            Put(record.Data, "Height", JsonValue.Create(height));
            Put(record.Data, "PixelsX", JsonValue.Create(pixelsX));
            Put(record.Data, "PixelsY", JsonValue.Create(pixelsY));
            Commit(record);
            return true;
        }

        /// <summary>Reserves a fresh local point key before insertion; no native drawing or exploration state is touched.</summary>
        public string AddPoint(string map, int width, int height)
        {
            ExplorationRecord.ValidateDimensions(width, height);
            var before = Read(map, width, height);
            if (before.Markers == null || before.Markers.Count >= 256)
            {
                throw new ArgumentException("Map marker budget is exhausted");
            }

            var number = 1;
            while (FindPoint(before, "point_" + number) != null)
            {
                number++;
            }

            var id = "point_" + number;
            var record = Begin(map, width, height);
            var list = EntityJson.Get(record.Data, "Markers", out _) as JsonArray;
            if (list == null)
            {
                list = new JsonArray();
                Put(record.Data, "Markers", list);
            }

            list.Add(JsonSerializer.SerializeToNode(new Map2dMarker
            {
                Id = id,
                X = 0.5f,
                Y = 0.5f
            }));
            Commit(record);
            return id;
        }

        /// <summary>Changes a selected point in one step, retaining its stable key and unknown nested metadata.</summary>
        public bool Point(string map, int width, int height, string id, string russian, string english,
            float x, float y, bool always)
        {
            ValidateTitle(russian);
            ValidateTitle(english);
            var bounds = new Map2dDefinition
            {
                Width = width,
                Height = height
            };
            if (!bounds.Contains(x, y))
            {
                throw new ArgumentException("Point lies outside its RPG map");
            }

            var before = FindPoint(Read(map, width, height), id);
            if (before == null || before.TitleRu == russian && before.TitleEn == english
                && before.X == x && before.Y == y && before.AlwaysVisible == always)
            {
                return false;
            }

            var record = Begin(map, width, height);
            foreach (var node in EntityJson.Get(record.Data, "Markers", out _) as JsonArray ?? new JsonArray())
            {
                if (node is JsonObject marker && Same(EntityJson.Get(marker, "Id", out _)?.ToString(), id))
                {
                    Put(marker, "TitleRu", JsonValue.Create(russian));
                    Put(marker, "TitleEn", JsonValue.Create(english));
                    Put(marker, "X", JsonValue.Create(x));
                    Put(marker, "Y", JsonValue.Create(y));
                    Put(marker, "AlwaysVisible", JsonValue.Create(always));
                    break;
                }
            }

            Commit(record);
            return true;
        }

        /// <summary>Removes one selected point; undo restores both known fields and provider-owned metadata.</summary>
        public bool RemovePoint(string map, string id)
        {
            var record = Data.FindAny(map);
            var list = EntityJson.Get(record?.Data, "Markers", out _) as JsonArray;
            if (list == null)
            {
                return false;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is JsonObject point && Same(EntityJson.Get(point, "Id", out _)?.ToString(), id))
                {
                    Data.BeginChange();
                    list.RemoveAt(i);
                    Commit(record);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Deletes own settings or writes an inherited deletion patch; it never deletes prepared art files.</summary>
        public bool Remove(string map)
        {
            var record = Data.FindAny(map);
            if (record == null || Data.IsDeleted(record))
            {
                return false;
            }

            Data.BeginChange();
            if (record.IsForeign)
            {
                Data.DeleteForeign(record);
            }
            else
            {
                Data.Remove(record);
            }

            return true;
        }

        private EntityRecord Begin(string map, int width, int height)
        {
            ExplorationRecord.ValidateDimensions(width, height);
            ExplorationState.ValidateIdentity("rpg/" + map);
            Data.BeginChange();
            var record = Data.FindAny(map);
            if (record == null)
            {
                record = Data.Add(JsonSerializer.SerializeToNode(new Map2dDefinition
                {
                    ID = map,
                    Width = width,
                    Height = height
                }).AsObject());
            }
            else if (Data.IsDeleted(record))
            {
                Data.ResetForeign(record);
            }

            return record;
        }

        private void Commit(EntityRecord record)
        {
            if (record.IsForeign)
            {
                Data.CommitForeign(record);
            }
        }

        private static Map2dMarker FindPoint(Map2dDefinition definition, string id)
        {
            foreach (var marker in definition.Markers ?? new())
            {
                if (Same(marker.Id, id))
                {
                    return marker;
                }
            }

            return null;
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static void Put(JsonObject data, string key, JsonNode value)
        {
            EntityJson.Get(data, key, out var spelling);
            data[spelling ?? key] = value;
        }

        private static void ValidateTitle(string value)
        {
            if (value == null || value.Length > 128)
            {
                throw new ArgumentException("Map titles must contain at most 128 characters");
            }
        }
    }
}
