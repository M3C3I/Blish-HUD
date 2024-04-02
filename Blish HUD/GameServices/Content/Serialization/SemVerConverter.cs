using System;
using Newtonsoft.Json;
using Version = SemanticVersioning.Version;

namespace Blish_HUD.Content.Serialization {
    public class SemVerConverter : JsonConverter<SemanticVersioning.Version> {

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, Version value, JsonSerializer serializer) {
            writer.WriteValue(value.ToString());
        }

        /// <inheritdoc />
        public override Version ReadJson(JsonReader reader, Type objectType, Version existingValue, bool hasExistingValue, JsonSerializer serializer) {
             return new SemanticVersioning.Version((string)reader.Value, true);
        }

    }
}
