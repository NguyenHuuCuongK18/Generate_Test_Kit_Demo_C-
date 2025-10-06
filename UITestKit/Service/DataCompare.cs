using System.Text.Json;
using System.Xml.Linq;

namespace UITestKit.Service
{
    public class DataCompare
    {
        public static bool CompareJson(string json1, string json2)
        {
            if (json1 == null || json2 == null) return false;
            try
            {
                var dict1 = JsonSerializer.Deserialize<Dictionary<string, object>>(json1,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var dict2 = JsonSerializer.Deserialize<Dictionary<string, object>>(json2,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return CompareDictionaries(dict1, dict2);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR JSON] " + ex.Message);
                return false;
            }
        }

        // So sánh XML với XML
        public static bool CompareXml(string xml1, string xml2)
        {
            if (xml1 == null || xml2 == null) { return false; }
            try
            {
                var dict1 = XmlToDictionary(XDocument.Parse(xml1).Root);
                var dict2 = XmlToDictionary(XDocument.Parse(xml2).Root);

                return CompareDictionaries(dict1, dict2);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR XML] " + ex.Message);
                return false;
            }
        }

        // Chuyển XML -> Dictionary
        private static Dictionary<string, object> XmlToDictionary(XElement element)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var child in element.Elements())
            {
                if (!child.HasElements)
                    dict[child.Name.LocalName] = child.Value.Trim();
                else
                    dict[child.Name.LocalName] = XmlToDictionary(child);
            }

            return dict;
        }

        // So sánh dictionary
        private static bool CompareDictionaries(Dictionary<string, object> d1, Dictionary<string, object> d2)
        {
            if (d1.Count != d2.Count) return false;

            foreach (var kv in d1)
            {
                if (!d2.ContainsKey(kv.Key)) return false;

                var v1 = kv.Value?.ToString()?.Trim();
                var v2 = d2[kv.Key]?.ToString()?.Trim();

                if (!string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }
    }

}
