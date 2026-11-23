using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Pulls QML/JS sources out of a Qt executable's embedded resources (plain or zlib-compressed rcc entries)
// and lists every qsTr()/qsTranslate() source string.
//   ExtractQml <exe> <outDir>   -> outDir\qml_NNN.txt, outDir\strings.txt (one string per line, JSON-escaped)
static class ExtractQml
{
    static readonly Regex Tr = new Regex(@"qsTr\(\s*(""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')|qsTranslate\(\s*(?:""[^""]*""|'[^']*')\s*,\s*(""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')", RegexOptions.Compiled);

    static int Main(string[] args)
    {
        byte[] exe = File.ReadAllBytes(args[0]);
        Directory.CreateDirectory(args[1]);
        var texts = new List<string>();
        var seenHashes = new HashSet<int>();
        int zlibOk = 0, plain = 0, zstd = 0;

        for (int p = 8; p < exe.Length - 2; p++)
        {
            byte b0 = exe[p], b1 = exe[p + 1];
            if (b0 == 0x28 && b1 == 0xB5 && p + 3 < exe.Length && exe[p + 2] == 0x2F && exe[p + 3] == 0xFD) zstd++;
            if (b0 != 0x78 || (b1 != 0x9C && b1 != 0xDA && b1 != 0x01 && b1 != 0x5E)) continue;
            uint size = (uint)(exe[p - 4] << 24 | exe[p - 3] << 16 | exe[p - 2] << 8 | exe[p - 1]);
            if (size < 16 || size > 8 * 1024 * 1024) continue;
            try
            {
                using (var ms = new MemoryStream(exe, p + 2, exe.Length - p - 2))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    var buf = new byte[size];
                    int read = 0, r;
                    while (read < buf.Length && (r = ds.Read(buf, read, buf.Length - read)) > 0) read += r;
                    if (read != size) continue;
                    string s = Encoding.UTF8.GetString(buf);
                    if (s.IndexOf("qsTr", StringComparison.Ordinal) < 0 && s.IndexOf("import Qt", StringComparison.Ordinal) < 0) continue;
                    if (seenHashes.Add(s.GetHashCode())) { texts.Add(s); zlibOk++; }
                }
            }
            catch { }
        }

        // Uncompressed QML: rcc stores a 4-byte big-endian length right before each file's data.
        string latin = Encoding.GetEncoding(28591).GetString(exe);
        foreach (Match m in Regex.Matches(latin, @"import QtQuick"))
        {
            int start = m.Index;
            // Walk back to the file start (comments / pragmas may precede the first import).
            for (int back = 0; back < 4096 && start > 4; back++, start--)
            {
                uint len = (uint)(exe[start - 4] << 24 | exe[start - 3] << 16 | exe[start - 2] << 8 | exe[start - 1]);
                if (len > (uint)(m.Index - start) && len < 2 * 1024 * 1024 && start + len <= exe.Length)
                {
                    string s = Encoding.UTF8.GetString(exe, start, (int)len);
                    if (seenHashes.Add(s.GetHashCode())) { texts.Add(s); plain++; }
                    break;
                }
            }
        }

        var strings = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < texts.Count; i++)
        {
            File.WriteAllText(Path.Combine(args[1], "qml_" + i.ToString("D3") + ".txt"), texts[i], new UTF8Encoding(false));
            foreach (Match m in Tr.Matches(texts[i]))
            {
                string lit = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                strings.Add(Unescape(lit.Substring(1, lit.Length - 2)));
            }
        }
        File.WriteAllLines(Path.Combine(args[1], "strings.txt"), strings.Select(Escape), new UTF8Encoding(false));
        Console.WriteLine("qml/js files: " + texts.Count + " (zlib " + zlibOk + ", plain " + plain + "), zstd frames seen: " + zstd + ", translatable strings: " + strings.Count);
        return 0;
    }

    static string Unescape(string s)
    {
        return Regex.Replace(s, @"\\(u[0-9a-fA-F]{4}|.)", m =>
        {
            string e = m.Groups[1].Value;
            if (e[0] == 'u' && e.Length == 5) return ((char)Convert.ToInt32(e.Substring(1), 16)).ToString();
            switch (e) { case "n": return "\n"; case "t": return "\t"; case "r": return "\r"; default: return e; }
        });
    }

    static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t"); }
}
