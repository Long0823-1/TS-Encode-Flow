using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TsEncodeFlow {
 public sealed class JsonCodec {
  static JsonSerializerOptions Options(){var o=new JsonSerializerOptions {IncludeFields=true,PropertyNameCaseInsensitive=true,WriteIndented=true};o.Converters.Add(new Dates());return o;}
  public string Serialize(object value)=>JsonSerializer.Serialize(value,Options());
  public T Deserialize<T>(string text) {
   if(typeof(T)==typeof(Dictionary<string,object>)){using(var d=JsonDocument.Parse(text))return (T)ConvertNode(d.RootElement);}
   return JsonSerializer.Deserialize<T>(text,Options());
  }
  static object ConvertNode(JsonElement e)=>e.ValueKind switch {
   JsonValueKind.Object=>e.EnumerateObject().ToDictionary(p=>p.Name,p=>ConvertNode(p.Value)),
   JsonValueKind.Array=>e.EnumerateArray().Select(ConvertNode).ToList(),
   JsonValueKind.String=>e.GetString(),JsonValueKind.Number=>e.TryGetInt64(out long n)?(object)n:e.GetDouble(),
   JsonValueKind.True=>true,JsonValueKind.False=>false,_=>null
  };
  sealed class Dates:JsonConverter<DateTime> {
   public override DateTime Read(ref Utf8JsonReader r,Type type,JsonSerializerOptions options) {
    string s=r.GetString();if(s.StartsWith("/Date(",StringComparison.Ordinal)&&s.EndsWith(")/",StringComparison.Ordinal)){long ms=long.Parse(s.Substring(6,s.Length-8));return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;}return DateTime.Parse(s,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind);
   }
   public override void Write(Utf8JsonWriter w,DateTime value,JsonSerializerOptions options)=>w.WriteStringValue(value);
  }
 }
}
