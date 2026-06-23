using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text;

namespace ZiZiBOOKS
{
    public class AppSettings
    {
        // 初期値をdouble.NaNにしておき、読込み時に判定しやすくする
        public double Top { get; set; } = double.NaN;
        public double Left { get; set; } = double.NaN;
        public bool IsMajiMode { get; set; } = false;
        public bool IsTopmost { get; set; } = false;
        public bool IsHoverTopmost { get; set; } = true; // マウスホバー時のみ最前面・便利なので標準ONがいいが、2k場合は邪魔になるので、制御を入れてOFF状態になる
        public int FontSize { get; set; } = 16;
        public int Padding { get; set; } = 10;
        public int IdleSeconds { get; set; } = 5;      // 放置秒数
        public double IdleOpacity { get; set; } = 0.02; // 放置時の不透明度
    }

    public class BookmarkDict
    {
        public List<BookmarkItem> Items { get; set; } = new List<BookmarkItem>();
    }

    public class BookmarkItem
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Memo { get; set; } = "";
        public string IconPath { get; set; } = string.Empty;
    }

    public static class ConfigManager
    {
        private static readonly string JsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZiZiBOOKS.json");
        private static readonly string DictPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZiZiBOOKS.dict");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true
        };

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(JsonPath))
                {
                    string json = File.ReadAllText(JsonPath, Encoding.UTF8);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }
        
        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                // [MOD] SaveSettings メソッドの内部をアトミック書き込みに変更
                //File.WriteAllText(JsonPath, json, new UTF8Encoding(false));
                AtomicWrite(JsonPath, json);
            }
            catch { }
        }

        public static void SaveDictToPath(BookmarkDict dict, string path)
        {
            try
            {
                string json = JsonSerializer.Serialize(dict, JsonOptions);
                // [MOD] SaveDictToPath メソッドの内部をアトミック書き込みに変更
                //File.WriteAllText(path, json, new UTF8Encoding(false));
                AtomicWrite(path, json);
            }
            catch { }
        }

        public static BookmarkDict LoadDict()
        {
            return LoadDictFromPath(DictPath) ?? GetDefaultDict();
        }

        public static BookmarkDict? LoadDictFromPath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    return JsonSerializer.Deserialize<BookmarkDict>(json);
                }
            }
            catch { }
            return null;
        }

        public static void SaveDict(BookmarkDict dict)
        {
            try
            {
                string json = JsonSerializer.Serialize(dict, JsonOptions);
                // [MOD] SaveDict メソッドの内部をアトミック書き込みに変更
                //File.WriteAllText(DictPath, json, new UTF8Encoding(false));
                AtomicWrite(DictPath, json);
            }
            catch { }
        }

        // [ADD] ここから：安全な書き込み（一時ファイル経由）を行う共通メソッドを追加
        /// <summary>
        /// 一時ファイルを使用して安全に書き込みを行う（アトミックな書き込み）
        /// </summary>
        private static void AtomicWrite(string path, string content)
        {
            string tempPath = path + ".tmp";
            // UTF-8 BOM無しで書き込み
            File.WriteAllText(tempPath, content, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tempPath, path);
        }

        private static BookmarkDict GetDefaultDict()
        {
            return new BookmarkDict
            {
                Items = new List<BookmarkItem>
                {
                    new BookmarkItem { Name = "Google 検索", Url = "https://www.google.co.jp/", Memo = "Default" },
                    new BookmarkItem { Name = "Google Drive", Url = "https://drive.google.com/", Memo = "Default" }
                }
            };
        }
    }
}