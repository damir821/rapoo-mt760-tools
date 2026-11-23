using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// Reads and writes Qt .qm translation files.
//   QmTool dump  <file.qm> <out.json>          -> all messages (context, source, comment, translations)
//   QmTool build <in.json> <template.qm> <out.qm> -> .qm with the translations from in.json;
//                                                  language and plural rules are copied from template.qm
//   QmTool check <file.qm> <in.json>           -> re-reads a built file and verifies every message by hash lookup
static class QmTool
{
    static readonly byte[] Magic = { 0x3C, 0xB8, 0x64, 0x18, 0xCA, 0xEF, 0x9C, 0x95, 0xCD, 0x21, 0x1C, 0xBF, 0x60, 0xA1, 0xBD, 0xDD };
    const byte SecContexts = 0x2F, SecHashes = 0x42, SecMessages = 0x69, SecNumerus = 0x88, SecDependencies = 0x96, SecLanguage = 0xA7;
    const byte TagEnd = 1, TagTranslation = 3, TagSourceText16 = 4, TagContext16 = 5, TagObsolete1 = 2,
               TagSourceText = 6, TagContext = 7, TagComment = 8, TagObsolete2 = 9;

    public class Message
    {
        public string context = "";
        public string source = "";
        public string comment = "";
        public List<string> translations = new List<string>();
    }

    class QmFile
    {
        public string language = "";
        public byte[] numerusRules;
        public List<Message> messages = new List<Message>();
    }

    static int Main(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "dump": Dump(args[1], args[2]); return 0;
                case "build": Build(args[1], args[2], args[3]); return 0;
                case "check": return Check(args[1], args[2]);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
        Console.Error.WriteLine("usage: dump|build|check");
        return 1;
    }

    static uint ReadU32(byte[] b, int p) { return (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); }

    static QmFile Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        for (int i = 0; i < 16; i++) if (b[i] != Magic[i]) throw new InvalidDataException("not a .qm file: " + path);
        var qm = new QmFile();
        int pos = 16, msgStart = -1, msgLen = 0;
        while (pos < b.Length)
        {
            byte tag = b[pos];
            int len = (int)ReadU32(b, pos + 1);
            int data = pos + 5;
            if (tag == SecMessages) { msgStart = data; msgLen = len; }
            else if (tag == SecLanguage) qm.language = Encoding.UTF8.GetString(b, data, len);
            else if (tag == SecNumerus) { qm.numerusRules = new byte[len]; Array.Copy(b, data, qm.numerusRules, 0, len); }
            pos = data + len;
        }
        if (msgStart < 0) return qm;
        int p = msgStart, end = msgStart + msgLen;
        var m = new Message();
        while (p < end)
        {
            byte tag = b[p++];
            switch (tag)
            {
                case TagEnd:
                    qm.messages.Add(m);
                    m = new Message();
                    break;
                case TagTranslation:
                {
                    uint len = ReadU32(b, p); p += 4;
                    if (len == 0xFFFFFFFF) { m.translations.Add(null); break; }
                    m.translations.Add(Encoding.BigEndianUnicode.GetString(b, p, (int)len));
                    p += (int)len;
                    break;
                }
                case TagSourceText:
                case TagContext:
                case TagComment:
                {
                    int len = (int)ReadU32(b, p); p += 4;
                    string s = Encoding.UTF8.GetString(b, p, len);
                    p += len;
                    if (tag == TagSourceText) m.source = s;
                    else if (tag == TagContext) m.context = s;
                    else m.comment = s;
                    break;
                }
                case TagObsolete1:
                    p += 4;
                    break;
                case TagSourceText16:
                case TagContext16:
                case TagObsolete2:
                {
                    int len = (int)ReadU32(b, p); p += 4 + len;
                    break;
                }
                default:
                    throw new InvalidDataException("unknown message tag 0x" + tag.ToString("X2") + " at " + (p - 1));
            }
        }
        return qm;
    }

    static void Dump(string qmPath, string jsonPath)
    {
        QmFile qm = Read(qmPath);
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        File.WriteAllText(jsonPath, json.Serialize(new { language = qm.language, messages = qm.messages }), new UTF8Encoding(false));
        Console.WriteLine(qmPath + ": language=" + qm.language + " messages=" + qm.messages.Count +
            " withSource=" + qm.messages.Count(x => x.source.Length > 0) +
            " plural=" + qm.messages.Count(x => x.translations.Count > 1) +
            " numerusRules=" + (qm.numerusRules == null ? 0 : qm.numerusRules.Length) + "B");
    }

    // Qt's elfHash over UTF-8 of sourceText followed by comment.
    static uint ElfHash(string source, string comment)
    {
        uint h = 0;
        foreach (byte c in Encoding.UTF8.GetBytes(source + comment))
        {
            h = (h << 4) + c;
            uint g = h & 0xF0000000;
            if (g != 0) h ^= g >> 24;
            h &= ~g;
        }
        return h == 0 ? 1 : h;
    }

    static void WriteU32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    static void WriteString(Stream s, byte tag, byte[] bytes) { s.WriteByte(tag); WriteU32(s, (uint)bytes.Length); s.Write(bytes, 0, bytes.Length); }

    static List<Message> LoadJson(string jsonPath)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(jsonPath, Encoding.UTF8));
        var list = new List<Message>();
        foreach (Dictionary<string, object> o in (System.Collections.ArrayList)root["messages"])
        {
            var m = new Message
            {
                context = (string)o["context"] ?? "",
                source = (string)o["source"] ?? "",
                comment = o.ContainsKey("comment") ? (string)o["comment"] ?? "" : "",
            };
            foreach (object t in (System.Collections.ArrayList)o["translations"]) m.translations.Add((string)t);
            list.Add(m);
        }
        return list;
    }

    static void Build(string jsonPath, string templatePath, string outPath)
    {
        List<Message> messages = LoadJson(jsonPath).Where(m => m.translations.Count > 0 && m.translations.All(t => !string.IsNullOrEmpty(t))).ToList();
        QmFile template = Read(templatePath);

        var body = new MemoryStream();
        var hashes = new List<KeyValuePair<uint, uint>>();
        foreach (Message m in messages)
        {
            hashes.Add(new KeyValuePair<uint, uint>(ElfHash(m.source, m.comment), (uint)body.Position));
            foreach (string t in m.translations) WriteString(body, TagTranslation, Encoding.BigEndianUnicode.GetBytes(t));
            WriteString(body, TagSourceText, Encoding.UTF8.GetBytes(m.source));
            WriteString(body, TagComment, Encoding.UTF8.GetBytes(m.comment));
            // No context tag means "any context": used for strings whose QML file is unknown.
            if (m.context.Length > 0) WriteString(body, TagContext, Encoding.UTF8.GetBytes(m.context));
            body.WriteByte(TagEnd);
        }
        // Lookup is a binary search on hash; equal hashes must stay in message order.
        var sorted = hashes.Select((h, i) => new { h, i }).OrderBy(x => x.h.Key).ThenBy(x => x.i).Select(x => x.h).ToList();

        using (var f = File.Create(outPath))
        {
            f.Write(Magic, 0, Magic.Length);
            byte[] lang = Encoding.UTF8.GetBytes(template.language);
            WriteString(f, SecLanguage, lang);
            f.WriteByte(SecHashes);
            WriteU32(f, (uint)(sorted.Count * 8));
            foreach (var h in sorted) { WriteU32(f, h.Key); WriteU32(f, h.Value); }
            byte[] msgs = body.ToArray();
            WriteString(f, SecMessages, msgs);
            if (template.numerusRules != null) WriteString(f, SecNumerus, template.numerusRules);
        }
        Console.WriteLine("wrote " + outPath + ": " + messages.Count + " messages, language " + template.language);
    }

    // Simulates QTranslator's lookup for every expected message.
    static int Check(string qmPath, string jsonPath)
    {
        byte[] b = File.ReadAllBytes(qmPath);
        int pos = 16, hashStart = -1, hashLen = 0, msgStart = -1, msgLen = 0;
        while (pos < b.Length)
        {
            byte tag = b[pos]; int len = (int)ReadU32(b, pos + 1);
            if (tag == SecHashes) { hashStart = pos + 5; hashLen = len; }
            if (tag == SecMessages) { msgStart = pos + 5; msgLen = len; }
            pos += 5 + len;
        }
        QmFile parsed = Read(qmPath);
        var byKey = parsed.messages.GroupBy(m => m.context + "\u0001" + m.source + "\u0001" + m.comment).ToDictionary(g => g.Key, g => g.First());
        int n = hashLen / 8, ok = 0, bad = 0;
        foreach (Message want in LoadJson(jsonPath).Where(m => m.translations.Count > 0 && m.translations.All(t => !string.IsNullOrEmpty(t))))
        {
            uint h = ElfHash(want.source, want.comment);
            int lo = 0, hi = n - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                uint mh = ReadU32(b, hashStart + mid * 8);
                if (mh < h) lo = mid + 1; else { if (mh == h) found = mid; hi = mid - 1; }
            }
            Message got;
            bool match = found >= 0 && byKey.TryGetValue(want.context + "\u0001" + want.source + "\u0001" + want.comment, out got) && got.translations.SequenceEqual(want.translations);
            if (match) ok++; else { bad++; if (bad <= 10) Console.WriteLine("MISS: [" + want.context + "] " + want.source); }
        }
        Console.WriteLine("check: " + ok + " ok, " + bad + " missing");
        return bad == 0 ? 0 : 3;
    }
}
