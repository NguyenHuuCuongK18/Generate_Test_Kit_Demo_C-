using System.Text;
using System.Text.Json;
using System.Xml;

namespace UITestKit.Service
{
    public class DataInspector
    {
        public static string DetecDataType(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "Empty";
            // 1. Thử parse số nguyên
            string asString = Encoding.UTF8.GetString(data).Trim();
            if (long.TryParse(asString, out _))
            {
                return "Integer";
            }
            if (asString.StartsWith("{") && asString.EndsWith("}"))
            {
                try
                {
                    JsonDocument.Parse(asString);
                    return "JSON";
                }
                catch { /* không parse được thì bỏ qua */ }
            }

            // 3. Thử parse XML
            if (asString.StartsWith("<") && asString.EndsWith(">"))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(asString);
                    return "XML";
                }
                catch { /* không parse được thì bỏ qua */ }
            }

            bool allPrintable = true;
            foreach (byte b in data)
            {
                if (b < 32 && b != 9 && b != 10 && b != 13) // bỏ qua tab, LF, CR
                {
                    allPrintable = false;
                    break;
                }
            }
            if (allPrintable) return "String";

            // 5. Mặc định coi là Binary
            return "Binary";
        }
    }

}
