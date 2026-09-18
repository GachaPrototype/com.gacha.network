using System;
using System.Globalization;
using GachaGame.Contracts.Api;

namespace GachaGame.Network
{
    /// Một cặp khoá/giá trị đọc ra từ map JSON của server.
    ///
    /// Tồn tại vì JsonUtility không có Dictionary. Khi có Newtonsoft thì thay
    /// bằng Dictionary thật, và đây là chỗ duy nhất phải sửa.
    [Serializable]
    public struct MapEntry
    {
        public string Key;
        public string RawValue;

        public MapEntry(string key, string rawValue)
        {
            Key = key;
            RawValue = rawValue;
        }

        public int AsInt()
        {
            return int.TryParse(RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }

        public bool AsBool() => string.Equals(RawValue, "true", StringComparison.OrdinalIgnoreCase);

        public override string ToString() => $"{Key}={RawValue}";
    }

    /// Đọc map JSON phẳng thành mảng cặp khoá/giá trị.
    public static class JsonMap
    {
        public static readonly MapEntry[] Empty = new MapEntry[0];

        /// objectJson là nguyên văn một object, ví dụ {"exp_book":3,"gold_ore":1}.
        /// Rỗng hoặc không phải object thì trả mảng rỗng, không ném lỗi.
        public static MapEntry[] Parse(string objectJson)
        {
            if (!JsonProbe.TryGetEntries(objectJson, out string[] keys, out string[] values))
            {
                return Empty;
            }

            var entries = new MapEntry[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                entries[i] = new MapEntry(keys[i], values[i]);
            }
            return entries;
        }

        /// Lấy map nằm ở một trường của response, ví dụ Field(body, "inventory").
        public static MapEntry[] Field(string json, string fieldName)
        {
            return Parse(JsonProbe.GetRaw(json, fieldName));
        }

        public static int GetInt(MapEntry[] entries, string key)
        {
            if (entries == null) return 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Key == key) return entries[i].AsInt();
            }
            return 0;
        }

        public static bool GetBool(MapEntry[] entries, string key)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Key == key) return entries[i].AsBool();
            }
            return false;
        }
    }
}
