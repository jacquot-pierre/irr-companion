using System;
using System.Text;

namespace IRRPresetTool
{
    /// <summary>
    /// Incursion Red River share codes: "IRR1-" + base64(compact JSON, no padding) + "-" + checksum.
    /// Checksum: djb2-xor over the base64 text, 4 base-36 digits, least significant first.
    /// </summary>
    public static class ShareCode
    {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static string Checksum(string payload)
        {
            uint h = 5381;
            foreach (char c in payload) h = unchecked((h * 33) ^ c);
            var sb = new StringBuilder(4);
            for (int i = 0; i < 4; i++) { sb.Append(Alphabet[(int)(h % 36)]); h /= 36; }
            return sb.ToString();
        }

        public static string Encode(JObj doc)
        {
            string json = Json.Write(doc);
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=');
            return "IRR1-" + payload + "-" + Checksum(payload);
        }

        public static JObj Decode(string code)
        {
            code = (code ?? "").Trim();
            // Tolerate line breaks / spaces introduced by chat apps.
            code = code.Replace("\r", "").Replace("\n", "").Replace(" ", "").Replace("\t", "");
            if (code.Length == 0) throw new FormatException("Paste a code first.");
            if (!code.StartsWith("IRR", StringComparison.Ordinal))
                throw new FormatException("That is not a share code: it should start with IRR1-.");
            int first = code.IndexOf('-');
            int last = code.LastIndexOf('-');
            if (first < 0 || last <= first) throw new FormatException("This code is damaged (it was probably cut short when copied).");
            string head = code.Substring(0, first);
            string payload = code.Substring(first + 1, last - first - 1);
            string chk = code.Substring(last + 1);
            if (head == "IRR2")
                throw new FormatException("IRR2 codes are not supported yet by this tool (only IRR1).");
            if (head != "IRR1") throw new FormatException("Unknown share code version '" + head + "'.");
            if (Checksum(payload) != chk)
                throw new FormatException("This code is damaged (checksum mismatch). It was probably cut short when copied.");
            string b64 = payload.Replace('-', '+').Replace('_', '/');
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var obj = Json.Parse(json) as JObj;
            if (obj == null) throw new FormatException("This code does not contain a preset.");
            return obj;
        }
    }
}
